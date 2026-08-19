// RadByNameInterfaceCodeunitTests — a delta must not damage the surface of objects it did
// not touch.
//
// A `--watch` cycle compiles only the changed AL objects, and strips those objects out of
// the packaged `ModuleDefinition` so the supplied syntax binds instead of the stale
// serialized copy (BcCompiler.Rad.cs, `ModuleDefinitionOps.WithoutObjects`). Everything else
// in the app is still resolved FROM that packaged module — including a codeunit whose
// serialized surface names a stripped object BY NAME, such as its `ImplementedInterfaces`.
//
// Unlike a Table/Page/Enum/Report/Query — never admitted to `changedSurfaces` at all,
// because that filter accepts only codeunits and id-less kinds (`changedSurfaces`, in
// `BcCompiler.DeltaCompile`)
// — an `interface` IS an id-less kind, so in principle it IS eligible for `changedSurfaces`.
// That eligibility makes no difference here: `changedSurfaces` is computed only after
// `rad.Emit(...)` has already returned with zero diagnostics — it sits past the
// `if (diags.Count > 0) return …;` gate immediately after the emit call
// in `BcCompiler.DeltaCompile`. This shape's damage is a hard, blocking METHOD-BODY
// diagnostic on that very first emit attempt: the consumer's codeunit-to-interface
// assignment fails to bind before the delta ever reaches the widening logic that only runs
// once a compile has already succeeded. Widening `changedSurfaces` (or the reference graph
// it queries) cannot rescue a bystander whose emit has already failed.

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The by-name reference carried by a codeunit's <c>ImplementedInterfaces</c>: `codeunit …
/// implements "ByName Contract"` serializes the interface as the STRING "ByName Contract",
/// and that string is resolved against the packaged module every time the implementing
/// codeunit is read back from it instead of compiled from source.
///
/// <para>Measured on the npcore codebase: <b>626</b> occurrences of a codeunit's
/// <c>ImplementedInterfaces</c> naming an interface, all 626 already present in the RAD
/// reference graph at 100% fidelity — so the graph is not the gap. The gap is that nothing
/// asks the graph about a bystander whose own emit has already failed.</para>
///
/// <para><b>Why the fixture is a triple, not a pair.</b> The damage needs three distinct
/// roles, and a two-object fixture goes green while proving nothing because it never asks
/// the damaged representation a question:</para>
/// <list type="bullet">
///   <item><b>X</b> — <c>interface "ByName Contract"</c>. Edited (a second method is added),
///     therefore in the delta's `modified` set, therefore STRIPPED from the packaged
///     baseline (`ModuleDefinitionOps.WithoutObjects` strips every modified object that is not itself
///     an extension).</item>
///   <item><b>V</b> — <c>codeunit 72021 "ByName Impl" implements "ByName Contract"</c>.
///     UNTOUCHED, so it is never compiled from source in this cycle and is always resolved
///     from the packaged baseline instead. Its serialized <c>ImplementedInterfaces</c> NAMES
///     X.</item>
///   <item><b>W</b> — <c>codeunit 72022 "ByName Consumer"</c>. Edited too, so it is in the
///     same delta and is recompiled from source, and its body assigns V, held in a
///     <c>Codeunit "ByName Impl"</c> variable, to an <c>Interface "ByName Contract"</c>
///     variable — exactly the part of V's surface that names X.</item>
/// </list>
///
/// <para>Drop V and the delta is X+W, which bind against each other from source and never
/// consult a serialized <c>ImplementedInterfaces</c> at all. Drop W and nothing ever asks
/// whether V still satisfies the interface, so the cycle reports success over a baseline
/// that has quietly gone wrong. All three, or it is not evidence.</para>
///
/// <para>This is a METHOD-BODY diagnostic: <c>BcCompiler.DeltaCompile</c> asks only
/// <c>GetDeclarationDiagnostics()</c> before codegen, and V's own
/// declaration is never re-checked at all in this delta — it is not part of the supplied
/// syntax trees. It is W's <c>C := I;</c> assignment that fails to bind inside W's freshly
/// compiled body, reaching the runner through <c>rad.Emit(...)</c> (`:682-687`).</para>
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class RadByNameInterfaceCodeunitTests(BcEngineFixture engine)
{
    private const string ModuleName = "RAD ByName Interface Codeunit";
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000010");

    /// <summary>
    /// The codeunit V and the codeunit W each generate C#; the interface X does not — an
    /// <c>interface</c> is an id-less kind (<see cref="RadObjectKey.IsIdlessKind"/>) and
    /// <see cref="RadObjectKey.EmitsCode"/> is false for it, so it contributes symbols to the
    /// module but no generated source. The fixture declares nothing else.
    /// </summary>
    private const int EmittedObjectCount = 2;

    /// <summary>
    /// Edit X and W in one cycle, never V, and the delta must report exactly what a full
    /// compile of the identical tree reports.
    ///
    /// <para>The edit to X adds a second required method, <c>Ping(): Integer</c> — but V
    /// (untouched) already declares a matching <c>Ping</c>, as ordinary surface unrelated to
    /// the checked-in (one-method) interface. So the widened interface is satisfied by V's
    /// existing, unedited source, and W's edit is a body-only literal change. There is no
    /// legitimate reason for any diagnostic at all: a from-scratch build of the edited tree
    /// is legal AL start to finish, asserted below before the comparison that is the actual
    /// oracle. What the delta does instead is strip X from the packaged module, read V back
    /// out of it unchanged, and hand W's <c>C := I;</c> a codeunit that the compiler can no
    /// longer see implementing "ByName Contract" — the assignment fails to bind and the
    /// cycle fails with AL0122 on a tree that compiles cleanly cold.</para>
    ///
    /// <para>The oracle is the cold compile, never a hand-written expected list — a delta has
    /// to accept and reject exactly what a from-scratch build of the same source accepts and
    /// rejects. But "delta == cold" is also satisfied when BOTH sides are empty, so the cold
    /// side is asserted to be genuinely clean FIRST. That is what makes the RED unambiguous:
    /// expected to fail at the final <see cref="RadByName.AssertMatchesColdCompile"/> call,
    /// because the delta invents an AL0122 that the cold compile of this same edited tree —
    /// asserted clean immediately above it — does not produce.</para>
    /// </summary>
    [SkippableFact]
    public void EditingAnInterfaceAndOneCaller_DoesNotBreakAnUntouchedCodeunitsImplementsClause()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            "RadByNameInterfaceCodeunit", ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                // X: a second required method that V, untouched, already implements — so the
                // widened interface is satisfied entirely by source this cycle never
                // recompiles. An interface is an id-less kind and so, unlike a table or enum,
                // is in principle eligible for `changedSurfaces` if its own fingerprint moves
                // — this edit does move it (Methods gains an entry). It makes no difference:
                // `changedSurfaces` is computed only once `rad.Emit(...)` has already returned
                // clean, and this shape's damage is a hard method-body diagnostic on that very
                // first attempt (see the class doc and the top-of-file note).
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, "ByNameContract.Interface.al"),
                    "    procedure Describe(): Text;",
                    "    procedure Describe(): Text;\n    procedure Ping(): Integer;");

                // W: a body-only change, enough to put the consumer in the same delta. V is
                // not touched by either edit and therefore stays on the packaged baseline.
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, "ByNameConsumer.Codeunit.al"),
                    "exit(C.Describe());",
                    "exit(C.Describe() + '!');");

                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);

                // This has to stay on the delta path. A silent fallback to a whole-module
                // rebuild would hide the defect behind a correct — and slow — answer.
                Assert.False(delta.FullRebuild,
                    "editing an interface and one caller rebuilt the whole module instead of deltaing");

                // The premise, asserted before the comparison below so that a green run can
                // never mean "both sides were empty": adding a method to the interface that V
                // already implements, plus a literal change in the caller's body, is legal AL,
                // so a from-scratch build of the edited tree reports nothing.
                var cold = RadByName.ColdCompile(tempRoot, ModuleName);
                Assert.True(cold.Emit.Diagnostics.Count == 0,
                    "the edited tree does not compile from scratch, so the fixture — not the "
                    + "delta path — is what this run measured:" + Environment.NewLine
                    + string.Join(Environment.NewLine, cold.Emit.Diagnostics));

                // The oracle. Expected to fail today: the delta reports an AL0122 against W's
                // `C := I;` assignment that the (asserted-clean, above) cold compile of this
                // exact tree does not produce.
                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);

                // And the cycle really produced the object it was told about, rather than
                // reporting success over an empty emit. Deliberately a containment check and
                // not an exact list: a correct fix is allowed to rebind the bystander from
                // source too, which would legitimately add a second emitted object, and
                // pinning the exact set here would fail the fix instead of the bug.
                var emitted = delta.Emit.Sources.Select(source => source.Name).ToArray();
                Assert.Contains("ByName Consumer", emitted);
            });
    }
}
