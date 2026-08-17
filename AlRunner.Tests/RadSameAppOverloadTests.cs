// RadSameAppOverloadTests — the SAME-APP half of the silent overload hazard, at the level
// the delta decides it: which objects a cycle re-emits.
//
// The runtime half — that the rebound caller really dispatches the new overload — is
// RadSameAppOverloadWatchTests, which reads the answer out of a running AL test.
// RadDeltaWatchTests.Watch_AddingAnOverloadInOneApp_RebindsItsCrossAppCaller covers the
// cross-app one. This suite is the path a member-level surface diff rewrites: caller and
// callee in one module, decided by `changedSurfaces` in BcCompiler.Rad.cs.
//
// Why it needs its own suite at all
// ---------------------------------
// Every other RAD test measures an edit whose damage announces itself: a retired member id
// makes dispatch throw, a stripped symbol makes the compile fail. Adding an overload does
// neither. `CalculateMethodId` is method-local, so `Which(Decimal)` keeps its id AND its
// `case` label in the re-emitted callee; what moves is the id the CALLER bakes, because an
// Integer argument now binds to the new `Which(Integer)` instead of widening. A caller left
// un-rebound therefore dispatches a member that still exists and gets a perfectly ordinary
// answer — the previous overload's. No exception, no diagnostic, no log line.
//
// So a rule of the form "an added member is safe, skip the rebind" is wrong in exactly one
// shape and wrong silently. These tests exist to make that shape falsifiable.

using AlRunner.Rad;
using Xunit;
using Cecil = Mono.Cecil;
using NavSymRef = Microsoft.Dynamics.Nav.CodeAnalysis.SymbolReference;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadSameAppOverloadTests(BcEngineFixture engine)
{
    private const string Fixture = "RadSameAppOverload";
    private const string ModuleName = "RAD Same App Overload";
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000016");

    /// <summary>Lib, caller and the test codeunit; the fixture declares nothing else.</summary>
    private const int EmittedObjectCount = 3;

    private const string LibFile = "OverloadLib.Codeunit.al";

    private const string MethodSymbolType = "Microsoft.Dynamics.Nav.CodeAnalysis.Symbols.MethodSymbol";

    /// <summary>
    /// The hazard, measured at the delta: a same-app caller of the overloaded method must be
    /// re-emitted, because the id it bakes has moved even though the callee's own members
    /// have not.
    ///
    /// <para><b>Expected GREEN today.</b> The current gate is a whole-object surface
    /// fingerprint (<c>ModuleDefinitionOps.ObjectSurfaceFingerprint</c>), and a serialized
    /// codeunit that gained a method compares unequal for that reason alone — the same reason
    /// a body-only edit compares equal and a body-only edit therefore does not rebind. So this
    /// is a trip-wire, not a bug report: it fails the moment a member-level rule replaces that
    /// fingerprint and treats "a member was added" as safe without asking whether the name was
    /// already on the object.</para>
    ///
    /// <para>Asserted as an exact set, so the opposite failure — a rule that rebinds
    /// everything and calls the hazard covered — is caught by
    /// <see cref="AddingAProcedureUnderANameNewToTheObject_DoesNotRebindTheCaller"/>, which is
    /// this test's control and fails against a whole-module rebuild.</para>
    /// </summary>
    [SkippableFact]
    public void AddingAnOverload_RebindsTheSameAppCaller()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(Fixture, ModuleName, AppId, EmittedObjectCount, (compiler, workspace, tempRoot) =>
        {
            AddIntegerOverload(tempRoot);

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);

            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild, "adding an overload rebuilt the whole module");
            Assert.False(delta.NoChange);
            // The caller comes along; the test codeunit — which calls the caller, not the
            // library — does not. One hop, not a cascade.
            Assert.Equal(["RAD Ovl Caller", "RAD Ovl Lib"], RadFixture.EmittedNames(delta));
        });
    }

    /// <summary>
    /// Control — the win a member-level surface diff is worth having, and the one assertion
    /// that stops "rebind everything" from passing for a fix.
    ///
    /// <para>A procedure added under a name NEW to the object cannot change what any existing
    /// call site binds to: overload resolution never considered it, and no existing member's
    /// id moved. The caller's baked ids are all still correct, so re-emitting it is pure cost.
    /// On NP Retail's `NPR POS Session` that cost is 313 objects for one added procedure.</para>
    ///
    /// <para><b>Expected RED today, by construction.</b> The whole-object fingerprint cannot
    /// tell this addition from the overload above — both change the serialized codeunit — so
    /// today both rebind. That is the same measurement
    /// RadObjectDeltaTests.AddingAProcedure_RebindsDirectCallersOnly records from the other
    /// side, and the two are deliberately in conflict: whichever change makes this test green
    /// must flip that one, and this comment is the record of why.</para>
    /// </summary>
    [SkippableFact]
    public void AddingAProcedureUnderANameNewToTheObject_DoesNotRebindTheCaller()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(Fixture, ModuleName, AppId, EmittedObjectCount, (compiler, workspace, tempRoot) =>
        {
            RadByName.Replace(
                RadByName.SourceFile(tempRoot, LibFile),
                "    procedure Sibling(Value: Integer): Integer",
                """
                    procedure Fresh(): Text
                    begin
                        exit('FRESH');
                    end;

                    procedure Sibling(Value: Integer): Integer
                """);

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);

            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild, "adding a procedure rebuilt the whole module");
            Assert.False(delta.NoChange);

            var emitted = RadFixture.EmittedNames(delta);
            Assert.True(
                emitted.SequenceEqual(["RAD Ovl Lib"], StringComparer.Ordinal),
                "a procedure added under a name NEW to the object still rebinds its caller — "
                + $"re-emitted [{string.Join(", ", emitted)}]. Expected to fail until the "
                + "whole-object surface fingerprint behind `changedSurfaces` (BcCompiler.Rad.cs) "
                + "is replaced by a member-level diff: this is that change's acceptance test, and "
                + "RadObjectDeltaTests.AddingAProcedure_RebindsDirectCallersOnly records the same "
                + "measurement from the other side.");
        });
    }

    /// <summary>
    /// The compiler contract the rule above rests on, measured rather than assumed: adding an
    /// overload moves NO other member's id — not the overload it joins, not an unrelated
    /// method on the same object, not a method on another object.
    ///
    /// <para>This is what makes "an addition under a new name is safe" true in the first
    /// place. If adding a member could re-number its neighbours, every addition would have to
    /// rebind every caller and there would be no win to take.</para>
    ///
    /// <para>Both compiles are full compiles of the same producer, so the only variable is the
    /// source. The negative direction lives in
    /// <see cref="RetypingOneParameter_MovesThatMethodsIdAndNoOther"/>: without it, an
    /// implementation that reported id 0 for everything would satisfy every equality
    /// here.</para>
    /// </summary>
    [SkippableFact]
    public void AddingAnOverload_MovesNoOtherMemberId()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(Fixture, ModuleName, AppId, EmittedObjectCount, (compiler, workspace, tempRoot) =>
        {
            var before = MemberIds(workspace.Baseline!);
            Assert.Contains("RAD Ovl Lib.Which(Decimal)", before.Keys);
            Assert.Contains("RAD Ovl Lib.Sibling(Integer)", before.Keys);
            Assert.Contains("RAD Ovl Caller.Call()", before.Keys);
            // Ids are hashes, never ordinals: nothing here may be a default.
            Assert.All(before, entry => Assert.NotEqual(0, entry.Value));

            AddIntegerOverload(tempRoot);
            var after = MemberIds(ColdBaseline(tempRoot));

            foreach (var (signature, id) in before)
            {
                Assert.True(after.ContainsKey(signature), $"{signature} vanished from the surface");
                Assert.True(id == after[signature],
                    $"{signature} moved from {id} to {after[signature]} when an overload was added");
            }

            // …and the addition really happened, under an id of its own. Without this the
            // loop above would pass against an edit that did nothing.
            Assert.True(after.ContainsKey("RAD Ovl Lib.Which(Integer)"),
                "the Integer overload is absent from the recompiled surface");
            Assert.NotEqual(after["RAD Ovl Lib.Which(Decimal)"], after["RAD Ovl Lib.Which(Integer)"]);
        });
    }

    /// <summary>
    /// The other direction of the contract, and the reason the equalities above mean
    /// something: a member id DOES move when its own signature moves, and only that member's.
    ///
    /// <para><c>Sibling</c> is retyped because nothing calls it — retyping <c>Which</c> would
    /// change what the caller binds to and measure two things at once.</para>
    /// </summary>
    [SkippableFact]
    public void RetypingOneParameter_MovesThatMethodsIdAndNoOther()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(Fixture, ModuleName, AppId, EmittedObjectCount, (compiler, workspace, tempRoot) =>
        {
            var before = MemberIds(workspace.Baseline!);

            RadByName.Replace(
                RadByName.SourceFile(tempRoot, LibFile),
                "procedure Sibling(Value: Integer): Integer",
                "procedure Sibling(Value: Decimal): Integer");
            var after = MemberIds(ColdBaseline(tempRoot));

            Assert.NotEqual(
                before["RAD Ovl Lib.Sibling(Integer)"], after["RAD Ovl Lib.Sibling(Decimal)"]);
            foreach (var (signature, id) in before)
            {
                if (signature == "RAD Ovl Lib.Sibling(Integer)") continue;
                Assert.True(after.ContainsKey(signature), $"{signature} vanished from the surface");
                Assert.True(id == after[signature],
                    $"{signature} moved from {id} to {after[signature]} when a DIFFERENT " +
                    "method's parameter was retyped");
            }
        });
    }

    /// <summary>
    /// The compiler-contract pin, read off the BC assembly this runner is linked against.
    ///
    /// <para>The one way "an addition under a new name is safe" could be unsound is if adding
    /// an overload flipped <c>RequiresRuntimeOverloadDisambiguation()</c> for OTHER methods on
    /// the object: that flag decides whether each parameter's SUBTYPE is hashed into the id
    /// (<c>MethodSymbol.CalculateMethodIdForNewVersions</c>), so a flag that consulted the
    /// object's other members would let a pure addition re-number a method it never touched —
    /// silently, since the callee keeps compiling.</para>
    ///
    /// <para>It does not: the flag consults only <c>CanBeOverloaded()</c> and its own
    /// <c>Parameters</c>, and <c>CanBeOverloaded()</c> in turn only its own
    /// <c>MethodKind</c>/<c>IsEvent</c>/<c>IsEventSubscriber</c>/<c>IsHandler</c>. "Method-wide"
    /// means all parameters of that method, never all methods of the object.</para>
    ///
    /// <para>Pinned structurally rather than by behaviour because behaviour cannot see the
    /// difference between "does not consult siblings" and "consults siblings and this fixture
    /// happens not to trip it" — the subtype term only exists for Record/Codeunit/Page/…
    /// parameters. The walk covers the base declaration AND every override, and fails loudly
    /// if the member is gone: a BC upgrade that renames it must not turn this pin into a
    /// vacuous pass.</para>
    /// </summary>
    [SkippableFact]
    public void RequiresRuntimeOverloadDisambiguation_ReadsOnlyTheMethodItIsAskedAbout()
    {
        // The assembly the runner compiles AL against, not a copy chosen by version guess.
        var codeAnalysis = typeof(NavSymRef.ModuleDefinition).Assembly.Location;
        TestArtifacts.SkipIf(
            string.IsNullOrEmpty(codeAnalysis) || !File.Exists(codeAnalysis),
            "Microsoft.Dynamics.Nav.CodeAnalysis.dll has no readable file location in this host.");

        using var module = Cecil.ModuleDefinition.ReadModule(codeAnalysis);
        var declared = AllTypes(module)
            .SelectMany(type => type.Methods)
            .Where(method => method.Name == "RequiresRuntimeOverloadDisambiguation")
            .ToList();

        Assert.True(declared.Count > 0,
            "Microsoft.Dynamics.Nav.CodeAnalysis no longer declares "
            + "RequiresRuntimeOverloadDisambiguation. The member-id contract was derived from it; "
            + "re-derive it before trusting any delta rule that assumes an added member is safe.");
        var root = Assert.Single(
            declared, method => method.DeclaringType.FullName == MethodSymbolType);
        // Nothing hands it the sibling set: no parameters, and it is an instance member of the
        // method symbol itself.
        Assert.All(declared, method =>
        {
            Assert.Empty(method.Parameters);
            Assert.False(method.IsStatic);
        });

        // The pin is only load-bearing while the id algorithm still asks the flag.
        var methodSymbol = Assert.Single(AllTypes(module), type => type.FullName == MethodSymbolType);
        var algorithm = methodSymbol.Methods
            .SingleOrDefault(method => method.Name == "CalculateMethodIdForNewVersions");
        Assert.True(algorithm != null,
            $"{MethodSymbolType} no longer declares CalculateMethodIdForNewVersions, so this test "
            + "no longer pins the algorithm that bakes member ids. Re-derive the contract.");
        Assert.Contains(Calls(algorithm!), call => call.Name == root.Name);

        // Every method reachable from it, inside this assembly. Measured at BC 28.1: 24.
        var closure = CallClosure(module, declared);
        Assert.Contains(closure, method => method.Name == "CanBeOverloaded");
        Assert.Contains(closure, method => method.Name == "get_Parameters");

        // Nothing in that closure can reach the containing object's other members. These are
        // the ways a symbol asks about its siblings; a hit means the flag is no longer
        // method-local and the "addition is safe" rule has to be re-derived.
        string[] siblingReaching =
            ["GetMembers", "get_Members", "get_ContainingType", "get_ContainingSymbol", "LookupSymbols"];
        var offenders = closure
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions
                .Select(instruction => instruction.Operand as Cecil.MemberReference)
                .OfType<Cecil.MemberReference>()
                .Where(reference => siblingReaching.Any(name =>
                    reference.Name.Contains(name, StringComparison.Ordinal)))
                .Select(reference => $"{method.DeclaringType.Name}.{method.Name} -> {reference.FullName}"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.True(offenders.Length == 0,
            "RequiresRuntimeOverloadDisambiguation now reaches the containing object's members, "
            + "so adding an overload may re-number OTHER methods and no delta rule may treat an "
            + "added member as safe:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The edit under test everywhere in this suite: a second overload, same name, Integer
    /// where the existing one takes Decimal. Placed AFTER the existing overload so nothing
    /// about the change can be attributed to declaration order.
    /// </summary>
    private static void AddIntegerOverload(string tempRoot) => RadByName.Replace(
        RadByName.SourceFile(tempRoot, LibFile),
        "    procedure Sibling(Value: Integer): Integer",
        """
            procedure Which(Seed: Integer): Text
            begin
                exit('INTEGER');
            end;

            procedure Sibling(Value: Integer): Integer
        """);

    /// <summary>
    /// A full compile of <paramref name="tempRoot"/> as it now stands, taken from the same
    /// producer as the seeded baseline (<c>ConvertModuleToSerializableSymbolModel</c>) so the
    /// two id sets are comparable.
    /// </summary>
    private static object ColdBaseline(string tempRoot)
    {
        var cold = RadByName.ColdCompile(tempRoot, ModuleName);
        Assert.True(cold.Emit.Diagnostics.Count == 0,
            string.Join(Environment.NewLine, cold.Emit.Diagnostics));
        Assert.True(cold.FullRebuild);
        Assert.NotNull(cold.WorkspaceUpdate);
        return cold.WorkspaceUpdate!.Baseline;
    }

    /// <summary>
    /// `&lt;Codeunit&gt;.&lt;Method&gt;(&lt;parameter types&gt;)` → the id BC baked, which is
    /// the same integer the callee's `case` label and the caller's `Target.Invoke` carry.
    ///
    /// <para>Walks <c>Namespaces</c> as well as the container's own arrays: the fixture
    /// declares a namespace, and a namespaced object is nested under it rather than sitting
    /// at the module root.</para>
    /// </summary>
    private static IReadOnlyDictionary<string, int> MemberIds(object baseline)
    {
        var container = Assert.IsAssignableFrom<NavSymRef.IObjectContainerDefinition>(baseline);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        Collect(container);
        Assert.NotEmpty(ids);
        return ids;

        void Collect(NavSymRef.IObjectContainerDefinition scope)
        {
            foreach (var codeunit in scope.Codeunits ?? [])
                foreach (var method in codeunit.Methods ?? [])
                {
                    var parameters = string.Join(", ", (method.Parameters ?? [])
                        .Select(parameter =>
                            (parameter.IsVar ? "var " : string.Empty)
                            + (parameter.TypeDefinition?.Name ?? "?")));
                    ids[$"{codeunit.Name}.{method.Name}({parameters})"] = method.Id ?? 0;
                }
            foreach (var child in scope.Namespaces ?? []) Collect(child);
        }
    }

    private static IEnumerable<Cecil.TypeDefinition> AllTypes(Cecil.ModuleDefinition module) =>
        module.Types.SelectMany(Flatten);

    private static IEnumerable<Cecil.TypeDefinition> Flatten(Cecil.TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes.SelectMany(Flatten)) yield return nested;
    }

    private static IEnumerable<Cecil.MethodReference> Calls(Cecil.MethodDefinition method) =>
        !method.HasBody
            ? []
            : method.Body.Instructions
                .Select(instruction => instruction.Operand as Cecil.MethodReference)
                .OfType<Cecil.MethodReference>();

    /// <summary>
    /// Every method transitively called from <paramref name="roots"/> that is defined in the
    /// same assembly. Calls out of the assembly (BCL, immutable collections) are not walked:
    /// they cannot reach a BC symbol's member list.
    /// </summary>
    private static IReadOnlyList<Cecil.MethodDefinition> CallClosure(
        Cecil.ModuleDefinition module, IEnumerable<Cecil.MethodDefinition> roots)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<Cecil.MethodDefinition>();
        foreach (var root in roots)
            if (seen.Add(root.FullName)) queue.Enqueue(root);

        var closure = new List<Cecil.MethodDefinition>();
        while (queue.Count > 0)
        {
            var method = queue.Dequeue();
            closure.Add(method);
            foreach (var call in Calls(method))
            {
                Cecil.MethodDefinition? definition = null;
                try { definition = call.Resolve(); } catch { }
                if (definition == null || definition.Module != module) continue;
                if (seen.Add(definition.FullName)) queue.Enqueue(definition);
            }
        }
        return closure;
    }
}
