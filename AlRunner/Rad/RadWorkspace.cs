using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;

namespace AlRunner.Rad;

/// <summary>One AL object as the workspace remembers it: enough to rebuild its change element.</summary>
public sealed record RadObjectRef(RadObjectKey Key, string Name, string Namespace);

/// <summary>
/// Compiler state prepared by an AL emit but not made current until its generated C# has
/// compiled and loaded successfully. Keeping this token separate prevents a rejected
/// backend generation from advancing the next watch cycle's hashes or symbol baseline.
/// </summary>
internal sealed record RadWorkspaceUpdate(
    Dictionary<string, string> FileHashes,
    IReadOnlyDictionary<string, List<RadObjectRef>> ObjectsByFile,
    IReadOnlyDictionary<RadObjectKey, HashSet<RadObjectKey>> ReferencesByObject,
    IReadOnlyDictionary<RadObjectKey, RadObjectKey> ExtensionTargets,
    IReadOnlyCollection<RadObjectKey> RemovedObjects,
    object Baseline,
    bool Full);

/// <summary>
/// Everything the RAD delta path must remember about one AL app between watch cycles:
/// per-file content hashes, which objects each file declares, the compiler's stable
/// symbol baseline to bind the next delta against, and the loaded assembly generations.
///
/// Held per app identity for the life of the process — the whole point of a resident
/// <c>--watch</c> daemon is that this state survives the edit.
/// </summary>
public sealed class RadWorkspace
{
    private readonly string _assemblyNamePrefix;
    private int _assemblyGeneration;

    public RadWorkspace(string moduleName, string sourceRoot)
    {
        ModuleName = moduleName;
        SourceRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar);
        _assemblyNamePrefix = $"{moduleName}#rad{Guid.NewGuid():N}";
    }

    public string ModuleName { get; }
    public string SourceRoot { get; }

    /// <summary>
    /// A process-unique assembly name for each full or overlay generation. Loaded
    /// assemblies cannot be unloaded, so reusing an identity after a chain refresh can
    /// make the CLR bind a new overlay to an older same-named generation.
    /// </summary>
    public string NextAssemblyName() =>
        $"{_assemblyNamePrefix}g{Interlocked.Increment(ref _assemblyGeneration)}";

    /// <summary>
    /// Signature of everything a delta may NOT change: the resolved dependency specs,
    /// the preprocessor symbol set, the app identity. A change here alters what the
    /// compilation binds against, which no codeunit overlay can express — the workspace
    /// invalidates itself and the next compile is a full rebuild.
    /// </summary>
    public string? ReferenceSignature { get; private set; }

    /// <summary>
    /// Compiler symbol picture from the last full emit. Accepted overlays have an identical
    /// exported surface, so this remains the correct baseline until a structural edit.
    /// </summary>
    public object? Baseline { get; private set; }

    /// <summary>Loaded generations, oldest first. Element 0 is the baseline assembly.</summary>
    public List<Assembly> Generations { get; } = new();

    public bool HasBaseline => Baseline != null;

    private readonly Dictionary<string, string> _fileHashes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<RadObjectRef>> _objectsByFile = new(StringComparer.Ordinal);
    private readonly Dictionary<RadObjectKey, HashSet<RadObjectKey>> _referencesByObject = new();
    private readonly Dictionary<RadObjectKey, RadObjectKey> _extensionTargets = new();

    /// <summary>
    /// Drop everything derived from a compile. Called when the reference surface moves,
    /// or when a delta compile fails and the next one must start from a full rebuild.
    /// </summary>
    public void Invalidate(string reason)
    {
        if (HasBaseline)
            Console.Error.WriteLine($"  [rad] {ModuleName}: full rebuild — {reason}");
        Baseline = null;
        _fileHashes.Clear();
        _objectsByFile.Clear();
        _referencesByObject.Clear();
        _extensionTargets.Clear();
        // Generations are deliberately kept: the assemblies are already loaded into the
        // process and .NET cannot unload them. A full rebuild adds a new generation that
        // supersedes all of them (see AlObjectResolution).
    }

    /// <summary>
    /// Re-arm the workspace against <paramref name="signature"/>, invalidating if it moved.
    /// Returns true when the workspace can serve a delta.
    /// </summary>
    public bool ArmFor(string signature)
    {
        if (ReferenceSignature != null && !string.Equals(ReferenceSignature, signature, StringComparison.Ordinal))
            Invalidate("the compilation's reference surface changed (dependencies, identity or preprocessor symbols)");
        ReferenceSignature = signature;
        return HasBaseline;
    }

    /// <summary>
    /// Content hashes for every <c>.al</c> file under <paramref name="dirs"/>. Hashed in
    /// parallel: a 7,000-file / 50 MB tree costs well under a second, and content — not
    /// mtime — is what decides whether an object must be recompiled, so a touch-without-edit
    /// (git checkout, formatter no-op) does not trigger one.
    /// </summary>
    public static Dictionary<string, string> HashSourceTree(IReadOnlyList<string> alFiles)
    {
        var result = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        Parallel.ForEach(alFiles, f =>
        {
            try
            {
                using var sha = SHA256.Create();
                using var fs = File.OpenRead(f);
                result[f] = Convert.ToHexString(sha.ComputeHash(fs));
            }
            catch (IOException)
            {
                // A save in flight: treat as changed so the next cycle picks it up.
                result[f] = "unreadable-" + Guid.NewGuid().ToString("N");
            }
        });
        return new Dictionary<string, string>(result, StringComparer.Ordinal);
    }

    /// <summary>
    /// Which files changed since the last compile. Object-level classification needs the
    /// changed files to be parsed, so that happens in <see cref="BcCompiler"/>; this only
    /// answers "which files".
    /// </summary>
    public (List<string> Changed, List<string> Removed) DiffFiles(Dictionary<string, string> current)
    {
        var changed = new List<string>();
        foreach (var (path, hash) in current)
            if (!_fileHashes.TryGetValue(path, out var old) || !string.Equals(old, hash, StringComparison.Ordinal))
                changed.Add(path);
        var removed = _fileHashes.Keys.Where(p => !current.ContainsKey(p)).ToList();
        changed.Sort(StringComparer.Ordinal);
        removed.Sort(StringComparer.Ordinal);
        return (changed, removed);
    }

    /// <summary>Objects the previous compile saw declared in <paramref name="file"/>.</summary>
    public IReadOnlyList<RadObjectRef> ObjectsIn(string file) =>
        _objectsByFile.TryGetValue(file, out var list) ? list : Array.Empty<RadObjectRef>();

    public bool Declares(RadObjectKey key) =>
        _objectsByFile.Values.Any(list => list.Any(o => o.Key == key));

    public RadObjectRef? Object(RadObjectKey key) =>
        _objectsByFile.Values.SelectMany(list => list).FirstOrDefault(o => o.Key == key);

    internal string? FileOf(RadObjectKey key) =>
        _objectsByFile.FirstOrDefault(pair => pair.Value.Any(item => item.Key == key)).Key;

    internal IReadOnlyList<RadObjectKey> DirectUsersOf(IEnumerable<RadObjectKey> targets)
    {
        var wanted = targets.ToHashSet();
        return _referencesByObject
            .Where(pair => pair.Value.Overlaps(wanted))
            .Select(pair => pair.Key)
            .Distinct()
            .ToArray();
    }

    internal bool TryGetExtensionTarget(RadObjectKey extension, out RadObjectKey target) =>
        _extensionTargets.TryGetValue(extension, out target);

    /// <summary>
    /// Record the outcome of a compile: source hashes, object locations and symbol baseline.
    /// </summary>
    internal void Commit(RadWorkspaceUpdate update)
    {
        if (update.Full)
        {
            _objectsByFile.Clear();
            _referencesByObject.Clear();
            _extensionTargets.Clear();
        }
        _fileHashes.Clear();
        foreach (var (path, hash) in update.FileHashes) _fileHashes[path] = hash;
        foreach (var (path, objs) in update.ObjectsByFile)
        {
            if (objs.Count == 0) _objectsByFile.Remove(path);
            else _objectsByFile[path] = objs;
        }
        // Files that vanished from the tree take their object mapping with them.
        foreach (var gone in _objectsByFile.Keys.Where(p => !update.FileHashes.ContainsKey(p)).ToList())
            _objectsByFile.Remove(gone);

        var refreshed = update.ObjectsByFile.Values.SelectMany(items => items)
            .Select(item => item.Key)
            .Concat(update.RemovedObjects)
            .ToHashSet();
        foreach (var key in refreshed)
        {
            _referencesByObject.Remove(key);
            _extensionTargets.Remove(key);
        }
        foreach (var (source, targets) in update.ReferencesByObject)
            _referencesByObject[source] = new HashSet<RadObjectKey>(targets);
        foreach (var (extension, target) in update.ExtensionTargets)
            _extensionTargets[extension] = target;

        Baseline = update.Baseline;
    }
}

/// <summary>
/// Process-wide store of <see cref="RadWorkspace"/>s, keyed by app identity and source root. A watch
/// cycle re-enters the same bundle loop, so the workspace has to be found again from
/// nothing but the app's identity.
/// </summary>
public static class RadWorkspaceStore
{
    private static readonly ConcurrentDictionary<string, RadWorkspace> _byKey = new(StringComparer.Ordinal);

    /// <summary>
    /// True when the RAD delta path is armed. Off by default: it is only ever a win for a
    /// resident process that recompiles the same app repeatedly, and a one-shot run pays
    /// the bookkeeping for nothing. <c>--watch</c> turns it on.
    /// </summary>
    public static bool Enabled { get; set; }

    public static RadWorkspace For(string moduleName, Guid? appId, string sourceRoot)
    {
        var identity = appId?.ToString("N") ?? "name:" + moduleName;
        var key = identity + "|" + Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar);
        return _byKey.GetOrAdd(key, _ => new RadWorkspace(moduleName, sourceRoot));
    }

    /// <summary>
    /// Decide whether a watch reload may retain compiler-captured metadata. Once every
    /// app has a committed baseline, an AL delta overwrites only the metadata entries it
    /// emits and removes its own tombstones at commit; untouched entries can stay warm.
    /// Manifest changes and separately-run bundles still require a clean full refresh.
    /// </summary>
    public static bool PrepareBundleReload(
        string bundleRoot, IReadOnlyCollection<string> changedPaths, bool singleBundle)
    {
        var root = Path.GetFullPath(bundleRoot).TrimEnd(Path.DirectorySeparatorChar);
        var workspaces = _byKey.Values.Where(ws => IsWithin(ws.SourceRoot, root)).ToList();
        bool preserve = singleBundle
            && workspaces.Count > 0
            && workspaces.All(ws => ws.HasBaseline)
            && changedPaths.Where(path => IsWithin(path, root)).All(path =>
            {
                var owner = workspaces.FirstOrDefault(ws => IsWithin(path, ws.SourceRoot));
                return owner != null
                    && string.Equals(Path.GetExtension(path), ".al", StringComparison.OrdinalIgnoreCase);
            });
        if (!preserve)
            foreach (var ws in workspaces)
                ws.Invalidate("the bundle needs a clean metadata refresh");
        return preserve;
    }

    private static bool IsWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// A backend failure after one app's AL state advanced makes the whole topologically
    /// ordered bundle suspect. Force its peers through one full compile on the next cycle;
    /// failure recovery is rare, and this avoids preserving a dependent bound to symbols
    /// that never produced a runnable assembly.
    /// </summary>
    public static void InvalidatePeers(RadWorkspace source, string reason)
    {
        foreach (var ws in _byKey.Values)
            if (!ReferenceEquals(ws, source)) ws.Invalidate(reason);
    }

}
