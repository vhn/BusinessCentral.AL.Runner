// RadByNameSelfSubtypeTests — the sub-shape RadByNameSubtypeTests measured as clean and a real app
// measured as broken, reconciled: BC chooses a different binder for a FILE without a namespace.
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
// parameter itself instead of a third caller, and the edited compilation unit declaring no
// namespace. The first two are not the cause; both stay clean. The third selects the broken path.
//
// WHY, mechanically. `BinderFactory.VisitCompilationUnitInternal` gives a namespace-free file
// `LegacyInContainerBinder`. Its by-name lookup asks `RadReferenceManager` for the plain packaged
// `ReferenceModuleSymbol` first. That copy can see only .alpackages dependencies, not this app's
// supplied source, so an untouched bystander's serialized subtype loses the changed object RAD
// stripped and becomes `MissingTypeSymbol.Instance`. A file with a namespace gets
// `NamespaceContainerBinder`, whose merged namespace lands on the `RadReferenceModuleSymbol` copy
// that can fall through to the source symbol map. Both copies exist; binder selection decides
// which wins. The repair puts the changed object's freshly compiled top-level definition back into
// the packaged copy and binds the same changed files once more.

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The same by-name shape as <see cref="RadByNameSubtypeTests"/> over a Codeunit subtype —
/// `procedure Attach(Hub: Codeunit "Self Subtype Hub")` instead of
/// `procedure Take(var Target: Record "Subtype Target")` — run over one fixture as authored,
/// with every `namespace …;` declaration removed, and with a mixed file set.
///
/// <para><b>Why the pair is the evidence.</b> Both runs compile the same objects, and
/// <see cref="RadByName.Run"/>'s <c>withoutNamespaces</c> flag is the only difference between
/// them, so the pair cannot be explained by two fixtures having drifted apart. Before the repair
/// the same source and edit produced opposite verdicts; now both must stay on the same narrow
/// delta path.</para>
///
/// <para>The mixed run keeps one file namespaced while the changed hub and its bystander are not,
/// so an app-wide namespace gate cannot pass it accidentally. It pins the actual rule: the
/// changed compilation unit chooses the binder.</para>
///
/// <para><b>Why the self-reference case needs no third object.</b> The harness rule is that a
/// two-object fixture never queries the bystander's damaged representation and so proves nothing.
/// Here the hub is both roles at once: it is stripped from the packaged baseline AND its own body
/// passes `_This` — a global of its own type — into the bystander's parameter. The question does
/// get asked; W has collapsed into X. That collapse is the sub-shape a triple cannot express, and
/// it is the shape npcore failed on.</para>
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
    /// The same edit on the same objects with no namespace declared. It must reach the SAME answer
    /// the namespaced control reaches: one object re-emitted, and exactly the diagnostics a cold
    /// compile of the same tree reports.
    ///
    /// <para>This used to assert the opposite — <see cref="RadEmitResult.FullRebuild"/> true, on the
    /// grounds that a cycle which cannot resolve the packaged baseline's own references has nothing
    /// truthful to say. It can resolve them now: the delta puts the stripped objects back into the
    /// packaged symbols carrying the surface this same compile just gave them, and binds again. So
    /// the correctness-preserving fallback is no longer the best available answer, and pinning it
    /// would pin the runner to the slower one.</para>
    ///
    /// <para><b>Both halves are asserted, and the second is the one that matters.</b>
    /// `AssertMatchesColdCompile` alone would go green on a whole-module rebuild — that is precisely
    /// what it used to do. The emitted-object set is what says the cycle stayed narrow.</para>
    /// </summary>
    [SkippableFact]
    public void WithoutANamespace_EditingAHubThatPassesItself_StillDeltasToTheOneEditedObject()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            "RadByNameSelfSubtype", ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                AddAProcedureToTheHub(tempRoot);
                AssertColdCompileIsClean(tempRoot);

                RadCycleNotes.Drain();          // whatever the seed cycle left behind
                RadCycleNotes.DrainRebinds();
                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
                var rebinds = string.Join(" | ", RadCycleNotes.DrainRebinds());

                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);
                Assert.False(delta.FullRebuild,
                    "adding one procedure to the hub rebuilt the whole module instead of deltaing");
                Assert.Equal(
                    ["Self Subtype Hub"],
                    delta.Emit.Sources.Select(source => source.Name).ToArray());

                // …and it got there by the repair, not by the break having quietly stopped
                // reproducing. Without this the test would still pass if the delta never needed a
                // second pass at all, which is a different claim from the one being made.
                Assert.Contains("namespace-free binder chose the packaged copy", rebinds);
                Assert.Contains("RAD stripped the changed target it names", rebinds);
            },
            withoutNamespaces: true);
    }

    /// <summary>
    /// Binder selection is per compilation unit, not per app. Leave the third codeunit namespaced
    /// while making the edited hub and its untouched bystander namespace-free: an app-wide
    /// "declares any namespace" gate would skip the repair and invent AL0133, while the per-file
    /// gate must still produce the same one-object delta as the two uniform controls above.
    /// </summary>
    [SkippableFact]
    public void MixedNamespaceApp_WhenTheChangedFileIsNamespaceFree_StillRepairsThatFilesBinder()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            "RadByNameSelfSubtype", ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                AddAProcedureToTheHub(tempRoot);
                AssertColdCompileIsClean(tempRoot);

                RadCycleNotes.Drain();
                RadCycleNotes.DrainRebinds();
                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
                var rebinds = string.Join(" | ", RadCycleNotes.DrainRebinds());

                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);
                Assert.False(delta.FullRebuild,
                    "a namespace elsewhere in the app hid the changed file's legacy binder");
                Assert.Equal(
                    ["Self Subtype Hub"],
                    delta.Emit.Sources.Select(source => source.Name).ToArray());
                Assert.Contains("namespace-free binder chose the packaged copy", rebinds);
            },
            prepareTree: tempRoot =>
            {
                RadByName.RemoveNamespaceDeclaration(
                    RadByName.SourceFile(tempRoot, "SelfSubtypeHub.Codeunit.al"));
                RadByName.RemoveNamespaceDeclaration(
                    RadByName.SourceFile(tempRoot, "SelfSubtypeLine.Codeunit.al"));
                Assert.StartsWith(
                    "namespace ",
                    File.ReadAllText(RadByName.SourceFile(
                        tempRoot, "SelfSubtypeCaller.Codeunit.al")));
            });
    }

    /// <summary>
    /// The control's control: same namespace-free tree, but the binder is a third edited codeunit
    /// passing a local rather than the hub passing itself. This is what ruled the self-reference out
    /// as the cause — it broke identically — and it is <see cref="RadByNameSubtypeTests"/>' triple
    /// with only the namespace removed. It must delta to the two edited objects and no more.
    /// </summary>
    [SkippableFact]
    public void WithoutANamespace_EditingAHubAndAThirdCaller_DeltasToBothEditedObjects()
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
                Assert.False(delta.FullRebuild,
                    "editing two codeunits rebuilt the whole module instead of deltaing");
                Assert.Equal(
                    ["Self Subtype Caller", "Self Subtype Hub"],
                    delta.Emit.Sources.Select(source => source.Name).Order(StringComparer.Ordinal)
                        .ToArray());
            },
            withoutNamespaces: true);
    }

    /// <summary>
    /// The test that pins WHICH surface the repair hands back, and the only one in this file that
    /// can tell the two candidates apart.
    ///
    /// <para>The repair reinserts the changed objects' definitions carrying the surface the CURRENT
    /// compile just produced. The obvious cheaper alternative — hand back the committed definition,
    /// i.e. simply do not strip — is indistinguishable from it for every other test here, because
    /// the repair only ever runs on a cycle whose surface did NOT move, and "did not move" makes the
    /// two definitions equal in almost every respect. Exactly one difference survives that filter:
    /// a member ADDED under a name the object did not already have is surface-stable by the
    /// member-level rule and is still absent from the committed definition.</para>
    ///
    /// <para>So this scenario adds `Probe()` to the hub and, in the same cycle, calls it from the
    /// caller. A namespace-free file resolves `Hub` through the packaged symbols, so the call binds
    /// only if what was put back is the new surface. Measured against a runner temporarily changed
    /// to reinsert the committed definition instead: this test fails with
    /// `AL0132 … does not contain a definition for 'Probe'` while every other test in the RAD suite
    /// stays green.</para>
    /// </summary>
    [SkippableFact]
    public void WithoutANamespace_ACallToAProcedureAddedThisCycle_BindsAgainstTheNewSurface()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            "RadByNameSelfSubtype", ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                AddAProcedureToTheHub(tempRoot);
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, "SelfSubtypeCaller.Codeunit.al"),
                    "exit(Line.Attach(Hub) + 1);",
                    "exit(Line.Attach(Hub) + Hub.Probe());");
                AssertColdCompileIsClean(tempRoot);

                RadCycleNotes.DrainRebinds();
                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
                var rebinds = string.Join(" | ", RadCycleNotes.DrainRebinds());

                // Through the repair — otherwise this measures the plain by-name path.
                Assert.Contains("namespace-free binder chose the packaged copy", rebinds);
                Assert.Contains("RAD stripped the changed target it names", rebinds);
                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);
                Assert.False(delta.FullRebuild,
                    "calling a procedure added in the same cycle rebuilt the whole module");
                Assert.Equal(
                    ["Self Subtype Caller", "Self Subtype Hub"],
                    delta.Emit.Sources.Select(source => source.Name).Order(StringComparer.Ordinal)
                        .ToArray());
            },
            withoutNamespaces: true);
    }

    /// <summary>
    /// The negative direction, and the reason the pair above cannot pass by the repair having made
    /// the delta blind. The same namespace-free edit, plus one call in the hub's new body to a
    /// procedure the untouched bystander does not have. The cycle must go through the repair — the
    /// `_This` reference still has to resolve — and still report the bad call, exactly as a cold
    /// compile of the same tree does.
    ///
    /// <para>Putting a stripped object's freshly compiled surface back into the packaged symbols is
    /// a repair for references that SHOULD resolve. It must not turn the bystander's surface into
    /// something that accepts anything, or the repair has bought green cycles on broken source.
    /// Asserting the rebind note is what proves the diagnostic came out of the repaired pass and
    /// not out of a first pass that gave up before reaching it.</para>
    ///
    /// <para><b>Why this one does not use the cold-compile oracle</b>, unlike every other test in
    /// the by-name family. A cold compile of a tree whose edited object has a body error does not
    /// report that error and stop: the full-compile path's emit retry EXCLUDES the object it could
    /// not emit, and every reference to it then reports `AL0185 … is missing` instead — measured
    /// here as three AL0185s where the delta reports one AL0132 naming the actual mistake. The two
    /// paths are both right and they are not comparable, so this test names the diagnostic it
    /// wants. That is a deliberate exception to <see cref="RadByName.AssertMatchesColdCompile"/>,
    /// confined to the one case that provokes the exclusion.</para>
    /// </summary>
    [SkippableFact]
    public void WithoutANamespace_ACallTheBystanderCannotSatisfy_IsStillReported()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            "RadByNameSelfSubtype", ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, "SelfSubtypeHub.Codeunit.al"),
                    "        exit(_Line.Attach(_This) + 1);",
                    "        exit(_Line.Attach(_This) + _Line.NoSuchProcedure());");

                RadCycleNotes.DrainRebinds();
                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
                var rebinds = string.Join(" | ", RadCycleNotes.DrainRebinds());

                // The repair ran — so `_This` did resolve, and what follows is the repaired pass
                // talking, not a first pass that stopped at the marker.
                Assert.Contains("namespace-free binder chose the packaged copy", rebinds);
                Assert.Contains("RAD stripped the changed target it names", rebinds);
                // …and it reports the real mistake, once, naming the member and the bystander.
                var diagnostic = Assert.Single(delta.Emit.Diagnostics.Distinct(StringComparer.Ordinal));
                Assert.Contains("AL0132", diagnostic);
                Assert.Contains(
                    "'Codeunit \"Self Subtype Line\"' does not contain a definition for "
                    + "'NoSuchProcedure'",
                    diagnostic);
                Assert.Empty(delta.Emit.Sources);
                Assert.False(delta.FullRebuild,
                    "a body error in the edited object bought a whole-module rebuild");
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
