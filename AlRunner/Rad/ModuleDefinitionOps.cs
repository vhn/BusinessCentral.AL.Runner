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

    /// <summary>Serialized public symbol definition for one keyed AL object.</summary>
    public static string? ObjectSurfaceFingerprint(
        NavSymRef.ModuleDefinition module,
        RadObjectKey key)
    {
        var element = FindElements(module, key).FirstOrDefault();
        return element == null
            ? null
            : System.Text.Json.JsonSerializer.Serialize(element, element.GetType());
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
