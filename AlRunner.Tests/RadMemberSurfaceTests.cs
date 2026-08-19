// RadMemberSurfaceTests — the member-level surface diff, from both sides.
//
// `changedSurfaces` in BcCompiler.DeltaCompile decides which untouched files a cycle drags in,
// and through `RadWorkspaceUpdate.MovedSurfaces` which of the OTHER apps in a bundle rebind
// too. It used to be one string compare over the whole serialized codeunit, which meant every
// edit that touched a codeunit at all — a new global, a reordered procedure, a procedure under
// a name nothing had ever called — re-emitted its complete caller set. On NP Retail that is
// 313 objects and 22–54 s for one added procedure on `NPR POS Session`.
//
// The replacement asks a narrower question: did anything a CALLER was compiled against change?
// This suite is that question's two edges.
//
//   * The WIN edge — edits that provably cannot move a call site, which must now cost exactly
//     one object: a global variable added, procedures reordered.
//   * The SAFETY edge — the four contract changes the member ID cannot see (access,
//     attributes, parameter names, the return type's subtype), a member removed, and the
//     overload hazard, all of which must still rebind.
//
// The overload half lives next door in RadSameAppOverloadTests / RadSameAppOverloadWatchTests,
// which measure it at the delta and at the running AL test respectively; nothing here repeats
// it. What this suite adds on top is the RECURSION step — a caller that a library edit widened
// gets recompiled, and the delta immediately re-asks whether ITS surface moved. That question
// is asked against the two producers' known disagreement, so the caller here carries an
// argument-less attribute deliberately.
//
// The hand-built module definitions at the bottom are not a shortcut around the fixture: they
// reach the fail-closed paths (a duplicated key, an id-less member, an object present twice)
// that no compilable AL tree can produce, and they are the only place the codeunit rule and the
// interface rule can be shown answering DIFFERENTLY to one identical edit.

using AlRunner.Rad;
using Xunit;
using NavSymRef = Microsoft.Dynamics.Nav.CodeAnalysis.SymbolReference;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadMemberSurfaceTests(BcEngineFixture engine)
{
    private const string Fixture = "RadMemberSurface";
    private const string ModuleName = "RAD Member Surface";
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000017");

    /// <summary>Library, caller and transitive caller; the fixture declares nothing else.</summary>
    private const int EmittedObjectCount = 3;

    private const string LibFile = "MemberLib.Codeunit.al";
    private const string Lib = "RAD Member Lib";
    private const string Caller = "RAD Member Caller";
    private const string Outer = "RAD Member Outer";

    private static readonly RadObjectKey LibKey = new("Codeunit", 72400);

    /// <summary>The two library procedures whole-block edits below move, exactly as declared.</summary>
    private const string TagProcedure = """
            procedure Tag(Prefix: Text): Text
            begin
                exit(Prefix + '-TAG');
            end;
        """;

    private const string IdsProcedure = """
            procedure Ids(): List of [Integer]
            var
                Found: List of [Integer];
            begin
                Found.Add(1);
                exit(Found);
            end;
        """;

    /// <summary>
    /// A codeunit's globals are not part of anything a caller can bind to — AL has no syntax
    /// for reading another object's variables — yet they ARE serialized into its symbol, so
    /// under the whole-object compare adding one re-emitted every caller of the object.
    ///
    /// <para>This is the exclusion's own RED → GREEN, and it needs one: an exclusion is the one
    /// kind of rule that can be "passed" by doing nothing at all. So the test proves the edit is
    /// visible before it asserts that the delta ignores it — the global reaches the serialized
    /// surface, the whole-object fingerprint therefore moves, and only the member-level
    /// comparison says the surface did not. Without those three lines this would still pass
    /// against an edit that failed to insert anything.</para>
    ///
    /// <para>Its negative direction is <see cref="AnIdInvisibleContractChange_StillRebindsTheCaller"/>:
    /// on the same object, in the same cycle shape, a change to a MEMBER still costs the caller.
    /// A rule that dropped too much would pass here and fail there.</para>
    /// </summary>
    [SkippableFact]
    public void AddingAGlobalVariable_DoesNotRebindTheCaller()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(Fixture, ModuleName, AppId, EmittedObjectCount, (compiler, ws, tempRoot) =>
        {
            var committed = (NavSymRef.ModuleDefinition)ws.Baseline!;
            Assert.DoesNotContain(
                "MemberCache", ModuleDefinitionOps.ObjectSurfaceFingerprint(committed, LibKey)!);

            RadByName.Replace(
                RadByName.SourceFile(tempRoot, LibFile),
                "    procedure Pick(Seed: Decimal): Text",
                """
                    var
                        MemberCache: Integer;

                    procedure Pick(Seed: Decimal): Text
                """);

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, ws);
            AssertCleanDelta(delta);
            Assert.Equal([Lib], RadFixture.EmittedNames(delta));

            // …and the edit really did land on the surface the old rule compared. Both halves
            // read off a cold compile of the edited tree, so this is the same producer on both
            // sides and the only variable is the source.
            var edited = (NavSymRef.ModuleDefinition)
                RadByName.ColdCompile(tempRoot, ModuleName).WorkspaceUpdate!.Baseline;
            Assert.Contains(
                "MemberCache", ModuleDefinitionOps.ObjectSurfaceFingerprint(edited, LibKey)!);
            Assert.NotEqual(
                ModuleDefinitionOps.ObjectSurfaceFingerprint(committed, LibKey),
                ModuleDefinitionOps.ObjectSurfaceFingerprint(edited, LibKey));

            var comparison = ModuleDefinitionOps.CompareObjectSurface(committed, edited, LibKey);
            Assert.Null(comparison.FailedClosedBecause);
            Assert.False(comparison.Moved,
                "a global variable added to a codeunit still reads as a moved callable surface");
        });
    }

    /// <summary>
    /// Declaration order is not part of the contract either. `CalculateMethodId` hashes the
    /// method's own name, return kind and parameters — never its position — so moving a
    /// procedure moves no id and no call site, but it does move the serialized `Methods` array
    /// and therefore the whole-object fingerprint.
    ///
    /// <para>This is what "compare the members as a MULTISET" buys, stated as a measurement.
    /// It is also the cheapest possible demonstration that the diff really is per-member: a
    /// per-position comparison of the same array would report every member from the move point
    /// on as changed.</para>
    /// </summary>
    [SkippableFact]
    public void ReorderingProcedures_DoesNotRebindTheCaller()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(Fixture, ModuleName, AppId, EmittedObjectCount, (compiler, ws, tempRoot) =>
        {
            var source = RadByName.SourceFile(tempRoot, LibFile);
            RadByName.Replace(source, IdsProcedure + Environment.NewLine, string.Empty);
            RadByName.Replace(
                source,
                "    procedure Pick(Seed: Decimal): Text",
                IdsProcedure + Environment.NewLine + Environment.NewLine
                    + "    procedure Pick(Seed: Decimal): Text");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, ws);
            AssertCleanDelta(delta);
            Assert.Equal([Lib], RadFixture.EmittedNames(delta));

            // The reorder happened, and it is visible where the old rule looked: the two
            // serializations differ, and they differ ONLY in the order of the member array.
            var edited = (NavSymRef.ModuleDefinition)
                RadByName.ColdCompile(tempRoot, ModuleName).WorkspaceUpdate!.Baseline;
            var committed = (NavSymRef.ModuleDefinition)ws.Baseline!;
            Assert.NotEqual(
                ModuleDefinitionOps.ObjectSurfaceFingerprint(committed, LibKey),
                ModuleDefinitionOps.ObjectSurfaceFingerprint(edited, LibKey));
            Assert.False(
                ModuleDefinitionOps.CompareObjectSurface(committed, edited, LibKey).Moved);
        });
    }

    /// <summary>
    /// The four edits that leave a member's id bit-identical while changing what a caller is
    /// entitled to assume, plus the removal that retires an id outright. Each must still rebind
    /// the direct caller.
    ///
    /// <para>Why they are grouped: they are the reason this rule compares the whole member
    /// element instead of the member id. `MethodSymbol.CalculateMethodId` hashes the
    /// upper-cased name, the return type's BARE `NavTypeKind` and per parameter
    /// `(index, IsVar, NavTypeKind)` — so access modifiers, attributes, parameter names and the
    /// return type's subtype or type ARGUMENTS are all invisible to it. A delta keyed on
    /// "did any id move?" would report every one of these as an unchanged surface.</para>
    ///
    /// <para>`List of [Integer]` → `List of [Text]` is the sharpest of them: same method name,
    /// same parameter list, same `NavTypeKind` for the return (List), and a caller assigning the
    /// result now has the wrong element type.</para>
    /// </summary>
    public static IEnumerable<object[]> IdInvisibleContractChanges()
    {
        yield return
        [
            "an argument-less attribute added",
            "    procedure Tag(Prefix: Text): Text",
            """
                [NonDebuggable]
                procedure Tag(Prefix: Text): Text
            """,
        ];
        yield return
        [
            "a procedure made internal",
            "    procedure Tag(Prefix: Text): Text",
            "    internal procedure Tag(Prefix: Text): Text",
        ];
        yield return
        [
            "a parameter renamed",
            TagProcedure,
            """
                procedure Tag(Head: Text): Text
                begin
                    exit(Head + '-TAG');
                end;
            """,
        ];
        yield return
        [
            "a generic return type argument retyped",
            IdsProcedure,
            """
                procedure Ids(): List of [Text]
                var
                    Found: List of [Text];
                begin
                    Found.Add('one');
                    exit(Found);
                end;
            """,
        ];
        yield return ["a procedure removed", IdsProcedure + Environment.NewLine, string.Empty];
    }

    [SkippableTheory]
    [MemberData(nameof(IdInvisibleContractChanges))]
    public void AnIdInvisibleContractChange_StillRebindsTheCaller(
        string scenario, string before, string after)
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(Fixture, ModuleName, AppId, EmittedObjectCount, (compiler, ws, tempRoot) =>
        {
            RadByName.Replace(RadByName.SourceFile(tempRoot, LibFile), before, after);

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, ws);
            AssertCleanDelta(delta);
            // The direct caller, and only the direct caller — over-triggering here would be as
            // wrong as under-triggering, just louder.
            Assert.Equal([Caller, Lib], RadFixture.EmittedNames(delta));
            Assert.DoesNotContain(Outer, RadFixture.EmittedNames(delta));

            // …and the id-blindness this case exists for, asserted rather than described: the
            // member set is keyed identically on both sides for the four non-removal cases, so
            // an implementation that compared ids would see nothing at all.
            Assert.True(
                ModuleDefinitionOps.CompareObjectSurface(
                    (NavSymRef.ModuleDefinition)ws.Baseline!,
                    (NavSymRef.ModuleDefinition)
                        RadByName.ColdCompile(tempRoot, ModuleName).WorkspaceUpdate!.Baseline,
                    LibKey).Moved,
                $"{scenario} left the library's callable surface reading as unchanged");
        });
    }

    /// <summary>
    /// The recursion step, which is where a one-hop rebind quietly becomes a cascade.
    ///
    /// <para>A library edit widens the cycle, so the caller's file is recompiled — and the
    /// delta then asks, of the CALLER this time, whether its own surface moved. If that second
    /// question answers wrongly the caller's callers come too, and so on; a fixture with only
    /// two objects can never see it. The caller here carries `[NonDebuggable]`, whose
    /// `Arguments` serialize as `null` from the converter and `[]` after the round trip, so the
    /// second question is asked against the exact shape that reads as a changed member if the
    /// diff is built on the raw serialization instead of the canonicalised one.</para>
    ///
    /// <para>The edit is the overload, because that is the widening whose CAUSE is entirely in
    /// the library: the caller's own file is byte-identical, so its presence in the emitted set
    /// is the delta's decision and nothing else.</para>
    /// </summary>
    [SkippableFact]
    public void WideningTheCaller_DoesNotThenWidenItsOwnCaller()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(Fixture, ModuleName, AppId, EmittedObjectCount, (compiler, ws, tempRoot) =>
        {
            RadByName.Replace(
                RadByName.SourceFile(tempRoot, LibFile),
                "    procedure Pick(Seed: Decimal): Text",
                """
                    procedure Pick(Seed: Integer): Text
                    begin
                        exit('INTEGER');
                    end;

                    procedure Pick(Seed: Decimal): Text
                """);

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, ws);
            AssertCleanDelta(delta);
            var emitted = RadFixture.EmittedNames(delta);
            Assert.Equal([Caller, Lib], emitted);
            Assert.True(emitted.Length < EmittedObjectCount,
                "the widened caller widened its own callers — one hop became a cascade");
        });
    }

    /// <summary>
    /// The kind discrimination, on one identical edit: a method added under a name new to the
    /// object is free on a CODEUNIT and breaking on an INTERFACE.
    ///
    /// <para>The asymmetry is the point and it is not symmetry-breaking for its own sake. A
    /// codeunit's caller baked one member id per call site, so a member nothing could have
    /// called cannot invalidate it. An interface method is a CONFORMANCE obligation: adding one
    /// makes every implementor that does not declare it stop compiling, and no implementor is
    /// in the delta's change set. So the id-less kinds keep the all-or-nothing rule.</para>
    ///
    /// <para>Hand-built rather than compiled because the two halves must differ in NOTHING but
    /// the kind — same member names, same ids, same addition — which a pair of AL fixtures
    /// cannot promise.</para>
    /// </summary>
    [Fact]
    public void AddingAMethodUnderANewName_IsSafeOnACodeunit_AndBreakingOnAnInterface()
    {
        var codeunit = ModuleDefinitionOps.CompareObjectSurface(
            CodeunitModule(Method("Pick", 11), Method("Tag", 12)),
            CodeunitModule(Method("Pick", 11), Method("Tag", 12), Method("Fresh", 13)),
            LibKey);
        Assert.Null(codeunit.FailedClosedBecause);
        Assert.False(codeunit.Moved);

        var contract = ModuleDefinitionOps.CompareObjectSurface(
            InterfaceModule(Method("Pick", 11), Method("Tag", 12)),
            InterfaceModule(Method("Pick", 11), Method("Tag", 12), Method("Fresh", 13)),
            RadObjectKey.For("Interface", 0, "Contract"));
        Assert.Null(contract.FailedClosedBecause);
        Assert.True(contract.Moved,
            "a method added to an interface no longer rebinds its implementors");
    }

    /// <summary>
    /// The same hand-built pair with the addition under an EXISTING name, so the codeunit rule's
    /// "false" above is a decision and not a constant.
    /// </summary>
    [Fact]
    public void AddingAnOverloadUnderAnExistingName_MovesTheCodeunitSurface()
    {
        var comparison = ModuleDefinitionOps.CompareObjectSurface(
            CodeunitModule(Method("Pick", 11), Method("Tag", 12)),
            CodeunitModule(Method("Pick", 11), Method("Tag", 12), Method("pick", 13)),
            LibKey);
        Assert.Null(comparison.FailedClosedBecause);
        // Case-insensitively: AL identifiers are, so `pick` joins `Pick`'s overload set.
        Assert.True(comparison.Moved);
    }

    /// <summary>
    /// Fail-closed class 2 — the object is unique and readable, but its member list cannot be
    /// keyed. Here "closed" means the WHOLE-OBJECT compare answers, and specifically that the
    /// answer is not hardcoded to "moved".
    ///
    /// <para>Both directions in one test, because either alone is satisfiable by a constant. A
    /// duplicated member key that appears IDENTICALLY on both sides is genuinely an unchanged
    /// object — the fallback compares the same single pair of elements over strictly MORE than
    /// the member diff looks at, so two byte-identical serializations mean nothing moved — and
    /// the same duplication over a surface that also lost a member must say it moved.</para>
    ///
    /// <para>Returning "moved" unconditionally here was considered and rejected: it would rebind
    /// a hub codeunit's whole caller set on every cycle for as long as the odd member existed,
    /// which is the cascade this rule exists to remove. What is not permitted either way is
    /// silence — the reason comes back for the cycle to print, per
    /// `.claude/rules/loud-failures.md`.</para>
    /// </summary>
    [Fact]
    public void AnUnkeyableMemberList_FallsBackToTheWholeObjectCompare()
    {
        var unchanged = ModuleDefinitionOps.CompareObjectSurface(
            CodeunitModule(Method("Pick", 11), Method("Pick", 11)),
            CodeunitModule(Method("Pick", 11), Method("Pick", 11)),
            LibKey);
        Assert.Contains("two members keyed `Pick`#11", unchanged.FailedClosedBecause);
        Assert.Contains("comparing the whole serialized object", unchanged.FailedClosedBecause);
        Assert.False(unchanged.Moved);

        var moved = ModuleDefinitionOps.CompareObjectSurface(
            CodeunitModule(Method("Pick", 11), Method("Pick", 11), Method("Tag", 12)),
            CodeunitModule(Method("Pick", 11), Method("Pick", 11)),
            LibKey);
        Assert.Contains("two members keyed `Pick`#11", moved.FailedClosedBecause);
        Assert.True(moved.Moved);

        // The other way a member resists keying: no id at all, so it cannot be told from an
        // overload of the same name. Never keyed on the name alone.
        var idless = ModuleDefinitionOps.CompareObjectSurface(
            CodeunitModule(Method("Pick", 11)),
            CodeunitModule(Method("Pick", 11), Method("Ghost", null)),
            LibKey);
        Assert.Contains("holds a member (`Ghost`) with no integer id", idless.FailedClosedBecause);
        Assert.True(idless.Moved);
    }

    /// <summary>
    /// Fail-closed class 1 — the object cannot be located unambiguously. Unlike class 2 this
    /// does NOT get the whole-object fallback, and the reason is the dangerous ordering below.
    ///
    /// <para>Two serialized copies of one object mean whichever the array lists first decides
    /// every answer about it — the exact condition `ModuleDefinitionOps.CountObjects` exists to
    /// let the RAD suites assert on, and a real one: it is what a delta that failed to strip its
    /// own pre-edit definition produces. `ObjectSurfaceFingerprint` takes the FIRST match, so
    /// with the STALE copy listed ahead of the re-emitted one it compares equal to the committed
    /// baseline and reads as "unchanged" — leaving every caller dispatching the pre-edit shape,
    /// green, with no diagnostic. That is the arrangement asserted first here, and it is the one
    /// a fallback to the whole-object compare gets wrong.</para>
    ///
    /// <para>The identical-copies arrangement is asserted too, at "moved": uniqueness is a
    /// precondition of the comparison, not a heuristic to apply when the copies happen to
    /// disagree. And the same holds for the id-less kinds, which never reach the member diff at
    /// all — a duplicated interface would otherwise suppress its implementors' rebind by the
    /// same mechanism.</para>
    /// </summary>
    [Fact]
    public void AnObjectSerializedTwice_ReadsAsMoved_WithoutConsultingArrayOrder()
    {
        // The stale copy first, the re-emitted one (with an added overload) second.
        var staleFirst = new NavSymRef.ModuleDefinition
        {
            Name = "hand-built",
            Codeunits =
            [
                Codeunit(Method("Pick", 11)),
                Codeunit(Method("Pick", 11), Method("Pick", 12)),
            ],
        };
        var ambiguous = ModuleDefinitionOps.CompareObjectSurface(
            CodeunitModule(Method("Pick", 11)), staleFirst, LibKey);
        Assert.True(ambiguous.Moved,
            "a stale duplicate listed first answered for the object, so the overload added after "
            + "it read as an unchanged surface and its callers were left dispatching the "
            + "pre-edit shape");
        Assert.Contains("more than one serialized copy", ambiguous.FailedClosedBecause);
        Assert.Contains("treating the surface as moved", ambiguous.FailedClosedBecause);

        // Identical copies, and on the committed side rather than the merged one.
        var identical = new NavSymRef.ModuleDefinition
        {
            Name = "hand-built",
            Codeunits = [Codeunit(Method("Pick", 11)), Codeunit(Method("Pick", 11))],
        };
        var alsoAmbiguous = ModuleDefinitionOps.CompareObjectSurface(
            identical, CodeunitModule(Method("Pick", 11)), LibKey);
        Assert.Contains("more than one serialized copy", alsoAmbiguous.FailedClosedBecause);
        Assert.True(alsoAmbiguous.Moved);

        // …and the id-less kinds, which take the all-or-nothing path and would otherwise skip
        // the uniqueness check entirely.
        var duplicatedContract = new NavSymRef.ModuleDefinition
        {
            Name = "hand-built",
            Interfaces =
            [
                new NavSymRef.InterfaceDefinition { Name = "Contract", Methods = [Method("Pick", 11)] },
                new NavSymRef.InterfaceDefinition
                {
                    Name = "Contract",
                    Methods = [Method("Pick", 11), Method("Fresh", 12)],
                },
            ],
        };
        var contract = ModuleDefinitionOps.CompareObjectSurface(
            InterfaceModule(Method("Pick", 11)),
            duplicatedContract,
            RadObjectKey.For("Interface", 0, "Contract"));
        Assert.Contains("more than one serialized copy", contract.FailedClosedBecause);
        Assert.True(contract.Moved);
    }

    /// <summary>
    /// The other half of class 1: an object absent from a side is reported AND read as moved —
    /// a caller of something the merged baseline no longer describes must be rebound so the
    /// dangling reference becomes an AL diagnostic, rather than left dispatching a type that is
    /// still loaded.
    /// </summary>
    [Fact]
    public void AnObjectMissingFromOneSide_FailsClosedAndReadsAsMoved()
    {
        var comparison = ModuleDefinitionOps.CompareObjectSurface(
            CodeunitModule(Method("Pick", 11)),
            new NavSymRef.ModuleDefinition { Name = "hand-built", Codeunits = [] },
            LibKey);
        Assert.Contains("holds no readable serialized copy", comparison.FailedClosedBecause);
        Assert.Contains("treating the surface as moved", comparison.FailedClosedBecause);
        Assert.True(comparison.Moved);
    }

    private static void AssertCleanDelta(RadEmitResult delta)
    {
        Assert.True(delta.Emit.Diagnostics.Count == 0,
            string.Join(Environment.NewLine, delta.Emit.Diagnostics));
        Assert.False(delta.FullRebuild, "the edit rebuilt the whole module");
        Assert.False(delta.NoChange);
    }

    private static NavSymRef.ModuleDefinition CodeunitModule(
        params NavSymRef.MethodDefinition[] methods) =>
        new() { Name = "hand-built", Codeunits = [Codeunit(methods)] };

    private static NavSymRef.CodeunitDefinition Codeunit(
        params NavSymRef.MethodDefinition[] methods) =>
        new() { Id = LibKey.Id, Name = Lib, Methods = methods };

    private static NavSymRef.ModuleDefinition InterfaceModule(
        params NavSymRef.MethodDefinition[] methods) =>
        new()
        {
            Name = "hand-built",
            Interfaces = [new NavSymRef.InterfaceDefinition { Name = "Contract", Methods = methods }],
        };

    private static NavSymRef.MethodDefinition Method(string name, int? id) =>
        new() { Name = name, Id = id };
}
