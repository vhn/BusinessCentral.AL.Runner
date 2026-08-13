using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;

namespace AlRunner.Rad;

/// <summary>One AL object as the workspace remembers it: enough to rebuild its change element.</summary>
public sealed record RadObjectRef(RadObjectKey Key, string Name, string Namespace);

/// <summary>
/// What a file declared that the workspace's object map cannot express. Everything a compile
/// fully recorded leaves this at its default, so the map holds an entry only for the rare file
/// a delta may NOT assume it can skip.
/// </summary>
/// <param name="DotNetPackage">
/// The file declares a <c>dotnet</c> package. Not an AL object, but it moves what every object
/// in the module binds against, and a RAD object compilation carries no package declaration
/// trees — <c>MergeRadBaseline</c> restores the previously committed <c>DotNetPackages</c>
/// wholesale. So editing one, and equally DELETING one, has to rebuild the module; the deleted
/// case is the reason this is remembered rather than read off the file each cycle.
/// </param>
/// <param name="Unrecorded">
/// The file declared more than the compile could record for it — a declaration with no usable
/// key, or one whose key another file also claimed. Without this flag such a file is
/// indistinguishable from one that declares nothing, so emptying it would look like a
/// comment-only edit while the object's symbol survived in the baseline.
/// </param>
public readonly record struct RadFileDeclarations(bool DotNetPackage, bool Unrecorded);

/// <summary>
/// Compiler state prepared by an AL emit but not made current until its generated C# has
/// compiled and loaded successfully. Keeping this token separate prevents a rejected
/// backend generation from advancing the next watch cycle's hashes or symbol baseline.
/// </summary>
internal sealed record RadWorkspaceUpdate(
    Dictionary<string, string> FileHashes,
    IReadOnlyDictionary<string, List<RadObjectRef>> ObjectsByFile,
    IReadOnlyDictionary<string, RadFileDeclarations> DeclarationsByFile,
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

    /// <summary>
    /// A reason this workspace will have to compile in full that is known BEFORE the cycle that
    /// pays for it. Failing to record a baseline makes every later cycle a full compile, and the
    /// cycle that discovers the failure is not the cycle the developer watches rebuilding — so
    /// the reason is parked here and consumed by the compile that acts on it.
    ///
    /// <para>Deliberately NOT used by the invalidation paths (a reference-surface change, the
    /// overlay-chain reset, a missing loaded module): those call <see cref="Invalidate"/>, which
    /// reports in the same cycle, and parking as well would say it twice.</para>
    /// </summary>
    internal string? PendingFullCompileReason { get; set; }

    internal string? TakePendingFullCompileReason()
    {
        var reason = PendingFullCompileReason;
        PendingFullCompileReason = null;
        return reason;
    }

    private readonly Dictionary<string, string> _fileHashes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<RadObjectRef>> _objectsByFile = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RadFileDeclarations> _declarationsByFile = new(StringComparer.Ordinal);
    private readonly Dictionary<RadObjectKey, HashSet<RadObjectKey>> _referencesByObject = new();
    private readonly Dictionary<RadObjectKey, RadObjectKey> _extensionTargets = new();

    /// <summary>
    /// Drop everything derived from a compile. Called when the reference surface moves,
    /// or when a delta compile fails and the next one must start from a full rebuild.
    /// </summary>
    public void Invalidate(string reason)
    {
        if (HasBaseline)
        {
            Console.Error.WriteLine($"  [watch] {ModuleName}: full rebuild — {reason}");
            // …and where the watch dashboard can show it: the bundle loop silences stderr.
            RadCycleNotes.FullCompile(ModuleName, reason);
        }
        Baseline = null;
        _fileHashes.Clear();
        _objectsByFile.Clear();
        _declarationsByFile.Clear();
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
    /// <param name="describeChange">
    /// Given the previous signature, the reason to report. Only the caller that builds the
    /// signature knows how to read one, and a whole-module rebuild has to say which facet
    /// moved — "app.json changed the app version" is recognisable; "the reference surface
    /// changed" reads like the delta path failing.
    /// </param>
    public bool ArmFor(string signature, Func<string, string>? describeChange = null)
    {
        if (ReferenceSignature != null && !string.Equals(ReferenceSignature, signature, StringComparison.Ordinal))
            Invalidate(describeChange?.Invoke(ReferenceSignature)
                ?? "the compilation's reference surface changed (dependencies, identity or preprocessor symbols)");
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

    /// <summary>
    /// What the previous compile saw in <paramref name="file"/> beyond the objects it could
    /// record there. The default answer covers both a fully recorded file and one this
    /// workspace has never seen — for a file that did not exist, "it declared nothing" is
    /// the truth rather than an assumption, which is what lets a delta accept a NEW file
    /// that declares no object instead of rebuilding the module for it.
    /// </summary>
    internal RadFileDeclarations DeclarationsIn(string file) =>
        _declarationsByFile.TryGetValue(file, out var declarations) ? declarations : default;

    public bool Declares(RadObjectKey key) =>
        _objectsByFile.Values.Any(list => list.Any(o => o.Key == key));

    public RadObjectRef? Object(RadObjectKey key) =>
        _objectsByFile.Values.SelectMany(list => list).FirstOrDefault(o => o.Key == key);

    /// <summary>Every object the last committed compile saw this app declare.</summary>
    internal IReadOnlyList<RadObjectRef> AllObjects() =>
        _objectsByFile.Values.SelectMany(list => list).ToArray();

    internal string? FileOf(RadObjectKey key) =>
        _objectsByFile.FirstOrDefault(pair => pair.Value.Any(item => item.Key == key)).Key;

    /// <summary>
    /// Every file declaring an object of <paramref name="kind"/>. Used for the two AL kinds
    /// whose relationship the dependency graph cannot express — see
    /// BcCompiler.DeltaCompile's entitlement handling — where the population is small enough
    /// by construction that "all of them" is cheaper than recording which named what.
    /// </summary>
    internal IReadOnlyList<string> FilesDeclaring(string kind) => _objectsByFile
        .Where(pair => pair.Value.Any(item => string.Equals(item.Key.Kind, kind, StringComparison.Ordinal)))
        .Select(pair => pair.Key)
        .ToArray();

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
            _declarationsByFile.Clear();
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
        // A delta re-reads exactly the files it touched, so within that scope its answer is
        // the whole answer: clear first, then record only what it found. Recording without
        // clearing would keep a stale flag alive for a file that no longer earns one.
        if (!update.Full)
            foreach (var path in update.ObjectsByFile.Keys) _declarationsByFile.Remove(path);
        foreach (var (path, declarations) in update.DeclarationsByFile)
            _declarationsByFile[path] = declarations;
        // Files that vanished from the tree take their per-file record with them.
        foreach (var gone in _objectsByFile.Keys.Where(p => !update.FileHashes.ContainsKey(p)).ToList())
            _objectsByFile.Remove(gone);
        foreach (var gone in _declarationsByFile.Keys.Where(p => !update.FileHashes.ContainsKey(p)).ToList())
            _declarationsByFile.Remove(gone);

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
        // A recorded baseline retires any parked "you have no baseline" reason, so it cannot be
        // reported against some unrelated future full compile.
        PendingFullCompileReason = null;
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

        // Say which of the four conditions blocked the warm reload, and for the common one say
        // which file. A branch switch that carries a different app.json lands here as well as
        // in ArmFor, and "clean metadata refresh" told the developer nothing about why the run
        // they were watching suddenly cost minutes.
        var blockers = new List<string>();
        if (!singleBundle)
            blockers.Add("this run has more than one bundle, so metadata cannot be kept warm");
        if (workspaces.Count == 0)
            blockers.Add("no delta workspace is warm for this bundle yet");
        else if (workspaces.Any(ws => !ws.HasBaseline))
            blockers.Add(
                $"{workspaces.Count(ws => !ws.HasBaseline)} app(s) in the bundle have no baseline");
        // Two different blockers, kept apart because they read as different problems and the
        // first one is almost always `app.json`.
        var inBundle = changedPaths.Where(path => IsWithin(path, root)).ToList();
        var notAlSource = Names(inBundle.Where(path =>
            !string.Equals(Path.GetExtension(path), ".al", StringComparison.OrdinalIgnoreCase)));
        var unownedAl = Names(inBundle.Where(path =>
            string.Equals(Path.GetExtension(path), ".al", StringComparison.OrdinalIgnoreCase)
            && !workspaces.Any(ws => IsWithin(path, ws.SourceRoot))));
        if (notAlSource.Count > 0)
            blockers.Add($"{List(notAlSource)} changed — not AL source, so warm metadata cannot be kept");
        if (unownedAl.Count > 0)
            blockers.Add($"{List(unownedAl)} changed — AL source that no warm app in this bundle owns");

        if (blockers.Count > 0)
            foreach (var ws in workspaces)
                ws.Invalidate(string.Join("; ", blockers));
        return blockers.Count == 0;

        static List<string> Names(IEnumerable<string> paths) => paths
            .Select(Path.GetFileName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Named files, capped: the point is recognition, and a branch switch can touch hundreds.
        static string List(List<string> names) =>
            string.Join(", ", names.Take(3))
            + (names.Count > 3 ? $" (+{names.Count - 3} more)" : string.Empty);
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
