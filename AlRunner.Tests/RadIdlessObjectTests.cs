// RadIdlessObjectTests — the delta path against the AL object kinds that have no object id.
//
// `RadObjectKey` was `(Kind, Id)`, and six AL kinds do not fit that: `interface`,
// `controladdin`, `profile`, `pagecustomization`, `profileextension` and `entitlement` have
// no id at all. They broke it in two different ways, and only one of them was visible.
//
//   * A `profile` IS an `ISymbolWithId` — it satisfies every "does this have an id?" check
//     and then reports id 0, so every profile in an app keys as `Profile:0`. An app with two
//     of them produced two objects with one key, which threw out of the baseline snapshot
//     and left the app with no baseline at all. Silently: that failure is caught and logged.
//     `pagecustomization` and `profileextension` behave the same way.
//   * An `interface`, `controladdin` or `entitlement` is not returned by
//     `GetDeclaredApplicationObjectSymbols()` at all, so the workspace never recorded which
//     file declared it. Its file therefore looked untracked for the life of the process, and
//     every edit to it — including a comment — took the full-compile path.
//
// Measured on NP Retail, that was 84 of 7,339 files (60 interface, 16 controladdin, 8
// profile), each a guaranteed whole-module rebuild on any edit.
//
// The key now carries a Name, used as the discriminator exactly when there is no id, and
// id-less declarations the symbol API omits are read off the syntax tree instead. So the
// claim this suite makes is the same one the rest of the RAD suites make for id-bearing
// objects: one edit costs one object.
//
// Two of these kinds cannot be proved that way, and are proved against a cold compile of the
// same tree instead:
//
//   * An `entitlement` has NO serialized form — no `Entitlements` array in
//     `ModuleDefinition`, no `EntitlementDefinition` type — so there is no baseline copy to
//     read back. "The delta settled" would pass against an implementation that discarded the
//     object outright.
//   * A duplicate declaration is not observable in the change set either: the delta reported
//     it as a MODIFICATION of the other file's object and went green.
//
// So `ColdCompile` compiles the same tree with no baseline and the delta has to accept and
// reject exactly what it accepts and rejects.

using System.Reflection;
using AlRunner.Rad;
using Xunit;
using NavSymRef = Microsoft.Dynamics.Nav.CodeAnalysis.SymbolReference;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadIdlessObjectTests(BcEngineFixture engine)
{
    private const string ModuleName = "RAD Profile Fixture";
    private static readonly Guid AppId = Guid.Parse("5a1d0f27-7c64-4b53-9f2e-3d8b6c41a907");
    private static readonly Version AppVersion = new(1, 0, 0, 0);

    /// <summary>
    /// The fixture declares two pages, three codeunits, a permission set, two profiles, two
    /// controladdins, two interfaces, a pagecustomization, a profileextension and an
    /// entitlement. Six of those generate code: the pages, the codeunits and — the one that
    /// looks like metadata and is not — the permission set. The id-less kinds contribute
    /// symbols and metadata, never a C# source, which is exactly why an id-less delta emits
    /// nothing at all.
    /// </summary>
    private const int EmittedObjectCount = 6;

    private static readonly string Source = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "RadProfileApp"));

    /// <summary>
    /// Two profiles must be two objects. This is the case that used to cost the app its
    /// baseline outright, so it is asserted before anything else: without a baseline there
    /// is no delta path to test.
    /// </summary>
    [SkippableFact]
    public void TwoProfiles_AreTwoDistinctObjects_NotOneCollidingKey()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            var profiles = workspace.ObjectsIn(Path.Combine(tempRoot, "src", "RadProfileA.Profile.al"))
                .Concat(workspace.ObjectsIn(Path.Combine(tempRoot, "src", "RadProfileB.Profile.al")))
                .ToList();

            Assert.Equal(
                ["Profile:0:RAD Profile A", "Profile:0:RAD Profile B"],
                profiles.Select(Describe).Order(StringComparer.Ordinal).ToArray());
        });
    }

    /// <summary>
    /// The kinds the symbol API never reports. If the workspace does not know which file
    /// declares them, their files stay untracked forever and every edit is a full compile —
    /// which is what this asserted the opposite of before.
    /// </summary>
    [SkippableTheory]
    [InlineData("RadIdlessContract.Interface.al", "Interface:0:RAD Idless Contract")]
    [InlineData("RadIdlessAddin.ControlAddin.al", "ControlAddIn:0:RAD Idless Addin")]
    [InlineData("RadIdlessAddinB.ControlAddin.al", "ControlAddIn:0:RAD Idless Addin B")]
    [InlineData("RadProfileEntitlement.Entitlement.al", "Entitlement:0:RAD Profile Entitlement")]
    public void ObjectsTheSymbolApiOmits_AreStillTrackedToTheirFile(string file, string expected)
    {
        Run((compiler, workspace, tempRoot) =>
            Assert.Equal(
                [expected],
                workspace.ObjectsIn(Path.Combine(tempRoot, "src", file))
                    .Select(Describe).ToArray()));
    }

    /// <summary>
    /// Editing an id-less object is a delta like any other. It emits no C# — there is none
    /// to emit — so the observable delta is the change set and the fact that the cycle did
    /// not rebuild the module.
    ///
    /// <para>The page-customization row carries its consumer with it. That is not slack in the
    /// assertion: the `profileextension` naming the customization in `Customizations` was
    /// compiled against it, so when the customization's serialized surface moves the extension
    /// is rebound — the same rule that rebinds an interface's implementers. Naming the extra
    /// object explicitly is what keeps the row honest about the cost.</para>
    /// </summary>
    [SkippableTheory]
    [InlineData("RadProfileB.Profile.al", "Enabled = true;", "Enabled = false;",
        new[] { "Profile:0:RAD Profile B" })]
    [InlineData("RadIdlessAddin.ControlAddin.al", "'idless-addin.js'", "'idless-addin-2.js'",
        new[] { "ControlAddIn:0:RAD Idless Addin" })]
    [InlineData("RadProfileCust.PageCust.al", "Visible = false;", "Visible = true;",
        new[] { "PageCustomization:0:RAD Profile Cust", "ProfileExtension:0:RAD Profile Ext" })]
    [InlineData("RadProfileExt.ProfileExt.al", "Caption = 'RAD Profile Ext';", "Caption = 'RAD Profile Ext v2';",
        new[] { "ProfileExtension:0:RAD Profile Ext" })]
    public void EditingAnIdLessObject_IsADelta_NotAFullCompile(
        string file, string before, string after, string[] expected)
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", file), before, after);

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild,
                $"editing {file} rebuilt the whole module instead of deltaing one id-less object");
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal(expected,
                delta.Changes.Modified.Select(Describe).Order(StringComparer.Ordinal).ToArray());
            Assert.Empty(delta.Changes.Added);
            Assert.Empty(delta.Changes.Removed);
            // An id-less object owns no generated type, so a correct delta compiles no C# at
            // all. Emitting something here would mean an unrelated object was dragged in.
            Assert.Empty(delta.Emit.Sources);

            delta.Commit(workspace, null);
            Assert.True(compiler.EmitIncremental([tempRoot], ModuleName, workspace).NoChange);
        });
    }

    /// <summary>
    /// The interface case has a consumer, which is what makes it more than bookkeeping:
    /// widening the contract and its implementer in one cycle must rebind and re-emit the
    /// implementer, and nothing else. If the interface were left in the packaged baseline
    /// its old shape would shadow the edit and the implementer would fail to satisfy it.
    /// </summary>
    [SkippableFact]
    public void WideningAnInterface_ReEmitsItsImplementer_AndNothingElse()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadIdlessContract.Interface.al"),
                "    procedure Answer(): Integer;",
                "    procedure Answer(): Integer;\n    procedure Second(): Integer;");
            Replace(Path.Combine(tempRoot, "src", "RadIdlessImpl.Codeunit.al"),
                "        exit(42);\n    end;",
                "        exit(42);\n    end;\n\n    procedure Second(): Integer\n    begin\n        exit(43);\n    end;");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal(
                ["Codeunit:71402:RAD Idless Impl", "Interface:0:RAD Idless Contract"],
                delta.Changes.Modified.Select(Describe).Order(StringComparer.Ordinal).ToArray());
            // Only the implementer generates code; the interface contributes symbols.
            Assert.Equal(["RAD Idless Impl"], RadFixture.EmittedNames(delta));

            delta.Commit(workspace, RadFixture.AssembleAndLoad(workspace, delta.Emit.Sources));
            Assert.True(compiler.EmitIncremental([tempRoot], ModuleName, workspace).NoChange);
        });
    }

    /// <summary>
    /// The sharp version of the interface case, and the one that says whether the modified
    /// object was really stripped from the packaged baseline before `CreateForRad` bound the
    /// new source. NARROWING the contract — renaming its only method, and the implementer's
    /// with it — is only legal against the NEW interface. If the stale packaged definition
    /// still shadows it, the implementer no longer satisfies `Answer` and the cycle fails
    /// with an AL diagnostic. Widening cannot detect this: implementing a method the old
    /// contract did not ask for is not an error.
    /// </summary>
    [SkippableFact]
    public void RenamingAnInterfaceMethod_BindsAgainstTheNewContract_NotTheBaselineCopy()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadIdlessContract.Interface.al"),
                "procedure Answer(): Integer;", "procedure Renamed(): Integer;");
            Replace(Path.Combine(tempRoot, "src", "RadIdlessImpl.Codeunit.al"),
                "procedure Answer(): Integer", "procedure Renamed(): Integer");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                "the delta bound the implementer against the pre-edit interface still in the " +
                "packaged baseline:" + Environment.NewLine +
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(["RAD Idless Impl"], RadFixture.EmittedNames(delta));

            delta.Commit(workspace, RadFixture.AssembleAndLoad(workspace, delta.Emit.Sources));

            // "Rather than both shapes" is a claim about the merged baseline, so read it: one
            // element for the key, carrying the renamed member. A second copy would not fail
            // the compile — which of the two a later lookup answers with is decided by array
            // order — so a settled next cycle can be green over the stale definition.
            var merged = (NavSymRef.ModuleDefinition)workspace.Baseline!;
            var contract = new RadObjectKey("Interface", 0, "RAD IDLESS CONTRACT");
            Assert.Equal(1, ModuleDefinitionOps.CountObjects(merged, contract));
            Assert.Contains("Renamed", ModuleDefinitionOps.ObjectSurfaceFingerprint(merged, contract)!);

            // And the NEXT delta must bind against the renamed contract too — proving the
            // merged baseline carries the new shape rather than both shapes.
            Replace(Path.Combine(tempRoot, "src", "RadIdlessImpl.Codeunit.al"),
                "exit(42);", "exit(44);");
            var second = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(second.Emit.Diagnostics.Count == 0,
                "the merged baseline did not carry the renamed interface:" + Environment.NewLine +
                string.Join(Environment.NewLine, second.Emit.Diagnostics));
            Assert.False(second.FullRebuild);
            Assert.Equal(["RAD Idless Impl"], RadFixture.EmittedNames(second));
        });
    }

    /// <summary>
    /// The case both interface tests above are blind to: widening the contract and NOT
    /// touching the implementer. An interface is a binding contract, so its users have to be
    /// rebound when it moves — and they cannot be, unless the dependency graph records an
    /// edge onto an object that is not an application object. Without that edge the delta
    /// reported success, emitted nothing, and left the implementer bound to a contract it no
    /// longer satisfies. The correct answer is the compiler's: AL0582.
    /// </summary>
    [SkippableFact]
    public void WideningAnInterfaceAlone_RebindsItsImplementer_AndReportsTheBreak()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadIdlessContract.Interface.al"),
                "    procedure Answer(): Integer;",
                "    procedure Answer(): Integer;\n    procedure Second(): Integer;");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.Contains("AL0582", string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Empty(delta.Emit.Sources);
        });
    }

    /// <summary>
    /// The same claim against an identifier that contains a quote. AL escapes one by doubling
    /// it, and the compiler reports the decoded value — so a syntax reader that strips only
    /// the outer delimiters produces `RAD ""Quoted"" Contract` where the module definition
    /// says `RAD "Quoted" Contract`. Two keys for one object, and the delta then fails to
    /// strip its own baseline copy: the narrowed contract binds against the stale one and
    /// AL0582 comes back. Nothing else in the suite would notice, because every other name
    /// survives naive unquoting unchanged.
    /// </summary>
    [SkippableFact]
    public void RenamingAMethodOnAQuotedInterface_BindsAgainstTheNewContract()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadIdlessQuoted.Interface.al"),
                "procedure Answer(): Integer;", "procedure Renamed(): Integer;");
            Replace(Path.Combine(tempRoot, "src", "RadIdlessQuotedImpl.Codeunit.al"),
                "procedure Answer(): Integer", "procedure Renamed(): Integer");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                "the quoted interface was keyed differently by the syntax reader and the " +
                "module definition, so its baseline copy was never stripped:" +
                Environment.NewLine + string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(["RAD Idless Quoted Impl"], RadFixture.EmittedNames(delta));
        });
    }

    /// <summary>
    /// AL identifiers are case-insensitive, so a case-only rename is the SAME object. Keyed
    /// on the exact spelling it read as one addition plus one removal of an object that
    /// never went anywhere.
    /// </summary>
    [SkippableFact]
    public void RenamingAnIdLessObjectsCaseOnly_IsAModification_NotAnAddAndRemove()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadIdlessAddinB.ControlAddin.al"),
                @"controladdin ""RAD Idless Addin B""", @"controladdin ""RAD IDLESS ADDIN B""");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(["ControlAddIn:0:RAD IDLESS ADDIN B"],
                delta.Changes.Modified.Select(Describe).ToArray());
            Assert.Empty(delta.Changes.Added);
            Assert.Empty(delta.Changes.Removed);
        });
    }

    /// <summary>
    /// Deleting an id-less object has to leave the baseline, or the next compile still
    /// resolves a `controladdin` whose declaration is gone — the exact failure the old
    /// blanket full-compile fallback existed to avoid. Nothing references this one, so the
    /// delta is a pure removal.
    /// </summary>
    [SkippableFact]
    public void DeletingAnIdLessObject_RemovesItFromTheBaseline()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            File.Delete(Path.Combine(tempRoot, "src", "RadIdlessAddinB.ControlAddin.al"));

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal(["ControlAddIn:0:RAD Idless Addin B"],
                delta.Changes.Removed.Select(Describe).ToArray());
            Assert.Empty(delta.Changes.Modified);
            Assert.Empty(delta.Emit.Sources);

            delta.Commit(workspace, null);

            // The change list saying "removed" is not the same as it being gone. Microsoft's
            // symbol writer drops a removed object from the previous module by matching the
            // change element, and a serialized id-less element carries a synthesized id that
            // the element built from source cannot reproduce — so the deleted add-in survived
            // the merge and the next delta still resolved it, while every assertion above
            // still passed. Read the merged baseline itself.
            var baseline = (NavSymRef.ModuleDefinition)workspace.Baseline!;
            Assert.Null(ModuleDefinitionOps.ObjectSurfaceFingerprint(
                baseline, new RadObjectKey("ControlAddIn", 0, "RAD IDLESS ADDIN B")));

            // The survivor with the very similar name is still there — name-keyed identity
            // has to distinguish "RAD Idless Addin" from "RAD Idless Addin B", in the
            // baseline as well as in the workspace.
            Assert.NotNull(ModuleDefinitionOps.ObjectSurfaceFingerprint(
                baseline, new RadObjectKey("ControlAddIn", 0, "RAD IDLESS ADDIN")));
            Assert.Equal(["ControlAddIn:0:RAD Idless Addin"],
                workspace.ObjectsIn(Path.Combine(tempRoot, "src", "RadIdlessAddin.ControlAddin.al"))
                    .Select(Describe).ToArray());
            Assert.True(compiler.EmitIncremental([tempRoot], ModuleName, workspace).NoChange);
        });
    }

    /// <summary>
    /// The id-less kinds the symbol API DOES report — and reports with id 0, which is the
    /// trap. `pagecustomization` and `profileextension` satisfy every "is this an
    /// application object?" test and then have no id to be told apart by, so keying on the
    /// id alone left them unkeyable and their files untracked: any edit, comment included,
    /// rebuilt the whole module.
    /// </summary>
    [SkippableTheory]
    [InlineData("RadProfileCust.PageCust.al", "PageCustomization:0:RAD Profile Cust")]
    [InlineData("RadProfileExt.ProfileExt.al", "ProfileExtension:0:RAD Profile Ext")]
    public void IdLessObjectsTheSymbolApiReportsWithoutAnId_AreTrackedToTheirFile(
        string file, string expected)
    {
        Run((compiler, workspace, tempRoot) =>
            Assert.Equal(
                [expected],
                workspace.ObjectsIn(Path.Combine(tempRoot, "src", file))
                    .Select(Describe).ToArray()));
    }

    /// <summary>
    /// Modifying an id-less object must leave the merged baseline holding exactly ONE copy
    /// of it, carrying the post-edit shape.
    ///
    /// <para>This is the assertion the rest of the suite cannot make. Microsoft's symbol
    /// writer drops the pre-edit definition from the previous module by matching the change
    /// element it is handed, and a serialized id-less element carries a synthesized id that
    /// an element built from a compiler symbol cannot reproduce — so the match can miss and
    /// leave both copies. Nothing downstream complains: which copy a later compile resolves
    /// is decided by array order, so the cycle goes green either way and a fingerprint check
    /// answers with whichever came first. Count them.</para>
    /// </summary>
    [SkippableTheory]
    [InlineData("RadIdlessAddin.ControlAddin.al", "procedure Ping();",
        "procedure Pong();", "ControlAddIn", "RAD IDLESS ADDIN", "Pong")]
    [InlineData("RadProfileB.Profile.al", "Enabled = true;",
        "Enabled = false;", "Profile", "RAD PROFILE B", null)]
    [InlineData("RadProfileCust.PageCust.al", "Visible = false;",
        "Visible = true;", "PageCustomization", "RAD PROFILE CUST", null)]
    [InlineData("RadProfileExt.ProfileExt.al", "Caption = 'RAD Profile Ext';",
        "Caption = 'RAD Profile Ext v2';", "ProfileExtension", "RAD PROFILE EXT", "RAD Profile Ext v2")]
    public void ModifyingAnIdLessObject_LeavesOneBaselineCopy_CarryingTheNewShape(
        string file, string before, string after, string kind, string name, string? newShapeMarker)
    {
        Run((compiler, workspace, tempRoot) =>
        {
            var key = new RadObjectKey(kind, 0, name);
            var previous = ModuleDefinitionOps.ObjectSurfaceFingerprint(
                (NavSymRef.ModuleDefinition)workspace.Baseline!, key);
            Assert.NotNull(previous);

            Replace(Path.Combine(tempRoot, "src", file), before, after);
            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            delta.Commit(workspace, RadFixture.AssembleAndLoad(workspace, delta.Emit.Sources));

            var baseline = (NavSymRef.ModuleDefinition)workspace.Baseline!;
            Assert.Equal(1, ModuleDefinitionOps.CountObjects(baseline, key));

            // One copy is not enough on its own: one STALE copy also counts as one. The
            // surviving definition has to be the edited one.
            var current = ModuleDefinitionOps.ObjectSurfaceFingerprint(baseline, key);
            Assert.NotNull(current);
            Assert.NotEqual(previous, current);
            if (newShapeMarker != null) Assert.Contains(newShapeMarker, current);
        });
    }

    /// <summary>
    /// A `pagecustomization` is named by the profiles and profile extensions that apply it,
    /// so it is a binding contract exactly as an `interface` is. Renaming one WITHOUT
    /// touching the `profileextension` whose `Customizations` names it must rebind that
    /// extension and report the break — the failure mode the previous review caught for
    /// `interface`, where the delta reported success, emitted nothing, and left the consumer
    /// bound to a name that no longer existed.
    /// </summary>
    [SkippableFact]
    public void RenamingAPageCustomizationAlone_RebindsItsConsumer_AndReportsTheBreak()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadProfileCust.PageCust.al"),
                @"pagecustomization ""RAD Profile Cust""",
                @"pagecustomization ""RAD Profile Cust Renamed""");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.NotEmpty(delta.Emit.Diagnostics);
            Assert.Empty(delta.Emit.Sources);
            // The same break a cold compile of this tree reports — not merely "some error".
            Assert.Equal(
                DiagnosticCodes(ColdCompile(tempRoot).Emit.Diagnostics),
                DiagnosticCodes(delta.Emit.Diagnostics));
        });
    }

    /// <summary>
    /// Deleting one of the newly keyable kinds has to leave the merged baseline, not just the
    /// change list — the same claim <see cref="DeletingAnIdLessObject_RemovesItFromTheBaseline"/>
    /// makes for a controladdin. The profile extension that names the customization is deleted
    /// with it, because leaving it behind is (correctly) a dangling reference.
    /// </summary>
    [SkippableFact]
    public void DeletingAPageCustomizationAndItsConsumer_LeavesNoBaselineCopyOfEither()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            File.Delete(Path.Combine(tempRoot, "src", "RadProfileExt.ProfileExt.al"));
            File.Delete(Path.Combine(tempRoot, "src", "RadProfileCust.PageCust.al"));

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(
                ["PageCustomization:0:RAD Profile Cust", "ProfileExtension:0:RAD Profile Ext"],
                delta.Changes.Removed.Select(Describe).Order(StringComparer.Ordinal).ToArray());
            Assert.Empty(delta.Emit.Sources);

            delta.Commit(workspace, null);

            var baseline = (NavSymRef.ModuleDefinition)workspace.Baseline!;
            Assert.Equal(0, ModuleDefinitionOps.CountObjects(
                baseline, new RadObjectKey("PageCustomization", 0, "RAD PROFILE CUST")));
            Assert.Equal(0, ModuleDefinitionOps.CountObjects(
                baseline, new RadObjectKey("ProfileExtension", 0, "RAD PROFILE EXT")));
            Assert.True(compiler.EmitIncremental([tempRoot], ModuleName, workspace).NoChange);
        });
    }

    /// <summary>
    /// An `entitlement` is the one AL object kind with NO serialized representation at all —
    /// `ModuleDefinition` has no `Entitlements` array and the compiler has no
    /// `EntitlementDefinition` type. So there is no baseline copy to inspect, and "the delta
    /// settled" would pass against an implementation that silently discarded the object. The
    /// claim that survives that is equivalence with a cold compile: the delta must accept and
    /// reject exactly what a from-scratch build of the same tree accepts and rejects.
    ///
    /// <para>It is also the one kind BC will not let bind against the packaged baseline, so the
    /// app's permission sets are pulled into the same delta. Asserting that exact change set is
    /// the point: "not a full rebuild" on its own would also pass if every permission set in a
    /// 7,000-object app came along for one entitlement edit.</para>
    /// </summary>
    [SkippableFact]
    public void EditingAnEntitlement_IsADelta_OfItselfAndThePermissionSetsItMayName()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadProfileEntitlement.Entitlement.al"),
                "RoleType = Local;", "RoleType = Delegated;");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild,
                "editing an entitlement rebuilt the whole module");
            Assert.Equal(
                DiagnosticCodes(ColdCompile(tempRoot).Emit.Diagnostics),
                DiagnosticCodes(delta.Emit.Diagnostics));
            Assert.Equal(
                ["Entitlement:0:RAD Profile Entitlement", "PermissionSet:71410:RAD Profile Perms"],
                delta.Changes.Modified.Select(Describe).Order(StringComparer.Ordinal).ToArray());
            Assert.Empty(delta.Changes.Added);
            Assert.Empty(delta.Changes.Removed);
            // The permission set is the only object here that generates code; the entitlement
            // contributes nothing to the assembly.
            Assert.Equal(["RAD Profile Perms"], RadFixture.EmittedNames(delta));

            delta.Commit(workspace, RadFixture.AssembleAndLoad(workspace, delta.Emit.Sources));
            Assert.True(compiler.EmitIncremental([tempRoot], ModuleName, workspace).NoChange);
        });
    }

    /// <summary>
    /// The reverse direction, and the one no dependency graph here can express: rename the
    /// permission set and leave the entitlement alone. Because an entitlement produces no
    /// compiler symbol, no semantic model ever recorded that it names the permission set — so
    /// the delta re-emitted the renamed permission set, reported success, and left the
    /// entitlement pointing at a name that no longer exists. A cold compile says AL0185.
    /// </summary>
    [SkippableFact]
    public void RenamingAPermissionSet_RebindsTheEntitlementsThatMayNameIt()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            // Under 20 characters: AL0305 caps a permission set's identifier there, and a name
            // the compiler rejects outright would mask the dangling-reference break under test.
            Replace(Path.Combine(tempRoot, "src", "RadProfilePerms.PermissionSet.al"),
                @"permissionset 71410 ""RAD Profile Perms""",
                @"permissionset 71410 ""RAD Perms Renamed""");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.NotEmpty(delta.Emit.Diagnostics);
            Assert.Equal(
                DiagnosticCodes(ColdCompile(tempRoot).Emit.Diagnostics),
                DiagnosticCodes(delta.Emit.Diagnostics));
        });
    }

    /// <summary>
    /// Pointing an entitlement at a permission set that does not exist. A delta that quietly
    /// drops the entitlement instead of binding it would report success here, so this is the
    /// test that says the object is really being compiled and not merely bookkept.
    /// </summary>
    [SkippableFact]
    public void AnEntitlementNamingAMissingPermissionSet_ReportsWhatAColdCompileReports()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadProfileEntitlement.Entitlement.al"),
                @"ObjectEntitlements = ""RAD Profile Perms"";",
                @"ObjectEntitlements = ""RAD Profile Perms Missing"";");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            var cold = ColdCompile(tempRoot);
            Assert.Equal(DiagnosticCodes(cold.Emit.Diagnostics), DiagnosticCodes(delta.Emit.Diagnostics));
        });
    }

    /// <summary>
    /// Two entitlements must be two objects. The failure this guards against is the one that
    /// cost apps with two profiles their baseline outright: id-less objects of one kind all
    /// keying as `Kind:0` and colliding.
    /// </summary>
    [SkippableFact]
    public void TwoEntitlements_AreTwoDistinctObjects_NotOneCollidingKey()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            var second = Path.Combine(tempRoot, "src", "RadProfileEntitlementB.Entitlement.al");
            File.WriteAllText(second, """
                entitlement "RAD Profile Entitlement B"
                {
                    Type = Role;
                    RoleType = Local;
                    Id = 'RAD-PROFILE-ROLE-B';
                    ObjectEntitlements = "RAD Profile Perms";
                }
                """);

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(["Entitlement:0:RAD Profile Entitlement B"],
                delta.Changes.Added.Select(Describe).ToArray());
            // The permission set it names comes along, because an entitlement cannot bind one
            // from the packaged baseline — see EditingAnEntitlement_IsADelta_OfItselfAnd…
            Assert.Equal(["PermissionSet:71410:RAD Profile Perms"],
                delta.Changes.Modified.Select(Describe).ToArray());

            delta.Commit(workspace, RadFixture.AssembleAndLoad(workspace, delta.Emit.Sources));
            Assert.Equal(
                ["Entitlement:0:RAD Profile Entitlement", "Entitlement:0:RAD Profile Entitlement B"],
                workspace.AllObjects()
                    .Where(item => item.Key.Kind == "Entitlement")
                    .Select(Describe).Order(StringComparer.Ordinal).ToArray());
        });
    }

    /// <summary>
    /// A changed file that declares a key an UNTOUCHED file still owns is a duplicate
    /// declaration, and a cold build rejects it. The delta path asked only "does the module
    /// declare this key?" (<c>ws.Declares</c>), which is true — so the new declaration was
    /// classified as a MODIFICATION of the other file's object, the other file's copy was
    /// stripped from the packaged baseline, and the cycle reported success on a tree that
    /// does not compile.
    ///
    /// <para>Not specific to the newly keyable kinds — the codeunit row proves it — but
    /// load-bearing for them, because an entitlement has no module representation at all and
    /// this is the only place a duplicate one can be caught.</para>
    ///
    /// <para><b>The delta reports it rather than deferring to a full compile.</b> This used to
    /// hand the whole module over on the argument that only the compiler can say which of the
    /// two is the duplicate, and the assertion below was therefore an equality of full
    /// diagnostic sets — trivially satisfied, since the fallback literally ran the cold
    /// compile. The compiler's answer is always the same, so the whole-module compile bought a
    /// diagnostic and nothing else, for the most ordinary way a developer starts a new object:
    /// copying an existing <c>.al</c> file, intending to renumber and rename it afterwards.
    /// What must still hold is the part that is about the developer: the same AL code a cold
    /// build reports. What legitimately differs is arity — a cold build names both sides,
    /// while the delta parsed only the changed one and says so, naming the other by path.</para>
    /// </summary>
    [SkippableTheory]
    [InlineData("DupCodeunit.Codeunit.al", """
        namespace AlRunner.Tests.RadProfileApp;

        codeunit 71401 "RAD Profile Service Duplicate"
        {
            procedure Value(): Integer
            begin
                exit(999);
            end;
        }
        """)]
    [InlineData("DupInterface.Interface.al", """
        interface "RAD Idless Contract"
        {
            procedure Answer(): Integer;
        }
        """)]
    [InlineData("DupEntitlement.Entitlement.al", """
        entitlement "RAD Profile Entitlement"
        {
            Type = Role;
            RoleType = Local;
            Id = 'RAD-PROFILE-ROLE-DUP';
            ObjectEntitlements = "RAD Profile Perms";
        }
        """)]
    public void AChangedFileClaimingAKeyAnUntouchedFileOwns_DoesNotPassAsAModification(
        string file, string source)
    {
        Run((compiler, workspace, tempRoot) =>
        {
            var duplicatePath = Path.Combine(tempRoot, "src", file);
            File.WriteAllText(duplicatePath, source);

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            var cold = ColdCompile(tempRoot);
            Assert.NotEmpty(cold.Emit.Diagnostics);

            // The same AL code a cold build reports, from the file the developer just changed.
            Assert.Equal(DiagnosticCodes(cold.Emit.Diagnostics).Distinct().ToArray(),
                         DiagnosticCodes(delta.Emit.Diagnostics));
            var reported = Assert.Single(delta.Emit.Diagnostics);
            Assert.Contains(duplicatePath, reported, StringComparison.Ordinal);
            // …and it names the other side, which is the thing a cold build makes the developer
            // find for themselves.
            Assert.Contains("is already declared by", reported, StringComparison.Ordinal);

            // The behavioural claim: reported, not compiled around. Nothing was emitted, nothing
            // is committable, and the workspace still holds the baseline it had — so the save
            // that renumbers the copy is a delta, not a whole-module compile.
            Assert.False(delta.FullRebuild);
            Assert.False(delta.NoChange);
            Assert.Empty(delta.Emit.Sources);
            Assert.False(delta.CanCommit);
            Assert.True(workspace.HasBaseline);
        });
    }

    /// <summary>
    /// The ownership guard's blind spot: two files THIS cycle touched declaring one key. The
    /// guard asks whether the key's baseline owner is untouched, and here there is no owner at
    /// all — both files are new — so it passes, while `declaredNow[key] = objRef` collapses the
    /// two declarations into one and throws the other away.
    ///
    /// <para>It is nonetheless not a hole, and this suite exists to keep it from becoming one.
    /// `CreateForRad` is handed the syntax trees of every changed file, not the change model's
    /// object list, so both declarations reach the compiler and its own declaration pass reports
    /// the duplicate — the delta rejects the tree for the same reason and with the same AL id as
    /// a cold build, and the cycle commits nothing. The collapse is invisible because a key
    /// collision within one kind IS an AL duplicate: nothing legal can produce two objects with
    /// one `RadObjectKey`.</para>
    ///
    /// <para>So the assertion is deliberately about the diagnostics rather than the change set.
    /// The change set here is wrong by construction and does not matter; what matters is that
    /// no cycle in this state can advance the workspace, and that the developer is told the same
    /// thing a full build would tell them.</para>
    /// </summary>
    [SkippableTheory]
    [InlineData("DupPairA.Codeunit.al", "DupPairB.Codeunit.al", """
        namespace AlRunner.Tests.RadProfileApp;

        codeunit 71420 "RAD Dup Pair {N}"
        {
            procedure Value(): Integer
            begin
                exit({N});
            end;
        }
        """)]
    [InlineData("DupPairA.Interface.al", "DupPairB.Interface.al", """
        interface "RAD Dup Pair Contract"
        {
            procedure Answer{N}(): Integer;
        }
        """)]
    [InlineData("DupPairA.Entitlement.al", "DupPairB.Entitlement.al", """
        entitlement "RAD Dup Pair Entitlement"
        {
            Type = Role;
            RoleType = Local;
            Id = 'RAD-DUP-PAIR-{N}';
            ObjectEntitlements = "RAD Profile Perms";
        }
        """)]
    public void TwoFilesAddedInOneCycleDeclaringOneKey_DoNotCollapseIntoOne(
        string firstFile, string secondFile, string template)
    {
        Run((compiler, workspace, tempRoot) =>
        {
            File.WriteAllText(Path.Combine(tempRoot, "src", firstFile),
                template.Replace("{N}", "1", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(tempRoot, "src", secondFile),
                template.Replace("{N}", "2", StringComparison.Ordinal));

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            var cold = ColdCompile(tempRoot);
            Assert.NotEmpty(cold.Emit.Diagnostics);
            Assert.Equal(DiagnosticCodes(cold.Emit.Diagnostics), DiagnosticCodes(delta.Emit.Diagnostics));
            // Nothing may advance: a rejected cycle leaves the workspace on its last good state,
            // so the next save re-diffs the whole edit rather than half of it.
            Assert.Empty(delta.Emit.Sources);
            Assert.False(delta.CanCommit);
        });
    }

    /// <summary>
    /// Deleting the permission set outright, not renaming it. The docs claim both directions
    /// rebind an entitlement; only the rename was pinned, and the two arrive on different code
    /// paths — a rename keeps the permission set's id-based key and lands in `modified`, a
    /// deletion lands in `removed`.
    /// </summary>
    [SkippableFact]
    public void DeletingAPermissionSet_RebindsTheEntitlementsThatMayNameIt()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            File.Delete(Path.Combine(tempRoot, "src", "RadProfilePerms.PermissionSet.al"));

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.NotEmpty(delta.Emit.Diagnostics);
            Assert.Equal(
                DiagnosticCodes(ColdCompile(tempRoot).Emit.Diagnostics),
                DiagnosticCodes(delta.Emit.Diagnostics));
        });
    }

    /// <summary>
    /// A `permissionset` has a real object id, so it was already deltaable — and it is the
    /// one kind added to this fixture that generates C#, which makes it the regression guard
    /// for the emit-count check that decides whether a delta is trusted.
    /// </summary>
    [SkippableFact]
    public void EditingAPermissionSet_IsStillOneObject_AndStillEmitsCode()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadProfilePerms.PermissionSet.al"),
                "Assignable = true;", "Assignable = false;");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(["PermissionSet:71410:RAD Profile Perms"],
                delta.Changes.Modified.Select(Describe).ToArray());
            Assert.Equal(["RAD Profile Perms"], RadFixture.EmittedNames(delta));
        });
    }

    /// <summary>
    /// The cycle that PAYS for a lost baseline has to be the one that explains it. A cycle that
    /// cannot record a baseline reports the failure and then every later cycle compiles in full
    /// — but the later cycles knew nothing, so they rebuilt in silence and the reason scrolled
    /// past one cycle before the slowdown the developer noticed. Asserted over two real cycles:
    /// the reason is parked on the workspace and consumed by the compile that acts on it, not by
    /// the one that discovered it.
    ///
    /// <para>Two opposite failures are asserted with it, because both are ways of turning the
    /// mechanism into noise: a parked reason must be consumed ONCE, and it must not survive a
    /// cycle that succeeded. Either way it ends up attached to an unrelated full compile.</para>
    /// </summary>
    [SkippableFact]
    public void AParkedFullCompileReason_IsReportedByTheCycleThatPaysForIt_AndOnlyOnce()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            const string parked = "the baseline snapshot failed (probe)";

            // A parked reason must not survive a cycle that succeeded. Park one while the
            // workspace still HAS a baseline: the cycle deltas, so nothing consumes the reason,
            // and committing has to retire it — otherwise it is reported against whatever
            // unrelated full compile happens next, however many cycles later.
            workspace.PendingFullCompileReason = parked;
            Replace(Path.Combine(tempRoot, "src", "RadProfileService.Codeunit.al"),
                "exit(140);", "exit(141);");
            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.Equal(parked, workspace.PendingFullCompileReason);
            delta.Commit(workspace, RadFixture.AssembleAndLoad(workspace, delta.Emit.Sources));
            Assert.Null(workspace.PendingFullCompileReason);

            // The state a failed snapshot leaves behind: armed against the current reference
            // surface, no baseline, and a reason already known. Reached by invalidating and
            // parking directly, because forcing the snapshot itself to throw needs a malformed
            // module no fixture can express.
            workspace.Invalidate("probe: drop the baseline");
            workspace.PendingFullCompileReason = parked;
            Assert.False(workspace.HasBaseline);

            AlRunner.Rad.RadCycleNotes.Drain();
            var paying = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(paying.FullRebuild);
            Assert.Contains(parked, string.Join(" | ", AlRunner.Rad.RadCycleNotes.Drain()));
            Assert.Null(workspace.PendingFullCompileReason);

            paying.Commit(workspace, RadFixture.AssembleAndLoad(workspace, paying.Emit.Sources));
            Assert.True(workspace.HasBaseline);

            // Consumed once. A reason that stuck would be reported against every unrelated full
            // compile from then on — the same defect in the other direction, and the one a
            // "park it and forget it" implementation lands in.
            workspace.Invalidate("probe: drop the baseline again");
            AlRunner.Rad.RadCycleNotes.Drain();
            var later = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(later.FullRebuild);
            Assert.DoesNotContain(parked, string.Join(" | ", AlRunner.Rad.RadCycleNotes.Drain()));
        });
    }

    /// <summary>
    /// The regression guard: an app that declares id-less objects must still delta its
    /// ordinary ones by id, one object at a time.
    /// </summary>
    [SkippableFact]
    public void AnOrdinaryEdit_InAnAppWithIdLessObjects_IsStillOneObject()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadProfileService.Codeunit.al"),
                "exit(140);", "exit(141);");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal(["RAD Profile Service"], RadFixture.EmittedNames(delta));
            Assert.Equal(["Codeunit:71401:RAD Profile Service"],
                delta.Changes.Modified.Select(Describe).ToArray());
        });
    }

    /// <summary>Seed a committed baseline over a private copy, then hand it to the scenario.</summary>
    private void Run(Action<BcCompiler, RadWorkspace, string> scenario)
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = Copy();
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(AppId, "AlRunner Tests", AppVersion);
            var workspace = new RadWorkspace(ModuleName, tempRoot);
            var compiler = new BcCompiler();

            var seed = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(seed.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, seed.Emit.Diagnostics));
            Assert.True(seed.FullRebuild);
            Assert.Equal(EmittedObjectCount, seed.Emit.Sources.Count);
            Assert.True(seed.CanCommit,
                "the first compile produced no committable baseline — the snapshot threw");
            seed.Commit(workspace, Load(workspace, seed.Emit.Sources));
            Assert.True(workspace.HasBaseline);

            scenario(compiler, workspace, tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>`Kind:Id:Name` — the whole identity, so a name-keyed object is legible.</summary>
    private static string Describe(RadObjectRef obj) => $"{obj.Key.Kind}:{obj.Key.Id}:{obj.Name}";

    /// <summary>
    /// Compile <paramref name="tempRoot"/> from scratch into a workspace with no baseline —
    /// the same tree, the same compiler, no delta path. This is the only available oracle for
    /// an object the module definition does not represent: whatever a cold build says about
    /// the tree is what the delta has to say about it.
    /// </summary>
    private static RadEmitResult ColdCompile(string tempRoot) =>
        new BcCompiler().EmitIncremental(
            [tempRoot], ModuleName, new RadWorkspace(ModuleName, tempRoot));

    /// <summary>
    /// Diagnostics reduced to their sorted AL ids, after collapsing byte-identical repeats.
    ///
    /// <para>The repeats are the full-compile path's, not information: a cold compile of a
    /// tree with one dangling `ObjectEntitlements` reference reports the same AL0185, at the
    /// same location, twice. Distinct locations survive the collapse, so "cold found this
    /// break in four places and the delta found it in one" still fails.</para>
    /// </summary>
    private static string[] DiagnosticCodes(IEnumerable<string> diagnostics) => diagnostics
        .Distinct(StringComparer.Ordinal)
        .Select(text => System.Text.RegularExpressions.Regex.Match(text, @"\bAL\d{4}\b"))
        .Select(match => match.Success ? match.Value : "no-al-code")
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string Copy()
    {
        var destination = Path.Combine(
            Path.GetTempPath(), "al-runner-rad-profile", Guid.NewGuid().ToString("N"));
        foreach (var source in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(Source, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        return destination;
    }

    private static void Replace(string path, string before, string after)
    {
        var source = File.ReadAllText(path);
        Assert.Equal(1, source.Split(before, StringSplitOptions.None).Length - 1);
        File.WriteAllText(path, source.Replace(before, after, StringComparison.Ordinal));
    }

    private static Assembly Load(RadWorkspace workspace, IReadOnlyList<EmittedSource> sources)
    {
        var compiled = new BcAssembler().Compile(workspace.NextAssemblyName(), sources);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Errors));
        return Assembly.Load(compiled.AssemblyBytes!);
    }
}
