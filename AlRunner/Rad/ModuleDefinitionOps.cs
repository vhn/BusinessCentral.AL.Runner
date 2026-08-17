using System.Collections.Concurrent;
using System.Reflection;
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

    private static object StripObjects(object source, HashSet<RadObjectKey> objects)
    {
        var type = source.GetType();
        var copy = Activator.CreateInstance(type)!;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.CanRead && property.CanWrite)
                property.SetValue(copy, property.GetValue(source));

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
        RadObjectKey key)
    {
        var element = FindElements(module, key).FirstOrDefault();
        if (element == null) return null;
        var node = System.Text.Json.Nodes.JsonNode.Parse(
            System.Text.Json.JsonSerializer.Serialize(element, element.GetType()));
        if (node == null) return null;
        Canonicalise(node);
        return node.ToJsonString();
    }

    /// <summary>Properties that say where a symbol was read from, not what it offers.</summary>
    private static readonly string[] _provenanceProperties = System.Array.Empty<string>();

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
    /// Every codeunit in <paramref name="module"/> whose <c>TableNo</c> names one of
    /// <paramref name="tableNames"/>.
    ///
    /// <para>Why a delta has to ask: <c>TableNo</c> is serialized as the target table's NAME,
    /// and BC resolves that name against the packaged module definition alone — the syntax
    /// trees a RAD compilation is handed do not participate. So stripping a table from the
    /// packaged baseline (which every delta that touches one must do, or its pre-edit shape
    /// shadows the edit) silently costs every codeunit that names it its <c>Run(Record)</c>
    /// overload, even though the codeunit itself was not touched and its own definition still
    /// carries the property.</para>
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
    /// <c>TableNo</c> is not the only property that names another object by name — counted
    /// over NP Retail's own committed baseline, so is <c>SourceTable</c> (1,827 pages),
    /// <c>TableRelation</c> (2,910 tables), <c>CalcFormula</c> (899), <c>RunObject</c> (1,042),
    /// <c>Implementation</c> (592 enums) and the report/query <c>DataItem*</c> family. Any of
    /// them could in principle be left dangling by the same stripping.</para>
    ///
    /// <para>Only <c>TableNo</c> is handled because only <c>TableNo</c> has been OBSERVED to
    /// break, and it is the one with an obvious reason to: it decides whether a
    /// <c>Run(Record)</c> overload exists on the codeunit's PUBLIC surface, so losing it
    /// changes what other objects can bind to. The rest are field- or control-level metadata
    /// that no caller binds against — plausible, unproven, and not worth pre-emptively
    /// widening a delta for. Reproduce one before adding it here; see
    /// `.claude/rules/no-assumption-fixes.md`.</para>
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
    /// <para>Same mechanism as <see cref="CodeunitsWithTableNo"/> and the same reason a delta has
    /// to ask, on the other kind of holder. A report dataitem and a query dataitem each record
    /// their table as a NAME — the serialized property is called <c>RelatedTable</c> on both —
    /// and that name is resolved against the packaged module definition alone. It is also the
    /// ONLY record of which table the dataitem's columns come from: a column serializes its
    /// source field name (<c>SourceColumn</c>) or nothing at all, never the table. So stripping
    /// the table costs an untouched report or query the ability to say what any of its columns
    /// are, even though its own definition is intact.</para>
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

    private static IEnumerable<object> FindElements(object container, RadObjectKey key)
    {
        var arrayProperty = _kindByArrayProperty.FirstOrDefault(pair => pair.Value == key.Kind).Key;
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
