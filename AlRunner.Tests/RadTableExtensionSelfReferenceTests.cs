// RadTableExtensionSelfReferenceTests — the delta path binding a tableextension that
// reads its OWN fields through Rec.
//
// Found by running --watch over NP Retail: adding a field to
// `GeneralPostingSetup.TableExt.al` failed the cycle with three AL0132s
// ("'Record "General Posting Setup"' does not contain a definition for
// NPR_AchievedRevenueTicketAcc") — against the extension's own field, declared six lines
// above the trigger that reads it. The whole-compile path binds it; only the delta did not.
//
// The 20-object fixture missed it because both of its tableextension triggers happen to
// touch a BASE table field (`Rec.Description`). That is the narrower shape: an extension
// field reaches the base table's symbol only through the extension itself, which the delta
// strips from the packaged baseline before rebinding it from source.

using System.Reflection;
using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadTableExtensionSelfReferenceTests(BcEngineFixture engine)
{
    private const string ScenarioDir = "al-runner-rad-tableext-self";

    /// <summary>
    /// Baseline already contains the self-reference; the edit adds an unrelated field.
    /// This is npcore's exact shape.
    /// </summary>
    [SkippableFact]
    public void AddingAFieldToAnExtensionThatReadsItsOwnFields_StillDeltas()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var path = RadFixture.SourceFile(tempRoot, "RadPerfHeaderExtA.TableExt.al");

            // Seed a baseline in which the extension's own trigger reads its own field.
            RadFixture.ReplaceExactlyOnce(
                path,
                "Rec.Description := 'extension-a-v1';",
                "Rec.Description := Rec.\"Extension A\";");
            var baseline = RadFixture.Seed(tempRoot);

            // Field 71002 on target table RAD Perf Header — 71000/71001 are taken.
            RadFixture.ReplaceExactlyOnce(
                path,
                "field(71000; \"Extension A\"; Text[30])",
                """
                field(71002; "Extension A Note"; Text[30]) { DataClassification = SystemMetadata; }
                        field(71000; "Extension A"; Text[30])
                """);

            var delta = baseline.Cycle(tempRoot);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(["RAD Perf Header Ext A"],
                delta.Emit.Sources.Select(source => source.Name).ToArray());

            var assembly = RadFixture.AssembleAndLoad(baseline.Workspace, delta.Emit.Sources);
            delta.Commit(baseline.Workspace, assembly);
            baseline.AssertOwnership(assembly, ["TableExtension71000"]);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The strongest form, and the one that says whether the fix is real rather than a
    /// baseline lookup that happens to still hold the answer: the field being read is
    /// added by the SAME edit, so nothing outside the supplied syntax tree knows it exists.
    /// </summary>
    [SkippableFact]
    public void AddingAFieldAndReadingItInTheSameEdit_StillDeltas()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);

            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfHeaderExtA.TableExt.al"),
                "field(71000; \"Extension A\"; Text[30])",
                """
                field(71002; "Extension A Note"; Text[30]) { DataClassification = SystemMetadata; }
                        field(71000; "Extension A"; Text[30])
                """);
            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfHeaderExtA.TableExt.al"),
                "Rec.Description := 'extension-a-v1';",
                "Rec.Description := Rec.\"Extension A Note\";");

            var delta = baseline.Cycle(tempRoot);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(["RAD Perf Header Ext A"],
                delta.Emit.Sources.Select(source => source.Name).ToArray());

            var assembly = RadFixture.AssembleAndLoad(baseline.Workspace, delta.Emit.Sources);
            delta.Commit(baseline.Workspace, assembly);
            baseline.AssertOwnership(assembly, ["TableExtension71000"]);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The harder version of the same question, and the one the retained-definition
    /// approach is most likely to get wrong: TWO extensions on one table, edited in the
    /// same cycle, where one reads a field the other adds. Neither packaged definition
    /// knows about the new field — only the supplied trees do — so this is where "the
    /// packaged module is what the target table's symbol is built from" would bite.
    /// </summary>
    [SkippableFact]
    public void TwoExtensionsOnOneTable_EditedTogether_SeeEachOthersNewFields()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);

            // Ext A gains field 71002; Ext B's trigger — in the same cycle — reads it.
            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfHeaderExtA.TableExt.al"),
                "field(71000; \"Extension A\"; Text[30])",
                """
                field(71002; "Extension A Note"; Text[30]) { DataClassification = SystemMetadata; }
                        field(71000; "Extension A"; Text[30])
                """);
            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfHeaderExtB.TableExt.al"),
                "Rec.Description := 'extension-b-v1';",
                "Rec.Description := Rec.\"Extension A Note\";");

            var delta = baseline.Cycle(tempRoot);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(
                ["RAD Perf Header Ext A", "RAD Perf Header Ext B"],
                RadFixture.EmittedNames(delta));

            var assembly = RadFixture.AssembleAndLoad(baseline.Workspace, delta.Emit.Sources);
            delta.Commit(baseline.Workspace, assembly);
            baseline.AssertOwnership(assembly, ["TableExtension71000", "TableExtension71001"]);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The cost of leaving an extension's previous definition in the packaged baseline is
    /// that it could SHADOW the edit — so a field the developer just deleted would keep
    /// binding, and the cycle would go green against a schema that no longer exists. It
    /// must not: the supplied syntax tree is the authority for the object being rebound.
    /// </summary>
    [SkippableFact]
    public void RemovingAnExtensionField_DoesNotKeepBindingAgainstTheOldDefinition()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var path = RadFixture.SourceFile(tempRoot, "RadPerfHeaderExtA.TableExt.al");
            RadFixture.ReplaceExactlyOnce(
                path,
                "Rec.Description := 'extension-a-v1';",
                "Rec.Description := Rec.\"Extension A\";");
            var baseline = RadFixture.Seed(tempRoot);

            // Delete the field the trigger reads, keeping the read. The tree no longer
            // declares "Extension A"; only the previous packaged definition still does.
            RadFixture.ReplaceExactlyOnce(
                path,
                """
                field(71000; "Extension A"; Text[30])
                        {
                            DataClassification = SystemMetadata;

                            trigger OnValidate()
                            begin
                                Rec.Description := Rec."Extension A";
                            end;
                        }
                """,
                """
                field(71000; "Extension A Renamed"; Text[30])
                        {
                            DataClassification = SystemMetadata;

                            trigger OnValidate()
                            begin
                                Rec.Description := Rec."Extension A";
                            end;
                        }
                """);

            var delta = baseline.Cycle(tempRoot);
            Assert.False(delta.FullRebuild);
            Assert.Contains(delta.Emit.Diagnostics, d =>
                d.Contains("Extension A", StringComparison.Ordinal));
            Assert.Empty(delta.Emit.Sources);
            Assert.Throws<InvalidOperationException>(
                () => delta.Commit(baseline.Workspace, assembly: null));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The other direction: the edit itself introduces the self-reference. Same binding
    /// question, but now the baseline the delta strips from has no record of the read.
    /// </summary>
    [SkippableFact]
    public void IntroducingASelfReferenceInAnExtension_StillDeltas()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);

            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfHeaderExtA.TableExt.al"),
                "Rec.Description := 'extension-a-v1';",
                "Rec.Description := Rec.\"Extension A\";");

            var delta = baseline.Cycle(tempRoot);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(["RAD Perf Header Ext A"],
                delta.Emit.Sources.Select(source => source.Name).ToArray());

            var assembly = RadFixture.AssembleAndLoad(baseline.Workspace, delta.Emit.Sources);
            delta.Commit(baseline.Workspace, assembly);
            baseline.AssertOwnership(assembly, ["TableExtension71000"]);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
