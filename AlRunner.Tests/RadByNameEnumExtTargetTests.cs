// RadByNameEnumExtTargetTests — a delta must not damage the surface of objects it did not
// touch.
//
// A `--watch` cycle compiles only the changed AL objects, and strips those objects out of
// the packaged `ModuleDefinition` so the supplied syntax binds instead of the stale
// serialized copy (BcCompiler.Rad.cs, `ModuleDefinitionOps.WithoutObjects`) — with one
// deliberate exception: an EXTENSION object is never stripped, because what it contributes
// is only visible on its target (`ModuleDefinitionOps.WithoutObjects`, exempting
// `RadObjectKey.IsExtension`).
//
// Which OTHER objects get pulled into the cycle and re-emitted from source is decided by
// `changedSurfaces` in `BcCompiler.DeltaCompile`, and that filter admits only codeunits and
// the id-less kinds. A modified Enum never enters it, so an enumextension whose serialized
// `TargetObject` names that enum is never rebound — it keeps resolving the enum's merged
// surface against a packaged module whose copy of the enum this same cycle just removed.

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The by-name reference carried by an enumextension's <c>TargetObject</c>: `enumextension …
/// extends "EnumExt Base"` serializes the target as the STRING "EnumExt Base", and that
/// string — together with the values the extension contributes to it — is resolved against
/// the packaged module every time something asks the base enum's merged surface for a
/// member the extension, not the base enum itself, declares.
///
/// <para><b>Why the fixture is a triple, not a pair.</b> The damage needs three distinct
/// roles, and a two-object fixture goes green while proving nothing because it never asks
/// the damaged representation a question:</para>
/// <list type="bullet">
///   <item><b>X</b> — <c>enum 72080 "EnumExt Base"</c>. Edited (a value is added), therefore
///     in the delta's `modified` set, therefore STRIPPED from the packaged baseline
///     (`ModuleDefinitionOps.WithoutObjects` strips every modified object that is not itself
///     an extension). Being a plain enum — not a codeunit, not an id-less kind — it is also
///     invisible to `changedSurfaces`, so nothing downstream is told its surface moved.</item>
///   <item><b>V</b> — <c>enumextension 72081 "EnumExt Ext" extends "EnumExt Base"</c>.
///     UNTOUCHED, so it is never compiled from source in this cycle and is always resolved
///     from the packaged baseline instead. Its serialized <c>TargetObject</c> NAMES X, and
///     the value it contributes — <c>Extended</c> — exists only on the merged view of X that
///     a full compile builds from X's own values plus V's.</item>
///   <item><b>W</b> — <c>codeunit 72082 "EnumExt Consumer"</c>. Edited too, so it is in the
///     same delta and is recompiled from source, and its body binds
///     <c>"EnumExt Base"::Extended</c> — exactly the part of V's surface that names X.</item>
/// </list>
///
/// <para>Drop V and the delta is X+W, which bind against each other from source and never
/// consult a serialized extension surface at all — "Extended" would simply not exist and
/// the fixture would not compile. Drop W and nothing ever asks the merged X+V surface to
/// resolve "Extended", so the cycle reports success over a baseline that has quietly gone
/// wrong. All three, or it is not evidence.</para>
///
/// <para>This is a METHOD-BODY diagnostic: <c>BcCompiler.DeltaCompile</c> asks only
/// <c>GetDeclarationDiagnostics()</c> before codegen, so an
/// enum-value access that fails to bind inside W's body reaches the runner through
/// <c>rad.Emit(...)</c> (`:682-687`).</para>
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class RadByNameEnumExtTargetTests(BcEngineFixture engine)
{
    private const string ModuleName = "RAD ByName EnumExt Target";
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000013");

    /// <summary>The enum, the enumextension and the codeunit each generate C#; the fixture
    /// declares nothing else.</summary>
    private const int EmittedObjectCount = 3;

    /// <summary>
    /// Edit X and W in one cycle, never V, and the delta must report exactly what a full
    /// compile of the identical tree reports.
    ///
    /// <para>The edit is deliberately additive and local — one new value on the enum, one
    /// changed expression in the caller's body — so there is no legitimate reason for any
    /// diagnostic at all. What the delta does instead is strip X from the packaged module,
    /// read V back out of it unchanged, and hand W's <c>"EnumExt Base"::Extended</c> a
    /// target enum whose fresh, syntax-only recompilation never re-merges V's contributed
    /// value onto it; the access no longer resolves and the cycle fails with AL0132 on a
    /// tree that compiles.</para>
    ///
    /// <para>The oracle is the cold compile, never a hand-written expected list — a delta
    /// has to accept and reject exactly what a from-scratch build of the same source accepts
    /// and rejects. But "delta == cold" is also satisfied when BOTH sides are empty, so the
    /// cold side is asserted to be genuinely clean first. That is what makes the RED
    /// unambiguous: today, this test fails at the final <c>AssertMatchesColdCompile</c> call,
    /// because the delta invents an AL0132 that the cold compile of this same edited tree —
    /// asserted clean immediately above it — does not produce.</para>
    /// </summary>
    [SkippableFact]
    public void EditingAnEnumAndOneCaller_DoesNotBreakAnUntouchedEnumExtensionsContributedValue()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            "RadByNameEnumExtTarget", ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                // X: additive value. An enum is never admitted by `changedSurfaces`, so this
                // edit strips the enum from the packaged baseline and rebinds nothing else —
                // in particular, it does not rebind V, the enumextension that names it.
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, "EnumExtBase.Enum.al"),
                    "    value(2; Value2) { }",
                    "    value(2; Value2) { }\n    value(3; Value3) { }");

                // W: a body-only change, enough to put the caller in the same delta. V is not
                // touched by either edit and therefore stays on the packaged baseline.
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, "EnumExtConsumer.Codeunit.al"),
                    "exit(\"EnumExt Base\"::Extended.AsInteger());",
                    "exit(\"EnumExt Base\"::Extended.AsInteger() + 1);");

                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);

                // This has to stay on the delta path. A silent fallback to a whole-module
                // rebuild would hide the defect behind a correct — and slow — answer.
                Assert.False(delta.FullRebuild,
                    "editing an enum and one caller rebuilt the whole module instead of deltaing");

                // The premise, asserted before the comparison below so that a green run can
                // never mean "both sides were empty": adding an enum value and changing an
                // expression is legal AL, so a from-scratch build of the edited tree reports
                // nothing. Verified independently with the `al-runner --no-cache` fixture
                // check against a scratch copy carrying the identical edits before this test
                // was written; see the PR/task notes.
                var cold = RadByName.ColdCompile(tempRoot, ModuleName);
                Assert.True(cold.Emit.Diagnostics.Count == 0,
                    "the edited tree does not compile from scratch, so the fixture — not the "
                    + "delta path — is what this run measured:" + Environment.NewLine
                    + string.Join(Environment.NewLine, cold.Emit.Diagnostics));

                // The oracle. Expected to fail today: the delta reports an AL0132 against
                // W's "EnumExt Base"::Extended access that the (asserted-clean, above) cold
                // compile of this exact tree does not produce.
                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);

                // And the cycle really produced the objects it was told about, rather than
                // reporting success over an empty emit. Deliberately a containment check and
                // not an exact list: a correct fix is allowed to rebind the bystander from
                // source too, which would legitimately add a third emitted object, and pinning
                // the exact set here would fail the fix instead of the bug.
                var emitted = delta.Emit.Sources.Select(source => source.Name).ToArray();
                Assert.Contains("EnumExt Base", emitted);
                Assert.Contains("EnumExt Consumer", emitted);
            });
    }
}
