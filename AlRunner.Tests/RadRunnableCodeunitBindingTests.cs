// RadRunnableCodeunitBindingTests — a delta must not break `CodeunitVar.Run(Rec)` on a
// codeunit it never touched.
//
// Found by running --watch over NP Retail. A body edit to `AdyenManagement.Codeunit.al`
// rebound its direct callers, and the cycle then died with
//
//     AdyenSetup.Page.al@332:53: error AL0126: No overload for method 'Run' takes 1
//     arguments. Candidates: built-in method 'Run()'
//
// against `AdyenRecreateRecDoc.Run(ReconHeader)` — code that compiles clean cold, in a file
// the edit never touched. `Run(Record)` exists on a codeunit variable exactly when the
// codeunit declares `TableNo`, so the diagnostic says the codeunit's symbol came back
// without one.
//
// The shape that produces it, and why the 20-object fixture cannot: three objects have to
// land on different sides of one delta.
//
//   * codeunit 6248336 declares `TableNo = "NPR Adyen Reconciliation Hdr"` and is NOT in the
//     delta — so its symbol is reconstructed from the packaged module definition, where
//     `TableNo` is stored as the table's NAME;
//   * that table IS in the delta (its file happens to reference the edited codeunit too), so
//     it is stripped from the packaged definition and supplied as source instead;
//   * the caller is also in the delta, and has to resolve `Run(Rec)` across that split.
//
// No fixture codeunit declared `TableNo` at all, so no fixture delta had ever split a
// `TableNo` target away from the codeunit naming it.

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadRunnableCodeunitBindingTests(BcEngineFixture engine)
{
    private const string ScenarioDir = "al-runner-rad-runnable-binding";

    /// <summary>
    /// Rearrange the fixture into NP Retail's shape before any baseline exists:
    ///
    /// <list type="bullet">
    /// <item>`RAD Perf Unrelated D` becomes a runnable codeunit — `TableNo` on RAD Perf
    ///   Header, `Access = Internal` like npcore's — and references nothing, so no surface
    ///   move can ever pull it into a delta;</item>
    /// <item>`RAD Perf Header`'s insert trigger calls RAD Perf Service, which makes its file a
    ///   direct user of the object the test edits — that is what drags the `TableNo` target
    ///   into the delta;</item>
    /// <item>`RAD Perf Caller` calls `Run(Rec)` on the runnable codeunit, and keeps its
    ///   existing call to `Service.Coerce` so it is dragged in by the same edit.</item>
    /// </list>
    /// </summary>
    private static void ArrangeRunnableShape(string tempRoot)
    {
        File.WriteAllText(
            RadFixture.SourceFile(tempRoot, "RadPerfUnrelatedD.Codeunit.al"),
            """
            namespace AlRunner.Tests.RadTwentyObject;

            codeunit 71005 "RAD Perf Unrelated D"
            {
                TableNo = "RAD Perf Header";
                Access = Internal;

                trigger OnRun()
                begin
                    Rec.Description := 'ran-by-runnable';
                end;

                procedure Value(): Integer
                begin
                    exit(105);
                end;
            }

            """);

        RadFixture.ReplaceExactlyOnce(
            RadFixture.SourceFile(tempRoot, "RadPerfHeader.Table.al"),
            """
                trigger OnInsert()
                begin
                    Description := 'header-v1';
                end;
            """,
            """
                trigger OnInsert()
                var
                    Service: Codeunit "RAD Perf Service";
                begin
                    Description := 'header-v1';
                    if Service.Value() = 0 then
                        Description := 'header-unreachable';
                end;
            """);

        RadFixture.ReplaceExactlyOnce(
            RadFixture.SourceFile(tempRoot, "RadPerfCaller.Codeunit.al"),
            """
                procedure Value(): Integer
                var
                    Service: Codeunit "RAD Perf Service";
                begin
                    exit(Service.Coerce(0));
                end;
            """,
            """
                procedure Value(): Integer
                var
                    Service: Codeunit "RAD Perf Service";
                    Runnable: Codeunit "RAD Perf Unrelated D";
                    Header: Record "RAD Perf Header";
                begin
                    Runnable.Run(Header);
                    exit(Service.Coerce(0));
                end;
            """);
    }

    /// <summary>
    /// The edit is a real callable-surface move (`Coerce`'s parameter type), so rebinding the
    /// direct callers is correct and must keep happening — this test is not about avoiding the
    /// rebind. It is about what the delta must ALSO pull in once it has: the codeunit whose
    /// `TableNo` names a table the rebind dragged into the change set.
    ///
    /// <para><b>What this test does and does not reproduce.</b> It reproduces the structural
    /// precondition exactly — the `TableNo` target table is stripped out of the packaged
    /// baseline while the codeunit naming it stays in — and asserts the fix's observable
    /// consequence, that the codeunit is rebound from source. It does NOT reproduce npcore's
    /// AL0126 at this scale: on the 20-object fixture the same split still binds clean, so
    /// before the fix this test fails on the missing object rather than on a diagnostic. The
    /// AL0126 itself was observed only on NP Retail, where codeunit 6248336 was confirmed
    /// present in the packaged definition with `TableNo` intact and only its target table
    /// (6150788) missing — see ModuleDefinitionOps.CodeunitsWithTableNo.</para>
    /// </summary>
    [SkippableFact]
    public void ASurfaceMoveThatDragsInATableNoTarget_StillBindsRunOnTheUntouchedCodeunit()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            ArrangeRunnableShape(tempRoot);
            var baseline = RadFixture.Seed(tempRoot);

            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "procedure Coerce(Input: Integer): Integer",
                "procedure Coerce(Input: Decimal): Integer");

            var delta = baseline.Cycle(tempRoot);

            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            // Four objects: the three the surface move reaches, plus RAD Perf Unrelated D —
            // which is in the delta for a different reason and is the reason the cycle binds
            // at all. Its `TableNo` names RAD Perf Header, and the delta strips that table out
            // of the packaged baseline, so the codeunit has to come from source or it comes
            // back with no `Run(Record)` overload. Asserted as an exact set rather than "no
            // diagnostics" so a fix that instead widened the delta towards a whole-module
            // rebuild would fail here.
            Assert.Equal(
                ["RAD Perf Caller", "RAD Perf Header", "RAD Perf Service", "RAD Perf Unrelated D"],
                RadFixture.EmittedNames(delta));
            Assert.True(RadFixture.EmittedNames(delta).Length < RadFixture.ObjectCount);

            var assembly = RadFixture.AssembleAndLoad(baseline.Workspace, delta.Emit.Sources);
            delta.Commit(baseline.Workspace, assembly);
            baseline.AssertOwnership(
                assembly, ["Codeunit71000", "Codeunit71001", "Codeunit71005", "Record71000"]);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The negative direction: a delta may not paper over a `Run(Rec)` call that a cold
    /// compile rejects. Dropping `TableNo` from the runnable codeunit removes the
    /// `Run(Record)` overload, so the same cycle must report AL0126 — the exact diagnostic
    /// the test above requires to be absent, here required to be present.
    ///
    /// <para>Without this, a "fix" that made the delta resolve `Run` unconditionally would
    /// pass the test above while turning a compile error into a green cycle.</para>
    /// </summary>
    [SkippableFact]
    public void RemovingTableNo_MakesTheDeltaReportTheSameAL0126_AColdCompileReports()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            ArrangeRunnableShape(tempRoot);
            var baseline = RadFixture.Seed(tempRoot);

            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfUnrelatedD.Codeunit.al"),
                "    TableNo = \"RAD Perf Header\";\n",
                string.Empty);
            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfUnrelatedD.Codeunit.al"),
                """
                    trigger OnRun()
                    begin
                        Rec.Description := 'ran-by-runnable';
                    end;
                """,
                """
                    trigger OnRun()
                    begin
                    end;
                """);

            var delta = baseline.Cycle(tempRoot);

            Assert.False(delta.FullRebuild);
            Assert.Contains(
                delta.Emit.Diagnostics,
                message => message.Contains("AL0126", StringComparison.Ordinal)
                    && message.Contains("'Run'", StringComparison.Ordinal));
            Assert.Empty(delta.Emit.Sources);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
