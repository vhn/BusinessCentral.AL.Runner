// RadProducerEquivalenceTests — do the two producers describe one unchanged surface the
// same way, MEMBER BY MEMBER?
//
// A delta decides whether an object's callers must rebind by comparing that object's
// serialized surface across two DIFFERENT producers:
//
//   A. the committed full-compile baseline, built by
//      `SerializableSymbolModelConverter.ConvertModuleToSerializableSymbolModel(Compilation)`
//      and kept as an object graph (BcCompiler.TryBuildBaselineSnapshot →
//      SymbolJsonWriter.BuildModuleDefinition);
//   B. the merged delta baseline, written by `CompilationUtilities.WriteSymbolReference`
//      and read back with `SymbolReferenceJsonReader` (BcCompiler.MergeRadBaseline) — so it
//      has been through a JSON round trip that A never sees.
//
// The whole-object compare in `ModuleDefinitionOps.ObjectSurfaceFingerprint` has already
// needed canonicalisation twice for exactly this reason (provenance, and null-versus-empty).
// A per-MEMBER compare adds surface the whole-object one never exercised — member ordering
// within `Methods`, nested parameter/attribute ordering, and members present on one side
// only — so before a member-level diff can be built, the two sides have to be proven to
// describe the same thing. That proof is this suite.
//
// WHAT IT MEASURES (all three numbers below are measured, not assumed):
//
//   * Producer A and producer B list the SAME thirteen members, in the SAME order, with the
//     same ids — for every shape a member-level diff has to align: no parameters, a `var`
//     parameter, a Record subtype, a Codeunit subtype, a `List of [Integer]` return, a
//     `Dictionary of [Text, Integer]` return, [TryFunction], [NonDebuggable],
//     [IntegrationEvent], [EventSubscriber], `internal`, and two overloads sharing one name.
//
//   * RAW, they disagree on exactly TWO of those thirteen — the argument-less attributes,
//     whose `Arguments` is `null` from the converter and `[]` after the round trip. A member
//     diff built on the raw serialization therefore reports two spurious changed members on
//     the first delta of every app that uses [TryFunction] or [NonDebuggable].
//
//   * Canonicalised through `ObjectSurfaceFingerprint`'s own rules, they agree on all thirteen.
//
// The FIRST delta after a full baseline is where the producer transition happens and is the
// case pinned here; `TheSecondDelta_...` shows why a same-producer comparison proves nothing.

using System.Text.Json;
using System.Text.Json.Nodes;
using AlRunner.Rad;
using Xunit;
using NavSymRef = Microsoft.Dynamics.Nav.CodeAnalysis.SymbolReference;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadProducerEquivalenceTests(BcEngineFixture engine)
{
    private const string FixtureName = "RadProducerSurface";
    private const string ModuleName = "RAD Producer Surface";
    // Its own AppId and its own object-id range, both unused by any other fixture: two
    // fixtures sharing an AppId would share a `RadWorkspaceStore` entry the moment either is
    // driven through the store, and overlapping id ranges make a failure in one suite read as
    // if it came from the other.
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000016");

    /// <summary>The table and the three codeunits; the fixture declares nothing else.</summary>
    private const int EmittedObjectCount = 4;

    private static readonly RadObjectKey ProbeKey = new("Codeunit", 72202);
    private const string ProbeName = "Producer Probe";
    private const string HelperName = "Producer Helper";
    private const string ProbeFile = "ProducerProbe.Codeunit.al";
    private const string HelperFile = "ProducerHelper.Codeunit.al";

    /// <summary>
    /// Every member the probe puts on its serialized surface, in declaration order, rendered
    /// from the serialization itself — so the list pins the parameter modes, the subtypes, the
    /// generic type arguments, the attributes and the access modifier, not just the names.
    ///
    /// <para>Two of the codeunit's fifteen declared methods are deliberately absent, and are
    /// asserted absent in <see cref="AssertSameMembers"/>: <c>LocalOnly</c> and
    /// <c>HandleProbedLocally</c>. A `local` method is not part of the module's exported symbol
    /// surface at all, on EITHER producer — not even when it carries an attribute — so it
    /// cannot be the source of a member-set difference, and adding, removing or retyping one is
    /// invisible to any surface diff built on this data.</para>
    /// </summary>
    private static readonly string[] ExpectedMembers =
    [
        "NoParams(): Integer",
        "VarParam(var Integer)",
        "RecordSubtype(var Record \"Producer Target\"): Integer",
        "CodeunitSubtype(Codeunit \"Producer Helper\"): Integer",
        "GenericList(): List of [Integer]",
        "GenericDictionary(): Dictionary of [Text, Integer]",
        "[TryFunction] TryIt(Integer): Boolean",
        "[NonDebuggable] Hidden(): Integer",
        "[IntegrationEvent] OnProbed(Integer)",
        "[EventSubscriber] HandleProbed(Integer)",
        "internal InternalOnly(): Integer",
        "Pick(Decimal): Integer",
        "Pick(Integer): Integer",
    ];

    /// <summary>Declared `local`, and therefore on neither producer's surface.</summary>
    private static readonly string[] ExpectedAbsentMembers = ["LocalOnly", "HandleProbedLocally"];

    /// <summary>
    /// The measured disagreement between the two producers, at member level, on the first
    /// delta after a full baseline — the ONLY place either producer is asked about a member it
    /// did not itself produce.
    ///
    /// <para>Both entries are the same cause: an attribute that takes no arguments serializes
    /// as <c>"Arguments":null</c> out of the converter and as <c>"Arguments":[]</c> after
    /// `WriteSymbolReference` + `SymbolReferenceJsonReader`. Nothing else about any of the
    /// thirteen members moves — not the ids, not the order, not a nested parameter or type
    /// argument, and not the attributes that DO carry arguments: <c>[IntegrationEvent]</c>'s
    /// two booleans and <c>[EventSubscriber]</c>'s six, one of which is an empty string,
    /// round-trip identically.</para>
    ///
    /// <para>This is the list Task 20 has to canonicalise away. Left raw, a member-level diff
    /// reports these two members as changed on the first warm cycle of any app that uses
    /// <c>[TryFunction]</c> or <c>[NonDebuggable]</c> — and rebinds every caller of the
    /// codeunit that carries them, whose own fingerprints then move for the same reason.</para>
    /// </summary>
    private static readonly string[] ExpectedRawDivergences =
    [
        "[TryFunction] TryIt(Integer): Boolean · Attributes[0].Arguments: A=null B=[]",
        "[NonDebuggable] Hidden(): Integer · Attributes[0].Arguments: A=null B=[]",
    ];

    /// <summary>
    /// The same two divergences, located in the WHOLE serialized object rather than inside its
    /// member list — which is what makes "only the members diverge" a measurement instead of an
    /// assumption. Nothing outside <c>Methods</c> moves: not <c>Properties</c>, not
    /// <c>ImplementedInterfaces</c>, not <c>Variables</c> (the array Task 20 means to exclude),
    /// not the object's own name or id, and not <c>ReferenceSourceFileName</c>, which is null on
    /// both sides when neither compile is given an app root.
    /// </summary>
    private static readonly string[] ExpectedRawObjectDivergences =
    [
        "Methods[6].Attributes[0].Arguments: A=null B=[]",
        "Methods[7].Attributes[0].Arguments: A=null B=[]",
    ];

    /// <summary>
    /// The transition that actually happens on a warm cycle: the object was RE-EMITTED by the
    /// delta, so producer B describes it from the RAD compilation's own module symbol while
    /// the committed baseline still describes it from the full compile's converter output.
    /// </summary>
    [SkippableFact]
    public void TheFirstDeltaAfterAFullBaseline_DescribesEveryReEmittedMemberAsTheFullCompileDid()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(FixtureName, ModuleName, AppId, EmittedObjectCount, (compiler, ws, tempRoot) =>
        {
            var full = Surface.Of(ws, ProbeKey);

            RadByName.Replace(RadByName.SourceFile(tempRoot, ProbeFile), "exit(1);", "exit(11);");
            var merged = CommitOneDelta(compiler, ws, tempRoot, [ProbeName]);

            AssertSameMembers(full, merged);
        });
    }

    /// <summary>
    /// The other half of the first delta, and a different code path inside the same writer: the
    /// probe was NOT touched, so `WriteSymbolReference` copies it forward out of the previous
    /// `ModuleDefinition` object graph rather than reading it off a compiler symbol. The
    /// round trip is the same one either way — which is the point. An untouched member is
    /// re-serialized, so it is exposed to exactly the same divergence as a re-emitted one, and
    /// a member diff that only handled re-emitted objects would still be wrong.
    /// </summary>
    [SkippableFact]
    public void TheFirstDeltaAfterAFullBaseline_DescribesCarriedForwardMembersTheSameWay()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(FixtureName, ModuleName, AppId, EmittedObjectCount, (compiler, ws, tempRoot) =>
        {
            var full = Surface.Of(ws, ProbeKey);

            // A body-only edit to a DIFFERENT codeunit. The probe calls it, so this also pins
            // that a body edit does not move the helper's surface: if it did, the probe would
            // be pulled into the same delta and this would stop being the carried-forward case.
            RadByName.Replace(RadByName.SourceFile(tempRoot, HelperFile), "exit(7);", "exit(17);");
            var merged = CommitOneDelta(compiler, ws, tempRoot, [HelperName]);

            AssertSameMembers(full, merged);
        });
    }

    /// <summary>
    /// Why a same-producer comparison proves nothing, stated as a measurement rather than as a
    /// warning: from the second delta on, both sides have been through the round trip, so the
    /// two argument-less attributes agree byte-for-byte and a raw member diff looks perfectly
    /// clean. A suite that compared two warm snapshots would be green with the divergence fully
    /// intact.
    /// </summary>
    [SkippableFact]
    public void TheSecondDelta_AgreesRawWhereTheFirstDidNot()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(FixtureName, ModuleName, AppId, EmittedObjectCount, (compiler, ws, tempRoot) =>
        {
            RadByName.Replace(RadByName.SourceFile(tempRoot, ProbeFile), "exit(1);", "exit(11);");
            var first = CommitOneDelta(compiler, ws, tempRoot, [ProbeName]);

            RadByName.Replace(RadByName.SourceFile(tempRoot, ProbeFile), "exit(11);", "exit(12);");
            var second = CommitOneDelta(compiler, ws, tempRoot, [ProbeName]);

            Assert.Equal(ExpectedMembers, Describe(first.Raw));
            Assert.Equal(ExpectedMembers, Describe(second.Raw));
            // Raw, with no canonicalisation at all — the comparison that reported two changed
            // members one delta ago now reports none, over the whole object as well as over
            // its member list.
            Assert.Empty(MemberDifferences(first.Raw, second.Raw));
            Assert.Empty(Differences(string.Empty, first.Raw, second.Raw));
        });
    }

    /// <summary>
    /// Run one warm cycle, assert it is a clean delta that emitted exactly
    /// <paramref name="expectedEmitted"/>, commit it, and hand back the merged baseline's view
    /// of the probe.
    ///
    /// <para>The emitted set is asserted because it is the consequence under test: the delta's
    /// existing WHOLE-object compare runs on this same pair of producers, so a spurious
    /// difference would show up here first as the caller being pulled into the cycle.</para>
    /// </summary>
    private static Surface CommitOneDelta(
        BcCompiler compiler, RadWorkspace ws, string tempRoot, string[] expectedEmitted)
    {
        var delta = compiler.EmitIncremental([tempRoot], ModuleName, ws);
        Assert.True(delta.Emit.Diagnostics.Count == 0,
            string.Join(Environment.NewLine, delta.Emit.Diagnostics));
        Assert.False(delta.FullRebuild, "the cycle rebuilt the whole module instead of deltaing it");
        Assert.False(delta.NoChange);
        Assert.Equal(
            expectedEmitted.Order(StringComparer.Ordinal).ToArray(),
            delta.Emit.Sources.Select(source => source.Name).Order(StringComparer.Ordinal).ToArray());

        delta.Commit(ws, RadFixture.AssembleAndLoad(ws, delta.Emit.Sources));
        return Surface.Of(ws, ProbeKey);
    }

    /// <summary>
    /// The whole claim: both producers list the same members in the same order, agree on every
    /// member id, put no `local` method on the surface, disagree raw on exactly the measured
    /// divergences, and agree completely once canonicalised.
    /// </summary>
    private static void AssertSameMembers(Surface a, Surface b)
    {
        Assert.Equal(ExpectedMembers, Describe(a.Raw));
        Assert.Equal(ExpectedMembers, Describe(b.Raw));
        // The canonicalised form is what a member diff would actually compare, so it has to
        // still hold every member: `Assert.Empty` on a difference list below would be vacuous
        // if canonicalisation had dropped the member array itself.
        Assert.Equal(ExpectedMembers, Describe(a.Canonical));
        Assert.Equal(ExpectedMembers, Describe(b.Canonical));

        // Ids, not just shapes: a member-level diff keys on (Name, Id), so the two producers
        // agreeing on the shapes while disagreeing on an id would be the worst possible
        // outcome — every member would look present and one would silently be a different
        // member. Asserted as a sequence, so it also pins that no two members share a key.
        var idsA = MemberKeys(a.Raw);
        var idsB = MemberKeys(b.Raw);
        Assert.Equal(idsA, idsB);
        Assert.Equal(ExpectedMembers.Length, idsA.Distinct(StringComparer.Ordinal).Count());

        // Provenance is recorded on the OBJECT, never on a member — so a member fingerprint
        // does not need the `ReferenceSourceFileName` strip that the object one does.
        foreach (var side in new[] { a.Raw, b.Raw })
            foreach (var member in Members(side))
                Assert.Null(member["ReferenceSourceFileName"]);

        // Stated rather than merely implied by the list above: `local` is a hole a member diff
        // cannot see through in either direction, on either producer.
        foreach (var side in new[] { a.Raw, b.Raw })
        {
            var names = MemberNames(side);
            foreach (var absent in ExpectedAbsentMembers)
                Assert.DoesNotContain(absent, names);
        }

        Assert.Equal(ExpectedRawDivergences, MemberDifferences(a.Raw, b.Raw));
        Assert.Empty(MemberDifferences(a.Canonical, b.Canonical));

        // The WHOLE object, not just its members. Two reasons this is not redundant: it proves
        // the divergence is confined to those two attribute nodes and does not also sit in
        // `Properties` / `ImplementedInterfaces` / `Variables`, and the canonical form is
        // compared as the exact string `changedSurfaces` compares — so this is the production
        // contract itself, asserted for an object the delta did not necessarily re-emit.
        Assert.Equal(ExpectedRawObjectDivergences, Differences(string.Empty, a.Raw, b.Raw).ToArray());
        Assert.Equal(a.Canonical.ToJsonString(), b.Canonical.ToJsonString());
    }

    private static JsonArray Members(JsonObject element) =>
        element["Methods"] as JsonArray ?? new JsonArray();

    /// <summary>Each member as <c>Name#Id</c> — the key a member-level diff has to align on.</summary>
    private static string[] MemberKeys(JsonObject element) => Members(element)
        .Select(member =>
            $"{member?["Name"]?.GetValue<string>()}#{member?["Id"]?.GetValue<int>()}")
        .ToArray();

    private static string[] MemberNames(JsonObject element) => Members(element)
        .Select(member => member?["Name"]?.GetValue<string>() ?? string.Empty)
        .ToArray();

    /// <summary>
    /// Each member as an AL-shaped signature, in declaration order. Rendered from the
    /// serialization rather than from the source so that a producer dropping a parameter mode,
    /// a subtype, a type argument, an attribute or the access modifier changes this string.
    /// </summary>
    private static string[] Describe(JsonObject element) =>
        Members(element).Select(member => DescribeMember(member!)).ToArray();

    private static string DescribeMember(JsonNode member)
    {
        var attributes = (member["Attributes"] as JsonArray ?? [])
            .Select(attribute => $"[{attribute?["NameForSerialization"]?.GetValue<string>()}] ");
        var access = member["IsInternal"]?.GetValue<bool>() == true ? "internal "
            : member["IsLocal"]?.GetValue<bool>() == true ? "local "
            : member["IsProtected"]?.GetValue<bool>() == true ? "protected "
            : string.Empty;
        var parameters = (member["Parameters"] as JsonArray ?? [])
            .Select(parameter =>
                (parameter?["IsVar"]?.GetValue<bool>() == true ? "var " : string.Empty)
                + DescribeType(parameter?["TypeDefinition"]));
        var returns = member["ReturnTypeDefinition"] is { } returnType
            ? $": {DescribeType(returnType)}"
            : string.Empty;
        return string.Concat(attributes) + access + member["Name"]?.GetValue<string>()
            + $"({string.Join(", ", parameters)})" + returns;
    }

    private static string DescribeType(JsonNode? type)
    {
        if (type == null) return "<none>";
        var name = type["Name"]?.GetValue<string>();
        if (type["TypeArguments"] is JsonArray arguments && arguments.Count > 0)
            return $"{name} of [{string.Join(", ", arguments.Select(DescribeType))}]";
        return type["Subtype"]?["Name"]?.GetValue<string>() is { } subtype
            ? $"{name} \"{subtype}\""
            : name ?? "<unnamed>";
    }

    /// <summary>
    /// Every leaf on which the two sides' member sets differ, as
    /// <c>&lt;member&gt; · &lt;path&gt;: A=&lt;value&gt; B=&lt;value&gt;</c>.
    ///
    /// <para>Members are aligned by position, which is only meaningful because
    /// <see cref="AssertSameMembers"/> asserts both sides describe the same members in the same
    /// order first. A count mismatch is reported as its own line rather than throwing, so a
    /// failure says WHICH member each side has.</para>
    /// </summary>
    private static string[] MemberDifferences(JsonObject a, JsonObject b)
    {
        var left = Members(a);
        var right = Members(b);
        if (left.Count != right.Count)
            return [$"member count: A={left.Count} B={right.Count}"];

        return Enumerable.Range(0, left.Count)
            .SelectMany(i => Differences(string.Empty, left[i], right[i])
                .Select(difference => $"{DescribeMember(left[i]!)} · {difference}"))
            .ToArray();
    }

    private static IEnumerable<string> Differences(string path, JsonNode? a, JsonNode? b)
    {
        if (a is JsonObject left && b is JsonObject right)
        {
            foreach (var name in left.Select(pair => pair.Key)
                .Concat(right.Select(pair => pair.Key))
                .Distinct(StringComparer.Ordinal))
                foreach (var difference in Differences(
                    path.Length == 0 ? name : $"{path}.{name}", left[name], right[name]))
                    yield return difference;
            yield break;
        }
        if (a is JsonArray leftItems && b is JsonArray rightItems
            && leftItems.Count == rightItems.Count)
        {
            for (int i = 0; i < leftItems.Count; i++)
                foreach (var difference in Differences($"{path}[{i}]", leftItems[i], rightItems[i]))
                    yield return difference;
            yield break;
        }
        var textA = a?.ToJsonString() ?? "null";
        var textB = b?.ToJsonString() ?? "null";
        if (!string.Equals(textA, textB, StringComparison.Ordinal))
            yield return $"{path}: A={textA} B={textB}";
    }

    /// <summary>
    /// One object's serialized surface, both as the raw graph serialization and in the
    /// canonicalised form <see cref="ModuleDefinitionOps.ObjectSurfaceFingerprint"/> compares —
    /// so a divergence can be attributed to the producers or to the canonicalisation.
    /// </summary>
    private sealed record Surface(JsonObject Raw, JsonObject Canonical)
    {
        internal static Surface Of(RadWorkspace ws, RadObjectKey key)
        {
            var module = (NavSymRef.ModuleDefinition)ws.Baseline!;
            // Fail closed on the duplicate the plan calls out: two elements under one key mean
            // whichever the array happens to list first answers, and everything below would be
            // comparing an arbitrary one of them.
            Assert.Equal(1, ModuleDefinitionOps.CountObjects(module, key));

            var element = FindElement(module, key);
            Assert.NotNull(element);
            var raw = JsonNode.Parse(JsonSerializer.Serialize(element, element!.GetType()))
                as JsonObject;
            Assert.NotNull(raw);

            var canonicalText = ModuleDefinitionOps.ObjectSurfaceFingerprint(module, key);
            Assert.NotNull(canonicalText);
            var canonical = JsonNode.Parse(canonicalText!) as JsonObject;
            Assert.NotNull(canonical);

            return new Surface(raw!, canonical!);
        }

        private static object? FindElement(object container, RadObjectKey key)
        {
            var type = container.GetType();
            if (type.GetProperty("Codeunits")?.GetValue(container) is Array items)
                foreach (var item in items)
                {
                    if (item == null) continue;
                    if (item.GetType().GetProperty("Id")?.GetValue(item) is int id && id == key.Id)
                        return item;
                }
            if (type.GetProperty("Namespaces")?.GetValue(container) is Array namespaces)
                foreach (var child in namespaces)
                {
                    if (child == null) continue;
                    if (FindElement(child, key) is { } hit) return hit;
                }
            return null;
        }
    }
}
