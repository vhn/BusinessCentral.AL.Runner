using System.Collections.Concurrent;
using System.Reflection;

namespace AlRunner.Rad;

/// <summary>
/// Explicit precedence for AL-output CLR types across reloads of the same app.
///
/// .NET cannot unload an assembly, so every <c>--watch</c> cycle leaves the previous
/// generation's <c>Codeunit60901</c> / <c>Record50000</c> / … in the AppDomain alongside
/// the fresh one. Every type finder in the runner resolves an AL object by walking
/// <c>AppDomain.CurrentDomain.GetAssemblies()</c> and taking the first name match, with a
/// bias towards the assembly currently being executed. That bias is enough for a
/// single-app bundle. It is NOT enough for a bundle of several apps: while app B's tests
/// run, the current assembly is B, so a call into app A falls through to the raw scan —
/// whose order is unspecified — and the stale A wins as often as not.
///
/// Measured, on a two-app bundle before this existed: editing the library app's
/// <c>Answer()</c> from 42 to 43 and re-running left the test asserting 42 GREEN. The
/// runner was executing the previous cycle's code and reporting a pass. That is the
/// worst possible failure mode — silent, and in the direction of false confidence.
///
/// So: ownership is recorded, not guessed. The newest generation of a module owns every
/// AL object type it declares; a name that a module used to declare and no longer does is
/// tombstoned, so a deleted object resolves to nothing instead of resurrecting from the
/// still-loaded previous generation.
/// </summary>
public static class AlObjectResolution
{
    // AL-output CLR type simple name (Codeunit60901, Record50000, …) -> owning assembly.
    private static readonly ConcurrentDictionary<string, Assembly> _owner = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> _tombstones = new(StringComparer.Ordinal);
    // Per workspace, the AL object type names its CURRENT generation declares.
    private static readonly ConcurrentDictionary<RadWorkspace, HashSet<string>> _declaredByWorkspace = new();
    private static readonly object _sync = new();

    /// <summary>
    /// Record <paramref name="asm"/> as the current generation of <paramref name="workspace"/>.
    /// Object types it declares become owned by it; names the previous generation declared
    /// and this one does not are tombstoned.
    /// </summary>
    public static void RegisterGeneration(RadWorkspace workspace, Assembly asm)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in LoadTypes(asm))
            if (IsAlObjectTypeName(t.Name) && t.Namespace == "Microsoft.Dynamics.Nav.BusinessApplication")
                declared.Add(t.Name);

        lock (_sync)
        {
            _declaredByWorkspace.TryGetValue(workspace, out var previous);
            foreach (var name in declared)
            {
                _owner[name] = asm;
                _tombstones.TryRemove(name, out _);
            }
            if (previous != null)
                foreach (var gone in previous)
                    if (!declared.Contains(gone) && _owner.TryGetValue(gone, out var owner) && owner != asm)
                    {
                        // The object was deleted from this module. Its type is still loaded
                        // in the previous generation; without a tombstone the finders would
                        // resurrect it and a test would pass against code that no longer
                        // exists in the source tree.
                        _owner.TryRemove(gone, out _);
                        _tombstones[gone] = 1;
                    }
            _declaredByWorkspace[workspace] = declared;
        }
    }

    /// <summary>
    /// Record <paramref name="asm"/> as a DELTA overlay. It declares only existing objects
    /// whose bodies changed, so everything absent keeps its previous owner and nothing can
    /// be tombstoned.
    /// </summary>
    public static void RegisterOverlay(Assembly asm)
    {
        var types = LoadTypes(asm);

        lock (_sync)
        {
            foreach (var t in types)
            {
                if (!IsAlObjectTypeName(t.Name) || t.Namespace != "Microsoft.Dynamics.Nav.BusinessApplication")
                    continue;
                _owner[t.Name] = asm;
                _tombstones.TryRemove(t.Name, out _);
            }
        }
    }

    /// <summary>
    /// Commit an object-granular generation. A deletion-only RAD cycle has no assembly;
    /// its removed names are still taken out of the workspace declaration set and
    /// tombstoned so an older loaded generation cannot resurrect them.
    /// </summary>
    public static void RegisterDelta(
        RadWorkspace workspace,
        Assembly? assembly,
        IEnumerable<string> removedTypeNames)
    {
        var declared = assembly == null
            ? Array.Empty<Type>()
            : LoadTypes(assembly)
                .Where(type => IsAlObjectTypeName(type.Name)
                    && type.Namespace == "Microsoft.Dynamics.Nav.BusinessApplication")
                .ToArray();
        var removed = removedTypeNames.ToHashSet(StringComparer.Ordinal);

        lock (_sync)
        {
            _declaredByWorkspace.TryGetValue(workspace, out var prior);
            var current = prior == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(prior, StringComparer.Ordinal);

            foreach (var name in removed)
            {
                current.Remove(name);
                _owner.TryRemove(name, out _);
                _tombstones[name] = 1;
            }
            foreach (var type in declared)
            {
                current.Add(type.Name);
                _owner[type.Name] = assembly!;
                _tombstones.TryRemove(type.Name, out _);
            }
            _declaredByWorkspace[workspace] = current;
        }
    }

    /// <summary>True when the object was deleted from its module and must not resolve.</summary>
    public static bool IsTombstoned(string typeName) => _tombstones.ContainsKey(typeName);

    /// <summary>
    /// The owning generation's type for <paramref name="typeName"/>, or null when no
    /// generation owns the name (leaving the caller's existing scan to answer). The
    /// <paramref name="requiredBase"/> check mirrors what every finder applies, so a
    /// name collision with a non-AL type can never satisfy the fast path.
    /// </summary>
    public static Type? FindOwned(string typeName, Type? requiredBase)
    {
        if (!_owner.TryGetValue(typeName, out var asm)) return null;
        try
        {
            var t = asm.GetType("Microsoft.Dynamics.Nav.BusinessApplication." + typeName);
            if (t == null) return null;
            return requiredBase == null || requiredBase.IsAssignableFrom(t) ? t : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// True when <paramref name="type"/> is an AL object type from a superseded generation.
    /// Scans that enumerate every loaded assembly (the event-subscriber registry, the
    /// record-type prewarm) must skip these or a replaced object registers twice.
    /// </summary>
    public static bool IsSuperseded(Type type)
    {
        var name = type.Name;
        if (_tombstones.ContainsKey(name)) return true;
        return _owner.TryGetValue(name, out var owner) && !ReferenceEquals(owner, type.Assembly);
    }

    private static Type[] LoadTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            var causes = string.Join(" | ", ex.LoaderExceptions
                .Where(e => e != null).Select(e => e!.Message).Distinct().Take(3));
            throw new InvalidOperationException(
                $"RAD could not register {assembly.GetName().Name}: some generated types " +
                $"failed to load{(causes.Length == 0 ? string.Empty : " — " + causes)}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"RAD could not inspect generated assembly {assembly.GetName().Name}: {ex.Message}", ex);
        }
    }

    // BC's emitter names AL objects `<Kind><id>` — Codeunit60901, Record50000, Page31,
    // Report1, Query7, XmlPort99, Enum60910. Nested helper types (method scopes) never
    // match because they carry a suffix, and no BC framework type in the
    // BusinessApplication namespace has this shape.
    private static bool IsAlObjectTypeName(string name)
    {
        int i = 0;
        while (i < name.Length && !char.IsAsciiDigit(name[i])) i++;
        if (i == 0 || i == name.Length) return false;
        for (int j = i; j < name.Length; j++)
            if (!char.IsAsciiDigit(name[j])) return false;
        return true;
    }
}
