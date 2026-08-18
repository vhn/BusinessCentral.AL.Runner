using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NavSymRef = Microsoft.Dynamics.Nav.CodeAnalysis.SymbolReference;

namespace AlRunner.Rad;

/// <summary>
/// Small pieces of symbol-reference surgery needed by Microsoft's RAD compilation.
/// </summary>
public static class ModuleDefinitionOps
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _idProperties = new();
    private static readonly IReadOnlyDictionary<string, string> _kindByArrayProperty =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tables"] = "Table",
            ["TableExtensions"] = "TableExtension",
            ["Codeunits"] = "Codeunit",
            ["Pages"] = "Page",
            ["PageExtensions"] = "PageExtension",
            ["Reports"] = "Report",
            ["ReportExtensions"] = "ReportExtension",
            ["XmlPorts"] = "XmlPort",
            ["Queries"] = "Query",
            ["EnumTypes"] = "Enum",
            ["EnumExtensionTypes"] = "EnumExtension",
            ["PermissionSets"] = "PermissionSet",
            ["PermissionSetExtensions"] = "PermissionSetExtension",
            // The id-less kinds. They are in the module definition like any other object —
            // which is what lets a changed codeunit still resolve an interface it implements
            // from the packaged baseline — so a MODIFIED one has to be stripped from it just
            // the same, or its pre-edit shape shadows the supplied syntax. Renaming an
            // interface method without this fails the delta with AL0582 against the old
            // member name; see RadIdlessObjectTests.
            ["Interfaces"] = "Interface",
            ["ControlAddIns"] = "ControlAddIn",
            ["Profiles"] = "Profile",
            ["PageCustomizations"] = "PageCustomization",
            ["ProfileExtensions"] = "ProfileExtension",
            // No "Entitlements". Not an omission: `ModuleDefinition` has no such property and
            // the compiler has no `EntitlementDefinition` type, so an entitlement has no
            // serialized copy to strip, to fingerprint, or to shadow an edit with. This map
            // previously claimed one, which cost nothing only because `GetProperty` returned
            // null and the loop skipped it silently.
        };

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _nameProperties = new();

    private static int? IdOf(object element)
    {
        var property = _idProperties.GetOrAdd(element.GetType(), static type =>
        {
            var candidate = type.GetProperty("Id");
            return candidate != null
                && (candidate.PropertyType == typeof(int) || candidate.PropertyType == typeof(int?))
                    ? candidate
                    : null;
        });
        return property?.GetValue(element) is int id ? id : null;
    }

    private static string? NameOf(object element)
    {
        var property = _nameProperties.GetOrAdd(element.GetType(), static type =>
        {
            var candidate = type.GetProperty("Name");
            return candidate?.PropertyType == typeof(string) ? candidate : null;
        });
        return property?.GetValue(element) as string;
    }

    /// <summary>
    /// The key a serialized module element answers to, applying the same "name only when
    /// there is no id" rule <see cref="RadObjectKey.For"/> applies to compiler symbols. The
    /// two must agree: one side builds the keys a delta strips, the other matches them.
    /// </summary>
    private static RadObjectKey KeyOf(object element, string kind) =>
        RadObjectKey.For(kind, IdOf(element) ?? 0, NameOf(element));

    /// <summary>
    /// Copy <paramref name="source"/> without the exact changed/removed objects.
    /// CreateForRad must bind their syntax trees in place of the old packaged symbols;
    /// leaving both present makes the stale definition shadow the edit. IDs are compared
    /// together with kind because AL's object kinds have independent ID spaces.
    /// </summary>
    public static NavSymRef.ModuleDefinition WithoutObjects(
        NavSymRef.ModuleDefinition source, IReadOnlyCollection<RadObjectKey> objects)
    {
        if (objects.Count == 0) return source;
        return (NavSymRef.ModuleDefinition)StripObjects(source, objects.ToHashSet());
    }

    /// <summary>
    /// Put <paramref name="objects"/> back into <paramref name="target"/>'s own object arrays,
    /// taking each one's definition from <paramref name="source"/>.
    ///
    /// <para>The inverse of <see cref="WithoutObjects"/>, and the repair for the namespace-free
    /// binder selecting the plain packaged copy of an untouched object after one of the targets
    /// named by that copy was stripped. Handing the packaged copy the target's <b>freshly
    /// compiled</b> definition is what makes this safe where handing it the committed definition
    /// is not — see <c>BcCompiler.DeltaCompile</c>'s <c>TryReplaceStrippedSurface</c> for the whole
    /// argument.</para>
    ///
    /// <para>Appended at the TOP LEVEL, never into a namespace node. The caller filters this set to
    /// definitions the current compile itself emitted at top level; that also handles an app
    /// part-way through namespace adoption without moving its namespaced objects. Guessing which
    /// namespace node owns a definition would be unsafe. <see cref="CountObjects"/> is the caller's
    /// check that the result holds exactly one copy of each.</para>
    /// </summary>
    public static NavSymRef.ModuleDefinition WithObjectsFrom(
        NavSymRef.ModuleDefinition target,
        NavSymRef.ModuleDefinition source,
        IReadOnlyCollection<RadObjectKey> objects)
    {
        if (objects.Count == 0) return target;
        var found = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        foreach (var key in objects)
        {
            if (ArrayPropertyFor(key.Kind) is not { } propertyName) continue;
            foreach (var element in FindElements(source, key))
            {
                if (!found.TryGetValue(propertyName, out var list))
                    found[propertyName] = list = new List<object>();
                list.Add(element);
            }
        }
        if (found.Count == 0) return target;

        var type = target.GetType();
        var copy = (NavSymRef.ModuleDefinition)ShallowCopy(target);
        foreach (var (propertyName, added) in found)
        {
            var property = type.GetProperty(propertyName)!;
            var kept = (property.GetValue(target) as Array)?.Cast<object>() ?? [];
            property.SetValue(
                copy,
                ToTypedArray(property.PropertyType.GetElementType()!, kept.Concat(added).ToList()));
        }
        return copy;
    }

    private static object ShallowCopy(object source)
    {
        var type = source.GetType();
        var copy = Activator.CreateInstance(type)!;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.CanRead && property.CanWrite)
                property.SetValue(copy, property.GetValue(source));
        return copy;
    }

    private static object StripObjects(object source, HashSet<RadObjectKey> objects)
    {
        var type = source.GetType();
        var copy = ShallowCopy(source);

        foreach (var (propertyName, kind) in _kindByArrayProperty)
        {
            var property = type.GetProperty(propertyName);
            if (property?.GetValue(source) is not Array items || items.Length == 0) continue;
            var kept = items.Cast<object>()
                .Where(item => !objects.Contains(KeyOf(item, kind)))
                .ToList();
            if (kept.Count != items.Length)
                property.SetValue(copy, ToTypedArray(property.PropertyType.GetElementType()!, kept));
        }

        var namespacesProperty = type.GetProperty("Namespaces");
        if (namespacesProperty?.GetValue(source) is Array namespaces && namespaces.Length > 0)
        {
            var rebuilt = namespaces.Cast<object>()
                .Select(child => StripObjects(child, objects))
                .ToList();
            namespacesProperty.SetValue(
                copy,
                ToTypedArray(namespacesProperty.PropertyType.GetElementType()!, rebuilt));
        }
        return copy;
    }

    // There is deliberately no "does this module declare any namespace" helper. One existed, and
    // it was the wrong question asked in the one place it mattered: whether a packaged object
    // reference re-resolves is decided by the binder BC gives the edited compilation unit
    // (`BinderFactory.VisitCompilationUnitInternal`, keyed on that unit's own
    // `NamespaceDeclaration`), never by what the module declares somewhere else. An app part-way
    // through adopting namespaces answers "yes" module-wide and still has namespace-free files that
    // resolve against the un-re-parented copy. `BcCompiler.DeclaresNoNamespace` asks per file.

    private static Array ToTypedArray(Type elementType, IReadOnlyList<object> items)
    {
        var array = Array.CreateInstance(elementType, items.Count);
        for (int i = 0; i < items.Count; i++) array.SetValue(items[i], i);
        return array;
    }

    /// <summary>
    /// Serialized public symbol surface of one codeunit. SymbolReference definitions do
    /// not contain procedure bodies, so this stays identical for a body-only edit and
    /// changes for signatures, access, subtype, event metadata, and other binding-visible
    /// changes. Only surface-stable edits are eligible for an overlay.
    /// </summary>
    public static string? CodeunitSurfaceFingerprint(NavSymRef.ModuleDefinition module, int id)
    {
        return ObjectSurfaceFingerprint(module, new RadObjectKey("Codeunit", id));
    }

    /// <summary>
    /// Serialized public symbol definition for one keyed AL object, canonicalised so that the
    /// two module definitions a delta compares — which are produced by DIFFERENT code paths —
    /// describe an unchanged surface identically.
    ///
    /// <para>The comparison is inherently between unlike producers, and that is what this has
    /// to absorb. The committed baseline comes from
    /// <c>SerializableSymbolModelConverter.ConvertModuleToSerializableSymbolModel(Compilation)</c>
    /// and stays an object graph; the merged one is written by
    /// <c>CompilationUtilities.WriteSymbolReference</c> and read back with
    /// <c>SymbolReferenceJsonReader</c>, so it has been through a JSON round trip the other
    /// never sees. A difference either side introduces for reasons of its own reads as "the
    /// surface moved", which rebinds the object's direct callers, whose fingerprints then
    /// differ for the same reason — the cascade is the failure mode, not the extra object.</para>
    ///
    /// <para>Two such differences are known, and both are handled by shape rather than by name,
    /// because a list of individual offending properties has already proved incomplete twice:</para>
    ///
    /// <list type="number">
    /// <item><b>Provenance.</b> A compile given an app root (#1912 — what the CLI passes on every
    ///   cycle) records <c>ReferenceSourceFileName</c>. This used to be asymmetric: the full
    ///   compile got a file system and the RAD one did not, so a re-emitted object came back with
    ///   that property null and EVERY modified object read as "surface moved". The delta is now
    ///   constructed with the same file system (<c>CreateForRad</c>'s <c>fileSystem</c>
    ///   parameter), and both producers record the identical relative path — measured on this
    ///   fixture as <c>"src/RadPerfService.Codeunit.al"</c> on both sides, so dropping this entry
    ///   keeps the whole suite green. It stays because the symmetry is the CALLER's to keep:
    ///   <c>appRootDir</c> is optional, and a caller that gives one side a file system and not
    ///   the other reproduces the cascade exactly (measured: the two
    ///   <c>WhenTheCompileRecordsSourceFileNames</c> tests both fail, the body edit pulling in
    ///   <c>RAD Perf Unrelated A</c>). Where a symbol was read from is not part of any binding
    ///   contract either way — two symbols identical but for the file they were read from bind
    ///   identically.</item>
    /// <item><b>Null versus empty.</b> The round trip materialises an absent collection as an
    ///   empty array where the converter left it null — measured on NP Retail's
    ///   `NPR Adyen Management`, whose five argument-less method attributes serialize as
    ///   <c>"Arguments":null</c> on one side and <c>"Arguments":[]</c> on the other. Ten
    ///   characters in a 36 KB surface, and every warm cycle on that app pulled in 30 caller
    ///   files and then failed to compile.</item>
    /// </list>
    ///
    /// <para>Dropping null and empty collections cannot hide a real change: a surface that
    /// genuinely gained or lost a member has it non-empty on exactly one side, so that side
    /// keeps the property and the two still differ. Pinned in both directions by
    /// RadObjectDeltaTests.ABodyEdit_StaysOneObject_WhenTheEditedCodeunitCarriesAnArgumentLessAttribute
    /// and ChangingAnAttributesArguments_StillCountsAsASurfaceMove.</para>
    /// </summary>
    public static string? ObjectSurfaceFingerprint(
        NavSymRef.ModuleDefinition module,
        RadObjectKey key) =>
        CanonicalObject(FindElements(module, key).FirstOrDefault())?.ToJsonString();

    /// <summary>
    /// Serialize one located element and reduce it to the form both producers agree on. Null
    /// when there is no element, or when it does not serialize to a JSON object at all — which
    /// every caller reads as "cannot be compared", never as "unchanged".
    /// </summary>
    private static JsonObject? CanonicalObject(object? element)
    {
        if (element == null) return null;
        if (JsonNode.Parse(JsonSerializer.Serialize(element, element.GetType()))
            is not JsonObject node)
            return null;
        Canonicalise(node);
        return node;
    }

    /// <summary>Properties that say where a symbol was read from, not what it offers.</summary>
    private static readonly string[] _provenanceProperties = ["ReferenceSourceFileName"];

    /// <summary>
    /// Reduce one serialized symbol to the form both producers agree on: no provenance, and no
    /// property that is absent — whether it says so with <c>null</c> or with an empty array.
    /// </summary>
    private static void Canonicalise(System.Text.Json.Nodes.JsonNode node)
    {
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject obj:
                foreach (var name in _provenanceProperties) obj.Remove(name);
                foreach (var child in obj.ToList())
                {
                    if (child.Value is { } value) Canonicalise(value);
                    if (IsAbsent(obj[child.Key])) obj.Remove(child.Key);
                }
                break;
            case System.Text.Json.Nodes.JsonArray array:
                foreach (var item in array)
                    if (item != null) Canonicalise(item);
                break;
        }
    }

    private static bool IsAbsent(System.Text.Json.Nodes.JsonNode? node) =>
        node == null || node is System.Text.Json.Nodes.JsonArray { Count: 0 };

    /// <summary>
    /// Whether one AL object's binding-visible surface moved between two module definitions —
    /// and, when the member-level rule could not be applied, the reason the whole-object
    /// compare answered in its place.
    /// </summary>
    /// <param name="Moved">
    /// True when something a caller was compiled against changed, so the object's direct users
    /// have to be re-emitted.
    /// </param>
    /// <param name="FailedClosedBecause">
    /// Null on the normal path. Non-null when <see cref="CompareObjectSurface"/> could not diff
    /// the two surfaces member by member, and then it states BOTH what stopped it and what was
    /// done instead — the two fail-closed classes take different actions, so the sentence has to
    /// carry the outcome. The caller prints it verbatim: a fallback that no one can see is the
    /// silent default `.claude/rules/loud-failures.md` exists to forbid.
    /// </param>
    public readonly record struct SurfaceComparison(bool Moved, string? FailedClosedBecause);

    /// <summary>
    /// Did anything a CALLER of <paramref name="key"/> was compiled against change between
    /// <paramref name="before"/> and <paramref name="after"/>?
    ///
    /// <para>This is the gate on the delta's one-hop rebind (BcCompiler.DeltaCompile's
    /// <c>changedSurfaces</c>), and through <c>RadWorkspaceUpdate.MovedSurfaces</c> on the
    /// cross-app one as well. Answering "yes" too often was assumed to be a cost rather than a
    /// correctness bug — a hub codeunit's callers get re-emitted and the cycle is slow.
    /// Measured on NP Retail, it is worse than that: adding one procedure to `NPR POS Session`
    /// under the whole-object rule widened to 312 direct-caller files plus 21 bystanders, and
    /// the widened delta then produced <c>EMIT-ZERO — 0 sources emitted, 130 AL error(s)</c> and
    /// ended the cycle in COMPILE FAIL, three cycles out of three. It never re-emitted those
    /// objects at all. Under the member-level rule the same edit is 1 object in 1.1–2.0 s.
    /// Answering "no" too often is a correctness bug of the other kind, and a SILENT one — see
    /// the overload hazard below.</para>
    ///
    /// <para><b>Codeunits are diffed member by member. Everything else is all-or-nothing.</b>
    /// The one thing generated code bakes about another object is the callee's METHOD ID, so
    /// for a codeunit the question decomposes: a member no caller could have bound to cannot
    /// invalidate one. It does not decompose for the id-less kinds this gate also covers — an
    /// interface's method set is a conformance contract, so a method ADDED to an interface
    /// breaks every implementor, and a control add-in's surface is what its users were compiled
    /// against wholesale. Those keep the whole-object compare.</para>
    ///
    /// <para><b>The rule, for a codeunit:</b></para>
    /// <list type="bullet">
    /// <item>the object's canonicalised shell — everything except <c>Methods</c> and
    ///   <c>Variables</c>, so <c>Properties</c>, <c>ImplementedInterfaces</c>, the name and the
    ///   id — compared wholesale. Subtracting two arrays rather than selecting the properties
    ///   that matter is deliberate: a per-property allowlist has already proved incomplete
    ///   twice on this file;</item>
    /// <item><c>Variables</c> is EXCLUDED. It is the codeunit's globals, and AL gives no
    ///   syntax for reading another object's globals — yet they are serialized, so adding one
    ///   moved the whole-object fingerprint and rebound every caller. Pinned by
    ///   RadMemberSurfaceTests.AddingAGlobalVariable_DoesNotRebindTheCaller, which also
    ///   asserts the global really does reach the serialized surface, so the exclusion cannot
    ///   pass by the edit having done nothing;</item>
    /// <item><c>Methods</c> compared as a multiset keyed on <c>(Name, Id)</c>, each member
    ///   fingerprinted as its whole canonicalised element. Order is therefore not part of the
    ///   comparison, so reordering procedures — which moves no id — stops rebinding
    ///   callers;</item>
    /// <item><b>moved</b> when a member is gone · when a member's fingerprint changed · when a
    ///   member was added under a name the object ALREADY had;</item>
    /// <item><b>unchanged</b> only when every addition is under a name new to the object.</item>
    /// </list>
    ///
    /// <para><b>Why "added under a name already on the object" is a trigger, and why the naive
    /// version of this rule is dangerous.</b> `CalculateMethodId` is method-local, so adding
    /// `Which(Integer)` beside `Which(Decimal)` moves NO existing member's id — but it moves
    /// the id the CALLER bakes, because an Integer argument now binds to the new overload
    /// instead of widening to the old one. The old id and its `case` label both survive in the
    /// re-emitted callee, so an un-rebound caller dispatches a member that still exists and
    /// gets the previous overload's answer: no exception, no diagnostic, a green cycle running
    /// the code the developer just stopped calling. Measured exactly that way against a runner
    /// taught the naive rule "the surface only grew, skip the rebind" — see
    /// RadSameAppOverloadWatchTests.</para>
    ///
    /// <para><b>Never keyed on "did the id move?".</b> Four contract changes leave a member's id
    /// bit-identical: access (`internal`), attributes (`[NonDebuggable]`, `[TryFunction]`,
    /// `[IntegrationEvent]`), parameter NAMES, and the return type's SUBTYPE — only the bare
    /// `NavTypeKind` is hashed, so `Codeunit A` → `Codeunit B` and `List of [Integer]` →
    /// `List of [Text]` are invisible to it. Comparing the whole member element catches all
    /// four; RadMemberSurfaceTests pins each one.</para>
    ///
    /// <para><b>Producer skew is what the canonicalisation absorbs.</b> The two sides come from
    /// different code paths — see <see cref="ObjectSurfaceFingerprint"/> — and the same
    /// canonicaliser runs here, over the whole element, before members are split out. Measured
    /// over a 15-method probe (RadProducerEquivalenceTests), raw serialization disagrees on
    /// exactly two members, both carrying an argument-less attribute. Left raw, those two read
    /// as changed on the first delta of any app using `[TryFunction]` or `[NonDebuggable]`,
    /// which rebinds their callers, whose own fingerprints then move for the same reason. The
    /// cascade is the failure mode, not the extra object.</para>
    ///
    /// <para><b>`local` methods are on neither producer's surface</b> — attribute or not. A
    /// `local`→`local` edit is invisible to this comparison in both directions; a
    /// `public`→`local` change correctly reads as a member removal. Measured, not worked
    /// around.</para>
    ///
    /// <para><b>Fail-closed, and in two different ways — the difference matters.</b></para>
    ///
    /// <list type="number">
    /// <item><b>The object cannot be located unambiguously</b> — absent from a side, present
    ///   TWICE on one, or not serializing to a JSON object. Here the answer is
    ///   <c>Moved</c> outright, because the whole-object compare is not a safe fallback: it
    ///   answers with the FIRST match, so with two copies present the verdict is decided by
    ///   array order, and the losing order is the dangerous one. A stale copy listed ahead of
    ///   the re-emitted one compares equal to the committed baseline, reads as "unchanged", and
    ///   leaves every caller dispatching the pre-edit shape — which is exactly the "went green
    ///   on the stale copy" failure <see cref="CountObjects"/> exists to let the RAD suites
    ///   assert against. Uniqueness is a precondition of BOTH comparisons, so it is checked for
    ///   every kind, before the codeunit/id-less split.</item>
    /// <item><b>The object is unique and readable, but its member list cannot be keyed</b> — a
    ///   member that is not an object, has no name, has no integer id, or shares
    ///   <c>(Name, Id)</c> with another. Here the whole-object compare IS the safe answer, and
    ///   answering "unchanged" from it is not a risk but the truth: it is a comparison of the
    ///   same single pair of elements over strictly MORE than the member diff looks at
    ///   (<c>Variables</c> included, order included), so two byte-identical serializations mean
    ///   nothing about the object changed. Returning <c>Moved</c> unconditionally here would
    ///   rebind a hub codeunit's whole caller set on every cycle forever, which is the cascade
    ///   this rule exists to remove.</item>
    /// </list>
    ///
    /// <para>Either way the reason comes back for the caller to print. It does not throw:
    /// unlike an unsupported AL surface, there IS a correct answer available here, and taking
    /// down a watch cycle instead of taking it would be the worse failure.</para>
    /// </summary>
    public static SurfaceComparison CompareObjectSurface(
        NavSymRef.ModuleDefinition before,
        NavSymRef.ModuleDefinition after,
        RadObjectKey key)
    {
        // At most two, because the only question asked of the count is "more than one?" — and
        // one walk per side is all either comparison below needs.
        var previousCopies = FindElements(before, key).Take(2).ToList();
        var currentCopies = FindElements(after, key).Take(2).ToList();
        if (previousCopies.Count > 1)
            return new SurfaceComparison(true, Ambiguous("the committed baseline"));
        if (currentCopies.Count > 1)
            return new SurfaceComparison(true, Ambiguous("the merged baseline"));

        var previousElement = CanonicalObject(previousCopies.FirstOrDefault());
        var currentElement = CanonicalObject(currentCopies.FirstOrDefault());
        // The pre-member-diff comparison, kept for the id-less kinds and as the class-2
        // fail-closed answer. A local function so the codeunit happy path never pays for it —
        // it re-renders a surface that reaches 36 KB on a real app. Safe to call at any point
        // below because TryReadSurface does not mutate what it is given.
        bool WholeObjectMoved() =>
            previousElement == null || currentElement == null
            || !string.Equals(
                previousElement.ToJsonString(),
                currentElement.ToJsonString(),
                StringComparison.Ordinal);

        if (!key.IsCodeunit)
            // Including the both-absent case, which is not a failure: an `entitlement` has no
            // serialized representation at all — no `Entitlements` array, no
            // `EntitlementDefinition` type — so every modified one reads as moved, silently and
            // correctly, exactly as it did before there was a member diff.
            return new SurfaceComparison(WholeObjectMoved(), null);

        if (previousElement == null)
            return new SurfaceComparison(true, Unreadable("the committed baseline"));
        if (currentElement == null)
            return new SurfaceComparison(true, Unreadable("the merged baseline"));

        if (!TryReadSurface(previousElement, out var previous, out var failure))
            return new SurfaceComparison(
                WholeObjectMoved(), Unkeyable("the committed baseline", failure!));
        if (!TryReadSurface(currentElement, out var current, out failure))
            return new SurfaceComparison(
                WholeObjectMoved(), Unkeyable("the merged baseline", failure!));

        if (!string.Equals(previous.Shell, current.Shell, StringComparison.Ordinal))
            return new SurfaceComparison(true, null);

        // Removed, or changed in any way at all — including the four the member id cannot see.
        foreach (var (member, fingerprint) in previous.Members)
            if (!current.Members.TryGetValue(member, out var now)
                || !string.Equals(now, fingerprint, StringComparison.Ordinal))
                return new SurfaceComparison(true, null);

        // Added. Safe only under a name the object did not already have: an addition that joins
        // an existing name is an overload, and overload resolution at every call site of that
        // name has to be redone.
        var names = previous.Members.Keys
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var member in current.Members.Keys)
            if (!previous.Members.ContainsKey(member) && names.Contains(member.Name))
                return new SurfaceComparison(true, null);

        return new SurfaceComparison(false, null);
    }

    /// <summary>
    /// The three fail-closed reasons, each stating what happened AND what was done about it —
    /// the two outcomes differ, so a reader of the log must not have to know which branch
    /// produced the line.
    /// </summary>
    private static string Ambiguous(string side) =>
        $"{side} holds more than one serialized copy of it, so which one answers would be "
        + "decided by array order rather than by the edit — treating the surface as moved";

    private static string Unreadable(string side) =>
        $"{side} holds no readable serialized copy of it — treating the surface as moved";

    private static string Unkeyable(string side, string failure) =>
        $"{side} {failure} — comparing the whole serialized object instead";

    /// <summary>
    /// One member's identity. The name is upper-cased for the same reason
    /// <see cref="RadObjectKey.For"/> upper-cases an object's: AL identifiers are
    /// case-insensitive, so two spellings of one member are one member.
    ///
    /// <para>The id is part of the key and the name alone is not, because overloads are
    /// distinguished ONLY by id — measured on a probe codeunit, <c>Pick(Decimal)</c> is
    /// 998637081 and <c>Pick(Integer)</c> is 998637083. Keying on the name would silently
    /// collapse an overload set to one member and lose whichever of them changed.</para>
    /// </summary>
    private readonly record struct MemberKey(string Name, int Id);

    /// <summary>
    /// One object's surface split the way <see cref="CompareObjectSurface"/> compares it: the
    /// canonicalised shell as a string, and its members keyed and fingerprinted individually.
    /// </summary>
    private readonly record struct MemberSurface(
        string Shell, IReadOnlyDictionary<MemberKey, string> Members);

    /// <summary>
    /// Split one already-canonicalised object element into the shell and the keyed member set,
    /// or say what stopped it. <paramref name="failure"/> completes the sentence "the committed
    /// baseline …", so it reads as a reason in the log line the caller writes.
    ///
    /// <para>It does NOT mutate <paramref name="element"/>. The caller may still need to render
    /// that same element whole — the fail-closed answer for everything reported here is the
    /// whole-object compare — and a half-stripped side would compare against an intact one.</para>
    /// </summary>
    private static bool TryReadSurface(
        JsonObject element,
        out MemberSurface surface,
        out string? failure)
    {
        surface = default;
        failure = null;

        var members = new Dictionary<MemberKey, string>();
        if (element["Methods"] is { } methods)
        {
            if (methods is not JsonArray declared)
            {
                failure = "serializes its `Methods` as something other than an array";
                return false;
            }
            foreach (var entry in declared)
            {
                if (entry is not JsonObject member)
                {
                    failure = "holds a member that is not a JSON object";
                    return false;
                }
                if (member["Name"] is not JsonValue nameNode
                    || !nameNode.TryGetValue<string>(out var name)
                    || string.IsNullOrEmpty(name))
                {
                    failure = "holds a member with no name";
                    return false;
                }
                if (member["Id"] is not JsonValue idNode || !idNode.TryGetValue<int>(out var id))
                {
                    failure = $"holds a member (`{name}`) with no integer id";
                    return false;
                }
                if (!members.TryAdd(new MemberKey(name.ToUpperInvariant(), id), member.ToJsonString()))
                {
                    failure = $"holds two members keyed `{name}`#{id}";
                    return false;
                }
            }
        }

        // The shell is everything else, copied out rather than stripped in place. Both omissions
        // are deliberate and for opposite reasons: `Methods` because it is compared member by
        // member above, `Variables` because a codeunit's globals are not part of any binding
        // contract. Method-LOCAL variables are unaffected — measured absent from the
        // serialization entirely (a `MethodDefinition` declares a `Variables` property and
        // neither producer populates it), so a member fingerprint is body-independent either way.
        var shell = new JsonObject();
        foreach (var property in element)
            if (property.Key is not ("Methods" or "Variables"))
                shell[property.Key] = property.Value?.DeepClone();
        surface = new MemberSurface(shell.ToJsonString(), members);
        return true;
    }

    /// <summary>
    /// Every codeunit in <paramref name="module"/> whose <c>TableNo</c> names one of
    /// <paramref name="tableNames"/>.
    ///
    /// <para>Why a delta has to ask: <c>TableNo</c> is serialized as the target table's NAME,
    /// and BC derives a <c>Run(Record)</c> overload from that relationship when it materialises
    /// the untouched codeunit's packaged surface. Stripping the table from the packaged baseline
    /// (which every delta that touches one must do, or its pre-edit shape shadows the edit) can
    /// therefore leave the codeunit present with its <c>TableNo</c> property intact but without
    /// that derived overload. Rebinding the codeunit from source reconstructs it.</para>
    ///
    /// <para>Measured on NP Retail: a cycle that rebound `AdyenSetup.Page.al` also had
    /// `NPR Adyen Reconciliation Hdr` in its change set, and the page's
    /// <c>AdyenRecreateRecDoc.Run(ReconHeader)</c> — untouched code that compiles clean cold —
    /// failed with `AL0126: No overload for method 'Run' takes 1 arguments`. The codeunit was
    /// present in the packaged definition with `TableNo` intact; only the table it names was
    /// gone.</para>
    ///
    /// <para>Names are compared case-insensitively because AL identifiers are.</para>
    ///
    /// <para><b>Deliberately narrow, and this is the part to read before extending it.</b>
    /// Many serialized properties name another object, but the bound semantic graph already
    /// records those edges and most plain pointers re-resolve without rebinding their owner.
    /// This helper exists for the measured, derived-surface loss above. Reports and queries
    /// whose dataitems lose their source table are the other measured table-target family and
    /// are handled separately by <see cref="DataItemsOn"/>. Extensions and interface users are
    /// discovered from their own workspace indexes. Reproduce another derived-surface loss
    /// before adding it here; see `.claude/rules/no-assumption-fixes.md`.</para>
    /// </summary>
    public static IReadOnlyList<RadObjectKey> CodeunitsWithTableNo(
        NavSymRef.ModuleDefinition module, IReadOnlyCollection<string> tableNames)
    {
        if (tableNames.Count == 0) return Array.Empty<RadObjectKey>();
        var wanted = tableNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<RadObjectKey>();
        foreach (var codeunit in AllElements(module, "Codeunits"))
            if (PropertyValue(codeunit, "TableNo") is { } target && wanted.Contains(target))
                found.Add(KeyOf(codeunit, "Codeunit"));
        return found;
    }

    /// <summary>
    /// Every report and query in <paramref name="module"/> with a dataitem whose source table is
    /// one of <paramref name="tableNames"/>.
    ///
    /// <para>Same derived-surface class as <see cref="CodeunitsWithTableNo"/>, on another kind of
    /// holder. A report dataitem and a query dataitem each record their table as a NAME — the
    /// serialized property is called <c>RelatedTable</c> on both. It is also the ONLY record of
    /// which table the dataitem's columns come from: a column serializes its source field name
    /// (<c>SourceColumn</c>) or nothing at all, never the table. Stripping the table can therefore
    /// cost an untouched report or query the ability to say what any of its columns are even
    /// though its own definition is intact; rebinding the holder from source reconstructs it.</para>
    ///
    /// <para>Two measurements, one cause, two very different-looking diagnostics — which is why
    /// they were originally filed as unrelated. With table X stripped and the holder untouched:
    /// a <c>reportextension</c> adding <c>column(…; Description)</c> to the report's dataitem
    /// fails with <c>AL0118 The name 'Description' does not exist in the current context</c>,
    /// and a codeunit reading <c>Host.QueryNo</c> off the query fails with <c>AL0386 A required
    /// package dependency could not be found</c>. Pinned by
    /// RadByNamePropertyShapesTests.ReportRelatedTable_… and .QueryRelatedTable_….</para>
    ///
    /// <para>Nested, because dataitems nest: a report's <c>DataItems</c> contains further
    /// <c>DataItems</c>, and a query holds them under <c>Elements</c>. Names are compared
    /// case-insensitively because AL identifiers are.</para>
    /// </summary>
    public static IReadOnlyList<RadObjectKey> DataItemsOn(
        NavSymRef.ModuleDefinition module, IReadOnlyCollection<string> tableNames)
    {
        if (tableNames.Count == 0) return Array.Empty<RadObjectKey>();
        var wanted = tableNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<RadObjectKey>();
        foreach (var (arrayProperty, kind) in new[] { ("Reports", "Report"), ("Queries", "Query") })
            foreach (var holder in AllElements(module, arrayProperty))
                if (NamesAnyDataItemTable(holder, wanted))
                    found.Add(KeyOf(holder, kind));
        return found;
    }

    private static readonly string[] _dataItemArrays = ["DataItems", "Elements"];

    private static bool NamesAnyDataItemTable(object element, HashSet<string> wanted)
    {
        if (element.GetType().GetProperty("RelatedTable")?.GetValue(element) is string related
            && wanted.Contains(related))
            return true;
        foreach (var arrayProperty in _dataItemArrays)
            if (element.GetType().GetProperty(arrayProperty)?.GetValue(element) is Array children)
                foreach (var child in children)
                    if (child != null && NamesAnyDataItemTable(child, wanted))
                        return true;
        return false;
    }

    /// <summary>
    /// The value of one AL property off a serialized definition's <c>Properties</c> array —
    /// the shape BC uses for <c>TableNo</c>, <c>Access</c> and the rest. Null when absent.
    /// </summary>
    private static string? PropertyValue(object element, string name)
    {
        if (element.GetType().GetProperty("Properties")?.GetValue(element) is not Array properties)
            return null;
        foreach (var property in properties)
        {
            if (property == null) continue;
            if (string.Equals(NameOf(property), name, StringComparison.OrdinalIgnoreCase))
                return property.GetType().GetProperty("Value")?.GetValue(property) as string;
        }
        return null;
    }

    private static IEnumerable<object> AllElements(object container, string arrayProperty)
    {
        var type = container.GetType();
        if (type.GetProperty(arrayProperty)?.GetValue(container) is Array items)
            foreach (var item in items)
                if (item != null) yield return item;
        if (type.GetProperty("Namespaces")?.GetValue(container) is Array namespaces)
            foreach (var child in namespaces)
            {
                if (child == null) continue;
                foreach (var hit in AllElements(child, arrayProperty)) yield return hit;
            }
    }

    /// <summary>
    /// How many serialized elements in <paramref name="module"/> answer to
    /// <paramref name="key"/>.
    ///
    /// <para>A merged baseline must hold at most one per key. Two mean a delta failed to
    /// strip its own pre-edit definition, and which of them a later compile resolves is then
    /// decided by array order rather than by the edit — so the cycle can go green on the
    /// stale shape. <see cref="ObjectSurfaceFingerprint"/> cannot see that: it answers with
    /// the first match, which is as likely to be the new copy as the old one. The RAD suites
    /// assert this count directly for exactly that reason.</para>
    /// </summary>
    public static int CountObjects(NavSymRef.ModuleDefinition module, RadObjectKey key) =>
        FindElements(module, key).Count();

    private static string? ArrayPropertyFor(string kind) =>
        _kindByArrayProperty.FirstOrDefault(pair => pair.Value == kind).Key;

    /// <summary>
    /// Whether <paramref name="kind"/> has a serialized array in <c>ModuleDefinition</c> at all,
    /// and therefore whether a delta can strip it and hand it back.
    ///
    /// <para>False for exactly one AL kind: <c>entitlement</c>. There is no <c>Entitlements</c>
    /// array and no <c>EntitlementDefinition</c> type, so a modified entitlement is neither
    /// stripped nor restorable — and a caller that expects to find one copy of everything it asked
    /// for would otherwise read that absence as a failure. Asked through this rather than by
    /// listing kinds at the call site, so <see cref="_kindByArrayProperty"/> stays the one place
    /// that knows.</para>
    /// </summary>
    public static bool HasSerializedForm(string kind) => ArrayPropertyFor(kind) != null;

    /// <summary>
    /// Whether <paramref name="module"/> holds <paramref name="key"/> in its own object arrays
    /// rather than inside one of its namespace nodes.
    ///
    /// <para>Asked before <see cref="WithObjectsFrom"/>, which appends at the top level: putting a
    /// namespaced definition there would MOVE it, and a definition that holds namespaces has its
    /// top-level arrays re-parented verbatim by the compiler
    /// (<c>RadReferenceModuleSymbol.CreateNamespaceDefinition</c>), so the move would be visible to
    /// every namespaced file's binder as an object in the wrong namespace. False is therefore the
    /// caller's signal to leave the object stripped and let the whole module answer.</para>
    /// </summary>
    public static bool HoldsAtTopLevel(NavSymRef.ModuleDefinition module, RadObjectKey key)
    {
        if (ArrayPropertyFor(key.Kind) is not { } propertyName) return false;
        return module.GetType().GetProperty(propertyName)?.GetValue(module) is Array items
            && items.Cast<object>().Any(item => item != null && KeyOf(item, key.Kind) == key);
    }

    private static IEnumerable<object> FindElements(object container, RadObjectKey key)
    {
        var arrayProperty = ArrayPropertyFor(key.Kind);
        return arrayProperty == null
            ? Enumerable.Empty<object>()
            : Elements(container, arrayProperty, key);
    }

    private static IEnumerable<object> Elements(object container, string arrayProperty, RadObjectKey key)
    {
        var type = container.GetType();
        if (type.GetProperty(arrayProperty)?.GetValue(container) is Array items)
            foreach (var item in items)
                if (item != null && KeyOf(item, key.Kind) == key) yield return item;
        if (type.GetProperty("Namespaces")?.GetValue(container) is Array namespaces)
            foreach (var child in namespaces)
            {
                if (child == null) continue;
                foreach (var hit in Elements(child, arrayProperty, key)) yield return hit;
            }
    }
}
