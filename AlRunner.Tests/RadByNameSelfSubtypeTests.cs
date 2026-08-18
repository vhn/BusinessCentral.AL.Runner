// RadByNameSelfSubtypeTests — the sub-shape RadByNameSubtypeTests measured as CLEAN, and a real
// app measured as broken, reconciled: the difference is whether the app declares a namespace.
//
// `RadByNameSubtypeTests` pins `TypeDefinition.Subtype` — 13,610 occurrences on npcore, the
// widest by-name exposure there is — as needing no widening rule: the delta strips the table, the
// untouched bystander's `var t: Record "Subtype Target"` parameter still resolves, and the cycle
// matches a cold compile. `docs/delta-compile.md` concluded from that "it is not exposure at all".
//
// Editing npcore's `codeunit 6150705 "NPR POS Sale"` says the opposite: seven
// `AL0133: Argument N: cannot convert from 'Codeunit "NPR POS Sale"' to '__MissingTypeSymbol__'`,
// every one of them where the hub hands its own `_This: Codeunit "NPR POS Sale"` to an untouched
// codeunit's method, on a tree that compiles clean. Three candidate differences were measured one
// at a time — a Codeunit subtype instead of a Record one, the stripped object binding the damaged
// parameter itself instead of a third caller, and the app declaring no namespace. The first two
// are not the cause; both stay clean. The third is, and it is decisive.
//
// WHY, mechanically. `ReferenceSymbolHelper.ResolveApplicationObjectReference` resolves a
// serialized subtype through `ReferenceManager.GetObjectSymbolsByIdAcrossModules`, which asks the
// symbol's OWN containing module first. `RadReferenceModuleSymbol.BuildGlobalNamespace` decides
// which module that is, and it branches on whether the packaged module definition holds any
// namespaces:
//   * namespaces present → the packaged objects are re-parented onto the RAD module symbol, whose
//     symbol map merges the packaged definition with the SOURCE namespaces, so the stripped
//     object is found as syntax and the reference resolves;
//   * none → BC reuses the packaged module symbol's own global namespace verbatim, so the
//     bystander resolves against a module the delta just removed the referent from, and gets
//     `MissingTypeSymbol.Instance`.
// Namespaces arrived in AL 11. Every fixture in the by-name family declares one; npcore declares
// none in any of its 7,053 files. So the family has only ever measured the resolving path.

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The same by-name shape as <see cref="RadByNameSubtypeTests"/> over a Codeunit subtype —
/// `procedure Attach(Hub: Codeunit "Self Subtype Hub")` instead of
/// `procedure Take(var Target: Record "Subtype Target")` — run twice over one fixture: as
/// authored, and with every `namespace …;` declaration removed.
///
/// <para><b>Why the pair is the evidence.</b> Both runs compile the same objects, and
/// <see cref="RadByName.Run"/>'s <c>withoutNamespaces</c> flag is the only difference between
/// them, so the pair cannot be explained by two fixtures having drifted apart. Same source, same
/// edit, opposite verdicts.</para>
///
/// <para><b>Why the self-reference case needs no third object.</b> The harness rule is that a
/// two-object fixture never queries the bystander's damaged representation and so proves nothing.
/// Here the hub is both roles at once: it is stripped from the packaged baseline AND its own body
/// passes `_This` — a global of its own type — into the bystander's parameter. The question does
/// get asked; W has collapsed into X. That collapse is the sub-shape a triple cannot express, and
/// it is the shape npcore fails on.</para>
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class RadByNameSelfSubtypeTests(BcEngineFixture engine)
{
    private const string ModuleName = "RAD ByName Self Subtype";
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000018");

    /// <summary>All three codeunits generate C#; the fixture declares nothing else.</summary>
    private const int EmittedObjectCount = 3;

    /// <summary>One new procedure on the hub, uniquely named — the npcore benchmark's own edit.</summary>
    private static void AddAProcedureToTheHub(string tempRoot) => RadByName.Replace(
        RadByName.SourceFile(tempRoot, "SelfSubtypeHub.Codeunit.al"),
        "    procedure Start(): Integer",
        "    procedure Probe(): Integer\n    begin\n        exit(41);\n    end;\n\n"
        + "    procedure Start(): Integer");

    /// <summary>
    /// The control. With a namespace declared, editing the hub and nothing else deltas to one
    /// object and reports exactly what a cold compile of the same tree reports — which is what
    /// the whole by-name family has been measuring.
    ///
    /// <para>The edit is additive, so no existing member id moves, nothing is rebound, and the
    /// bystander genuinely stays on the packaged baseline. That is asserted rather than assumed:
    /// a cycle that re-emitted the bystander from source would never ask the packaged
    /// representation a question and would go green having measured nothing.</para>
    /// </summary>
    [SkippableFact]
    public void WithANamespace_EditingAHubThatPassesItself_LeavesTheBystandersParameterResolvable()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            "RadByNameSelfSubtype", ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                AddAProcedureToTheHub(tempRoot);

                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);

                Assert.False(delta.FullRebuild,
                    "adding one procedure to the hub rebuilt the whole module instead of deltaing");
                AssertColdCompileIsClean(tempRoot);
                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);

                var emitted = delta.Emit.Sources.Select(source => source.Name).ToArray();
                Assert.Equal(["Self Subtype Hub"], emitted);
            });
    }

    /// <summary>
    /// The same edit on the same objects with no namespace declared. The delta must still report
    /// what a cold compile reports — and today it does not: it invents
    /// `AL0133 … '__MissingTypeSymbol__'` at the site where the hub passes itself, which is
    /// npcore's failure exactly.
    ///
    /// <para>A cycle that cannot resolve the packaged baseline's own references has nothing
    /// truthful to say about the edit, and no widening reaches it — the damaged bystanders are
    /// `DirectUsersOf(everything stripped)`, and every file that widening adds strips one more
    /// object and damages the next ring of bystanders. So the required behaviour is the whole
    /// module, attributed: <see cref="RadEmitResult.FullRebuild"/> true and a reason on the
    /// cycle's notes, never a delta whose diagnostics a cold compile does not produce.</para>
    /// </summary>
    [SkippableFact]
    public void WithoutANamespace_EditingAHubThatPassesItself_TakesTheWholeModuleRatherThanInventAL0133()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            "RadByNameSelfSubtype", ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                AddAProcedureToTheHub(tempRoot);
                AssertColdCompileIsClean(tempRoot);

                RadCycleNotes.Drain();   // whatever the seed cycle left behind
                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
                var notes = string.Join(" | ", RadCycleNotes.Drain());

                // The claim, and the reason it is worth a test: a green run here must mean "the
                // cycle noticed it could not answer", not "the diagnostics happened to match".
                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);
                Assert.True(delta.FullRebuild,
                    "a delta that cannot resolve the packaged baseline's own references reported "
                    + "a delta anyway");
                // Named, not merely taken. The cost is the developer's to understand, and this
                // cause is invisible from the edit — the unresolvable reference is in a file the
                // cycle never touched.
                Assert.Contains("could not resolve", notes);
            },
            withoutNamespaces: true);
    }

    /// <summary>
    /// The control's control: same namespace-free tree, but the binder is a third edited codeunit
    /// passing a local rather than the hub passing itself. This is what rules the self-reference
    /// out as the cause — it breaks identically — and it is <see cref="RadByNameSubtypeTests"/>'
    /// triple with only the namespace removed.
    /// </summary>
    [SkippableFact]
    public void WithoutANamespace_EditingAHubAndAThirdCaller_TakesTheWholeModuleToo()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            "RadByNameSelfSubtype", ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                AddAProcedureToTheHub(tempRoot);
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, "SelfSubtypeCaller.Codeunit.al"),
                    "Attach(Hub) + 1", "Attach(Hub) + 2");
                AssertColdCompileIsClean(tempRoot);

                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);

                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);
                Assert.True(delta.FullRebuild,
                    "a delta that cannot resolve the packaged baseline's own references reported "
                    + "a delta anyway");
            },
            withoutNamespaces: true);
    }

    /// <summary>
    /// The premise every one of these tests rests on, asserted before the comparison so a green
    /// run can never mean "both sides were empty": adding a procedure is legal AL, so a
    /// from-scratch build of the edited tree reports nothing.
    /// </summary>
    private static void AssertColdCompileIsClean(string tempRoot)
    {
        var cold = RadByName.ColdCompile(tempRoot, ModuleName);
        Assert.True(cold.Emit.Diagnostics.Count == 0,
            "the edited tree does not compile from scratch, so the fixture — not the delta path — "
            + "is what this run measured:" + Environment.NewLine
            + string.Join(Environment.NewLine, cold.Emit.Diagnostics));
    }
}
