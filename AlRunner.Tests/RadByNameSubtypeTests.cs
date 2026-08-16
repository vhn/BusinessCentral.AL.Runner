// RadByNameSubtypeTests — a delta must not damage the surface of objects it did not touch.
//
// A `--watch` cycle compiles only the changed AL objects, and strips those objects out of
// the packaged `ModuleDefinition` so the supplied syntax binds instead of the stale
// serialized copy (BcCompiler.Rad.cs, `ModuleDefinitionOps.WithoutObjects`). Everything
// else in the app is still resolved FROM that packaged module — including objects whose
// serialized surface refers to a stripped object BY NAME.
//
// Which other objects get pulled into the cycle and re-emitted from source is decided by
// `changedSurfaces`, and that filter admits only codeunits and the id-less kinds. A
// modified Table / Page / Enum / Report / Query never enters it, so nothing that names one
// of those by name is ever rebound — it keeps resolving against a module the delta just
// removed the referent from.

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The by-name reference carried by a method parameter's <c>TypeDefinition.Subtype</c>:
/// `procedure Take(var t: Record "Subtype Target")` serializes the table as the STRING
/// "Subtype Target", and that string is resolved against the packaged module every time the
/// owning codeunit is read back from it.
///
/// <para>Measured on the npcore codebase this is the widest of the seven by-name shapes —
/// <b>13,610</b> occurrences — so it is the one that decides whether a real app can trust a
/// watch cycle at all.</para>
///
/// <para><b>Why the fixture is a triple, not a pair.</b> The damage needs three distinct
/// roles, and a two-object fixture goes green while proving nothing because it never asks
/// the damaged representation a question:</para>
/// <list type="bullet">
///   <item><b>X</b> — <c>table 72000 "Subtype Target"</c>. Edited, therefore in the delta's
///     `modified` set, therefore STRIPPED from the packaged baseline. Being a table, it is
///     also invisible to `changedSurfaces`.</item>
///   <item><b>V</b> — <c>codeunit 72001 "Subtype Bystander"</c>. UNTOUCHED, so it is never
///     compiled from source in this cycle and is resolved from the packaged baseline
///     instead — and its serialized surface NAMES X.</item>
///   <item><b>W</b> — <c>codeunit 72002 "Subtype Caller"</c>. Edited too, so it is in the
///     same delta, and it binds to exactly the part of V's surface that names X.</item>
/// </list>
///
/// <para>Drop V and the delta is X+W, which bind against each other from source and never
/// consult a serialized surface. Drop W and nothing ever asks V's damaged parameter type to
/// resolve, so the cycle reports success over a baseline that has quietly gone wrong. All
/// three, or it is not evidence.</para>
///
/// <para>These are METHOD-BODY diagnostics: <c>BcCompiler.DeltaCompile</c> asks only
/// <c>GetDeclarationDiagnostics()</c> before codegen, so an overload that fails to bind
/// inside W's body reaches the runner through <c>rad.Emit(...)</c>.</para>
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class RadByNameSubtypeTests(BcEngineFixture engine)
{
    private const string ModuleName = "RAD ByName Subtype";
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000009");

    /// <summary>The table and both codeunits generate C#; the fixture declares nothing else.</summary>
    private const int EmittedObjectCount = 3;

    /// <summary>
    /// Edit X and W in one cycle, never V, and the delta must report exactly what a full
    /// compile of the identical tree reports.
    ///
    /// <para>The edit is deliberately additive and local — one new field on the table, one
    /// changed literal in the caller's body — so there is no legitimate reason for any
    /// diagnostic at all. What the delta does instead is strip X from the packaged module,
    /// read V back out of it, and hand W a `Take` whose parameter resolved to
    /// <c>'__MissingTypeSymbol__'</c>; the argument no longer matches and the cycle fails
    /// with AL0133 on a tree that compiles.</para>
    ///
    /// <para>The oracle is the cold compile, never a hand-written expected list — a delta
    /// has to accept and reject exactly what a from-scratch build of the same source accepts
    /// and rejects. But "delta == cold" is also satisfied when BOTH sides are empty, so the
    /// cold side is asserted to be genuinely clean first. That is what makes the RED
    /// unambiguous: the failure is the delta INVENTING a diagnostic that no full compile of
    /// this source produces, not the two sides merely disagreeing.</para>
    /// </summary>
    [SkippableFact]
    public void EditingATableAndOneCaller_DoesNotBreakAnUntouchedCodeunitsRecordParameter()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            "RadByNameSubtype", ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                // X: additive field. A table is never admitted by `changedSurfaces`, so this
                // edit strips the table from the packaged baseline and rebinds nothing.
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, "SubtypeTarget.Table.al"),
                    "        field(2; Amount; Decimal) { DataClassification = CustomerContent; }",
                    "        field(2; Amount; Decimal) { DataClassification = CustomerContent; }\n"
                    + "        field(3; Quantity; Decimal) { DataClassification = CustomerContent; }");

                // W: a body-only change, enough to put the caller in the same delta. V is not
                // touched by either edit and therefore stays on the packaged baseline.
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, "SubtypeCaller.Codeunit.al"),
                    "Take(Target) + 1", "Take(Target) + 2");

                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);

                // This has to stay on the delta path. A silent fallback to a whole-module
                // rebuild would hide the defect behind a correct — and slow — answer.
                Assert.False(delta.FullRebuild,
                    "editing a table and one caller rebuilt the whole module instead of deltaing");

                // The premise, asserted before the comparison below so that a green run can
                // never mean "both sides were empty": adding a field and changing a literal is
                // legal AL, so a from-scratch build of the edited tree reports nothing.
                var cold = RadByName.ColdCompile(tempRoot, ModuleName);
                Assert.True(cold.Emit.Diagnostics.Count == 0,
                    "the edited tree does not compile from scratch, so the fixture — not the "
                    + "delta path — is what this run measured:" + Environment.NewLine
                    + string.Join(Environment.NewLine, cold.Emit.Diagnostics));

                // The oracle.
                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);

                // And the cycle really produced the objects it was told about, rather than
                // reporting success over an empty emit. Deliberately a containment check and
                // not an exact list: a correct fix is allowed to rebind the bystander from
                // source too, which would legitimately add a third emitted object, and pinning
                // the exact set here would fail the fix instead of the bug.
                var emitted = delta.Emit.Sources.Select(source => source.Name).ToArray();
                Assert.Contains("Subtype Target", emitted);
                Assert.Contains("Subtype Caller", emitted);
            });
    }
}
