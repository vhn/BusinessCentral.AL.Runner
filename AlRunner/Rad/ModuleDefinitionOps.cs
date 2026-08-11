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
            ["Entitlements"] = "Entitlement",
        };

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
                .Where(item => !objects.Contains(new RadObjectKey(kind, IdOf(item) ?? -1)))
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
        var property = _kindByArrayProperty.FirstOrDefault(pair => pair.Value == key.Kind).Key;
        if (property == null) return null;
        var element = FindElement(module, property, key.Id);
        return element == null
            ? null
            : System.Text.Json.JsonSerializer.Serialize(element, element.GetType());
    }

    private static object? FindElement(object container, string arrayProperty, int id)
    {
        var type = container.GetType();
        if (type.GetProperty(arrayProperty)?.GetValue(container) is Array items)
            foreach (var item in items)
                if (item != null && IdOf(item) == id) return item;
        if (type.GetProperty("Namespaces")?.GetValue(container) is Array namespaces)
            foreach (var child in namespaces)
            {
                if (child == null) continue;
                var hit = FindElement(child, arrayProperty, id);
                if (hit != null) return hit;
            }
        return null;
    }
}
