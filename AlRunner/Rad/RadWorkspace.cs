using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;

namespace AlRunner.Rad;

/// <summary>One AL object as the workspace remembers it: enough to rebuild its change element.</summary>
public sealed record RadObjectRef(RadObjectKey Key, string Name, string Namespace);

/// <summary>
/// One AL object in a NAMED app — what a reference edge needs when the target lives in a
/// DIFFERENT app of the same bundle.
///
/// <para><see cref="RadObjectKey"/> is deliberately not widened to carry this. It is the key
/// type of the object map, the extension-target map, <c>RadChangeSet</c> and every RAD test,
/// and it is scoped to one app by construction: two apps can each declare
/// <c>interface "Contract"</c> and both key as <c>("Interface", 0, "CONTRACT")</c>. Qualifying
/// only the cross-app edges keeps that scoping intact and leaves every same-app path
/// unchanged.</para>
/// </summary>
/// <param name="App">
/// The producing app's workspace identity, exactly as <see cref="RadWorkspaceStore.IdentityOf"/>
/// computes it — an <c>app.json</c> id when there is one, and <c>name:&lt;module&gt;</c> when
/// there is not. The <c>app.json</c>-less case is not hypothetical: a bundle's orphan suites are
/// merged into one <c>AppGroup</c> with a null <c>AppId</c>, and the compilation it produces
/// still has a Guid (a deterministic hash of the module name), so the two cannot be matched on
/// the compiler's Guid alone.
/// </param>
public readonly record struct RadAppObjectRef(string App, RadObjectKey Key);

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
/// <param name="CrossAppReferencesByObject">
/// The same one-hop relation as <paramref name="ReferencesByObject"/>, for targets in a
/// SIBLING SOURCE APP of the same bundle. Separate rather than merged because the two are
/// consumed by different rules — the same-app map drives <c>DirectUsersOf</c> within one
/// compile, this one drives the cross-workspace rebind between them — and because keeping the
/// same-app map's value type is what leaves every existing path untouched.
/// </param>
/// <param name="MovedSurfaces">
/// The objects whose CALLABLE SURFACE this compile moved, and which therefore have to rebind
/// their users: exactly what <c>BcCompiler.DeltaCompile</c> feeds to <c>DirectUsersOf</c> for
/// the same app. Carried on the token so the commit — which is the first moment the generation
/// is known to have loaded — can publish it to the other apps in the bundle. A commit that
/// carries none publishes none, which is what keeps a body-only edit from rebinding anything.
/// </param>
internal sealed record RadWorkspaceUpdate(
    Dictionary<string, string> FileHashes,
    IReadOnlyDictionary<string, List<RadObjectRef>> ObjectsByFile,
    IReadOnlyDictionary<string, RadFileDeclarations> DeclarationsByFile,
    IReadOnlyDictionary<RadObjectKey, HashSet<RadObjectKey>> ReferencesByObject,
    IReadOnlyDictionary<RadObjectKey, HashSet<RadAppObjectRef>> CrossAppReferencesByObject,
    IReadOnlyDictionary<RadObjectKey, RadObjectKey> ExtensionTargets,
    IReadOnlyCollection<RadObjectKey> RemovedObjects,
    IReadOnlyCollection<RadObjectKey> MovedSurfaces,
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

    public RadWorkspace(string moduleName, string sourceRoot, string? identity = null, string? bundleRoot = null)
    {
        ModuleName = moduleName;
        SourceRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar);
        Identity = identity ?? RadWorkspaceStore.IdentityOf(null, moduleName);
        BundleRoot = bundleRoot == null
            ? SourceRoot
            : Path.GetFullPath(bundleRoot).TrimEnd(Path.DirectorySeparatorChar);
        _assemblyNamePrefix = $"{moduleName}#rad{Guid.NewGuid():N}";
    }

    public string ModuleName { get; }
    public string SourceRoot { get; }

    /// <summary>
    /// How the other apps of this bundle name this one in their reference graphs — the same
    /// string <see cref="RadWorkspaceStore.For"/> keys the store by. See
    /// <see cref="RadAppObjectRef"/> for why it is a string and not the compiler's Guid.
    /// </summary>
    public string Identity { get; }

    /// <summary>
    /// The bundle this app was compiled as part of. The cross-app queries are scoped by it and
    /// not by identity alone: <see cref="RadWorkspaceStore"/>'s map is process-wide, never
    /// cleared, and its key admits the SAME app id at two different source roots — so an
    /// unscoped lookup can hand one checkout's consumers a producer from another.
    /// </summary>
    public string BundleRoot { get; }

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
    private readonly Dictionary<RadObjectKey, HashSet<RadAppObjectRef>> _crossAppReferences = new();
    private readonly Dictionary<RadObjectKey, RadObjectKey> _extensionTargets = new();

    // ── cross-app surface moves: a committed broadcast, not a drainable event ──────────
    //
    // Generated calls bake Microsoft's member id, and an id is a hash of the callee's
    // signature — so when app A moves a callable surface, every app that CALLS it holds IL
    // that dispatches the previous id, whether or not the caller's own source moved. Only a
    // cross-app edge can say so, and until this existed the graph held none.
    //
    // Why a generation counter and a per-consumer watermark rather than a queue the way
    // RadCycleNotes drains one: a bundle can have TWO dependents of one producer, and a
    // drained signal is consumed by whichever asks first — the second one then never learns
    // the surface moved and is left dispatching the old id, silently. A watermark is read, not
    // taken, so every consumer sees every publish exactly once regardless of order, and a
    // consumer that compiles BEFORE its producer in a cycle (BuildAppGroups falls back to
    // declaration order on a dependency cycle) simply picks the signal up on the next one
    // instead of dropping it.
    //
    // Deliberately NOT committed state and NOT persisted: a generation is a process-local
    // counter. A watermark restored from disk would be compared against a fresh producer's
    // counter starting at zero, so every publish would read as already-consumed and the rebind
    // would be suppressed — the same silent staleness this exists to remove, reintroduced by
    // the persistence of the fix.
    private long _publishGeneration;
    private long _fullRebuildGeneration;
    private readonly Dictionary<RadObjectKey, long> _surfaceMoveGenerations = new();
    private readonly Dictionary<string, long> _producerWatermarks = new(StringComparer.Ordinal);

    /// <summary>
    /// Reverse index over <see cref="_objectsByFile"/>: which file declares a given key.
    ///
    /// <para>Deliberately NOT committed state. It is a pure function of
    /// <see cref="_objectsByFile"/>, rebuilt from it by <see cref="ReindexObjectFiles"/> at the
    /// end of every <see cref="Commit"/>, so it cannot drift from the map it indexes and there
    /// is nothing extra for <see cref="RadWorkspaceUpdate"/> or the sidecar to carry. A map
    /// that is genuinely new state must go through the token instead — see
    /// <see cref="Snapshot"/>.</para>
    /// </summary>
    private readonly Dictionary<RadObjectKey, string> _fileOfObject = new();

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
        _crossAppReferences.Clear();
        _extensionTargets.Clear();
        _fileOfObject.Clear();
        // The publish history and the watermarks deliberately survive. They are not derived
        // from a compile: the history is what OTHER apps have not consumed yet — dropping it
        // would silently un-tell them — and a watermark is refreshed by this app's next
        // commit anyway.
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
    /// Every <c>.al</c> file under <paramref name="alFolders"/>, as the compile sees them.
    ///
    /// <para>Shared rather than repeated because two callers must agree on the file SET or
    /// the disagreement is silent: <see cref="BcCompiler.EmitIncremental"/> decides what
    /// changed from it, and <see cref="RadBaselineSidecar"/> validates a persisted baseline
    /// against it. A sidecar enumerated differently from the compile would reject on a
    /// count mismatch — costing a full compile with no way to see why.</para>
    /// </summary>
    public static List<string> EnumerateAlFiles(IEnumerable<string> alFolders) => alFolders
        .Where(Directory.Exists)
        .Distinct()
        .SelectMany(d => Directory.EnumerateFiles(d, "*.al", SearchOption.AllDirectories))
        .Distinct()
        .ToList();

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

    /// <summary>
    /// The file the previous compile saw declare <paramref name="key"/>, or null.
    ///
    /// <para>Answered from <see cref="_fileOfObject"/> rather than by scanning. The scan was
    /// O(files x objects-per-file) PER KEY, and every caller asks in bulk: once per declared
    /// object in the delta's added-vs-modified classifier, and once per widened caller — twice
    /// over, since a widened cycle recurses. Measured fan-in on NP Retail is p50 2 files, p90
    /// 10, p99 59, max 435, so widening the rebind rules multiplies the call count against a
    /// 7,000-object map.</para>
    /// </summary>
    internal string? FileOf(RadObjectKey key) =>
        _fileOfObject.TryGetValue(key, out var file) ? file : null;

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

    /// <summary>
    /// The objects THIS app declares that reference something in <paramref name="producer"/> —
    /// restricted to <paramref name="keys"/>, or all of them when it is null.
    ///
    /// <para>Null means the producer rebuilt in full and cannot say which of its surfaces
    /// moved; see <see cref="PublishSurfaceMoves"/>.</para>
    /// </summary>
    internal IReadOnlyList<RadObjectKey> CrossAppUsersOf(
        string producer, IReadOnlySet<RadObjectKey>? keys) => _crossAppReferences
        .Where(pair => pair.Value.Any(target =>
            string.Equals(target.App, producer, StringComparison.Ordinal)
            && (keys == null || keys.Contains(target.Key))))
        .Select(pair => pair.Key)
        .ToArray();

    /// <summary>Every sibling app this workspace's committed graph holds an edge into.</summary>
    internal IReadOnlyCollection<string> CrossAppProducers() => _crossAppReferences.Values
        .SelectMany(targets => targets)
        .Select(target => target.App)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Announce the surfaces a just-LOADED generation moved, so the other apps in the bundle
    /// can rebind the calls that bake their member ids.
    ///
    /// <para><paramref name="fullRebuild"/> is not "a bigger version of the same thing". A full
    /// compile is preceded by <see cref="Invalidate"/>, which drops the object map — so by the
    /// time the new module exists there is no record of what the previous one declared and a
    /// per-key answer cannot be reconstructed. Broadcasting "assume everything moved" is the
    /// honest answer, and it is the correct one: the reasons a watch cycle rebuilds in full —
    /// a dependency, identity or preprocessor-symbol change, a <c>dotnet</c> package, an edit
    /// the delta could not classify — are exactly the ones able to move any member id in the
    /// module. Snapshotting the surfaces before invalidating was the alternative and was
    /// rejected: it would fingerprint a whole module on the cycle that is already the expensive
    /// one, to sharpen the rare case.</para>
    /// </summary>
    internal void PublishSurfaceMoves(IReadOnlyCollection<RadObjectKey> keys, bool fullRebuild)
    {
        if (fullRebuild)
        {
            _fullRebuildGeneration = ++_publishGeneration;
            return;
        }
        if (keys.Count == 0) return;
        var generation = ++_publishGeneration;
        foreach (var key in keys) _surfaceMoveGenerations[key] = generation;
    }

    internal long PublishGeneration => _publishGeneration;

    internal long WatermarkFor(string producer) =>
        _producerWatermarks.TryGetValue(producer, out var generation) ? generation : 0;

    internal void RecordConsumed(string producer, long generation) =>
        _producerWatermarks[producer] = generation;

    /// <summary>
    /// What this app has published since <paramref name="watermark"/>. <c>Everything</c> means
    /// a full rebuild landed and no per-key answer exists.
    /// </summary>
    internal (bool Everything, IReadOnlySet<RadObjectKey> Keys) MovesSince(long watermark)
    {
        if (_fullRebuildGeneration > watermark)
            return (true, new HashSet<RadObjectKey>());
        return (false, _surfaceMoveGenerations
            .Where(pair => pair.Value > watermark)
            .Select(pair => pair.Key)
            .ToHashSet());
    }

    internal bool TryGetExtensionTarget(RadObjectKey extension, out RadObjectKey target) =>
        _extensionTargets.TryGetValue(extension, out target);

    /// <summary>
    /// Every extension object whose target is in <paramref name="targets"/>.
    ///
    /// <para>A tableextension's field, an enumextension's value, a pageextension's control and
    /// a reportextension's column exist on the TARGET, not on the extension — so when a delta
    /// strips the target and rebuilds it from syntax, the extension's contribution is not in
    /// that syntax and does not come back. The extension has to be rebound from source in the
    /// same cycle, and this is how it is found.</para>
    ///
    /// <para>Scanned rather than indexed on purpose: the map holds only extension objects, so
    /// it is a small fraction of the app, and unlike a caller set it does not grow with the
    /// call graph. An object has the extensions it has.</para>
    /// </summary>
    internal IReadOnlyList<RadObjectKey> ExtensionsTargeting(IEnumerable<RadObjectKey> targets)
    {
        var wanted = targets.ToHashSet();
        if (wanted.Count == 0) return Array.Empty<RadObjectKey>();
        return _extensionTargets
            .Where(pair => wanted.Contains(pair.Value))
            .Select(pair => pair.Key)
            .ToArray();
    }

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
            _crossAppReferences.Clear();
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
            _crossAppReferences.Remove(key);
            _extensionTargets.Remove(key);
        }
        foreach (var (source, targets) in update.ReferencesByObject)
            _referencesByObject[source] = new HashSet<RadObjectKey>(targets);
        foreach (var (source, targets) in update.CrossAppReferencesByObject)
            if (targets.Count > 0) _crossAppReferences[source] = new HashSet<RadAppObjectRef>(targets);
        foreach (var (extension, target) in update.ExtensionTargets)
            _extensionTargets[extension] = target;

        ReindexObjectFiles();

        Baseline = update.Baseline;
        // A recorded baseline retires any parked "you have no baseline" reason, so it cannot be
        // reported against some unrelated future full compile.
        PendingFullCompileReason = null;
    }

    /// <summary>
    /// Rebuild <see cref="_fileOfObject"/> from <see cref="_objectsByFile"/>.
    ///
    /// <para>Wholesale rather than incrementally, because a commit already touches the map by
    /// path (add, replace, remove and two vanished-file prunes) and reconstructing which keys
    /// each of those moved is more ways to be wrong than a rebuild is to be slow: one pass over
    /// the objects the app declares, against a compile that just parsed and bound them.</para>
    ///
    /// <para><see cref="Dictionary{TKey,TValue}.TryAdd"/>, not the indexer: a key claimed by two
    /// files is the duplicate-declaration case, and the FIRST file wins so that
    /// <c>BcCompiler.DeltaCompile</c>'s ownership guard still finds a declaring file to name.
    /// Which of the two it names was already unspecified — the scan this replaces returned
    /// whichever came first in dictionary order.</para>
    /// </summary>
    private void ReindexObjectFiles()
    {
        _fileOfObject.Clear();
        foreach (var (path, objects) in _objectsByFile)
            foreach (var item in objects)
                _fileOfObject.TryAdd(item.Key, path);
    }

    /// <summary>
    /// This workspace's committed delta-readiness as a commit token, or null when it has no
    /// baseline to be ready with.
    ///
    /// <para>The mirror image of <see cref="Commit"/>, and deliberately the same type: what a
    /// delta needs to exist is exactly what a compile produces, so persisting it
    /// (<see cref="RadBaselineSidecar"/>) and restoring it are one shape rather than a second
    /// definition of "the baseline" that could drift from this one. A map added to the
    /// workspace and not to <see cref="RadWorkspaceUpdate"/> cannot be committed at all, which
    /// is what keeps the persisted set complete by construction.</para>
    /// </summary>
    internal RadWorkspaceUpdate? Snapshot() => Baseline == null
        ? null
        : new RadWorkspaceUpdate(
            new Dictionary<string, string>(_fileHashes, StringComparer.Ordinal),
            _objectsByFile.ToDictionary(pair => pair.Key, pair => pair.Value.ToList(), StringComparer.Ordinal),
            new Dictionary<string, RadFileDeclarations>(_declarationsByFile, StringComparer.Ordinal),
            _referencesByObject.ToDictionary(pair => pair.Key, pair => new HashSet<RadObjectKey>(pair.Value)),
            _crossAppReferences.ToDictionary(pair => pair.Key, pair => new HashSet<RadAppObjectRef>(pair.Value)),
            new Dictionary<RadObjectKey, RadObjectKey>(_extensionTargets),
            Array.Empty<RadObjectKey>(),
            // Not baseline state: a surface move is an instruction to ONE commit, already
            // published by the compile this snapshot describes. Persisting it would make a
            // cache HIT re-announce a move the consumers acted on processes ago.
            Array.Empty<RadObjectKey>(),
            Baseline,
            Full: true);

    /// <summary>
    /// Adopt a baseline that came off disk rather than out of a compile — the AL-output cache
    /// HIT path, where the module arrives precompiled and there is no compilation to snapshot.
    ///
    /// <para><paramref name="referenceSignature"/> is the signature the persisted baseline was
    /// BUILT under, and adopting it is the load-bearing half. It cannot go through
    /// <see cref="ArmFor"/>, which compares and invalidates; it has to be installed as the
    /// incumbent so that the next cycle's <see cref="ArmFor"/> is the comparison. That
    /// comparison is not optional: the AL-output cache key hashes the module name, the
    /// preprocessor symbols, the resolved dependency ids and the <c>.al</c> contents, but NOT
    /// the app version, publisher or id — so a HIT can legitimately serve a tree whose
    /// <c>app.json</c> identity has moved, and a delta bound under the new identity against
    /// the old baseline is precisely what the signature exists to refuse.</para>
    /// </summary>
    internal void Hydrate(RadWorkspaceUpdate update, string referenceSignature)
    {
        Commit(update);
        ReferenceSignature = referenceSignature;
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

    /// <summary>
    /// How one app is named across workspaces: its <c>app.json</c> id when it has one, and its
    /// module name otherwise. The second case is real — a bundle's suites without an
    /// <c>app.json</c> are merged into a single <c>AppGroup</c> carrying a null <c>AppId</c> —
    /// and it is the reason the identity is a string rather than a Guid. The COMPILATION for
    /// such a group is not id-less: it is given <c>DeterministicGuid(moduleName)</c>. So a
    /// reference graph that mapped the compiler's Guid straight to a workspace would silently
    /// never match one, and every edge into it would be dropped. See
    /// <c>RadAppCohort</c>, which owns that translation.
    /// </summary>
    public static string IdentityOf(Guid? appId, string moduleName) =>
        appId?.ToString("N") ?? "name:" + moduleName;

    public static RadWorkspace For(string moduleName, Guid? appId, string sourceRoot, string? bundleRoot = null)
    {
        var identity = IdentityOf(appId, moduleName);
        var key = identity + "|" + Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar);
        return _byKey.GetOrAdd(key, _ => new RadWorkspace(moduleName, sourceRoot, identity, bundleRoot));
    }

    /// <summary>
    /// The workspaces of one bundle. Scoped by the bundle root that each was created under,
    /// because <see cref="_byKey"/> is process-wide and never cleared and its key admits the
    /// same app id at two different source roots — so an identity lookup across the whole store
    /// can hand one checkout's consumer a producer belonging to another.
    /// </summary>
    internal static IReadOnlyList<RadWorkspace> InBundle(string bundleRoot)
    {
        var root = Path.GetFullPath(bundleRoot).TrimEnd(Path.DirectorySeparatorChar);
        return _byKey.Values
            .Where(ws => string.Equals(ws.BundleRoot, root, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>What one app must re-bind because a SIBLING app's callable surface moved.</summary>
    /// <param name="Producer">The app that moved.</param>
    /// <param name="Everything">
    /// The producer rebuilt in full, so every consumer of it rebinds rather than the users of
    /// named keys — see <see cref="RadWorkspace.PublishSurfaceMoves"/>.
    /// </param>
    /// <param name="Users">Objects of the CONSUMING app that have to be re-emitted.</param>
    internal readonly record struct CrossAppRebind(
        RadWorkspace Producer, bool Everything, IReadOnlyList<RadObjectKey> Users);

    /// <summary>
    /// Everything <paramref name="consumer"/> has to rebind because another app in its bundle
    /// published a surface move it has not consumed yet.
    ///
    /// <para>Precision is the whole point: only the consumer's own objects that hold an edge
    /// onto a MOVED surface are returned. A body-only edit in the producer publishes nothing,
    /// so this is empty and the consumer takes the unchanged path exactly as before.</para>
    /// </summary>
    internal static IReadOnlyList<CrossAppRebind> PendingCrossAppRebinds(RadWorkspace consumer)
    {
        var producers = consumer.CrossAppProducers();
        if (producers.Count == 0) return Array.Empty<CrossAppRebind>();

        var pending = new List<CrossAppRebind>();
        foreach (var producer in InBundle(consumer.BundleRoot))
        {
            if (ReferenceEquals(producer, consumer)) continue;
            if (!producers.Contains(producer.Identity)) continue;
            var (everything, keys) = producer.MovesSince(consumer.WatermarkFor(producer.Identity));
            if (!everything && keys.Count == 0) continue;
            var users = consumer.CrossAppUsersOf(producer.Identity, everything ? null : keys);
            if (users.Count > 0) pending.Add(new CrossAppRebind(producer, everything, users));
        }
        return pending;
    }

    /// <summary>
    /// Mark <paramref name="consumer"/> as bound against every sibling's current publish
    /// generation. Called from <c>RadEmitResult.Commit</c> — that is, only once the consumer's
    /// generation has actually loaded — so a rejected C# candidate leaves the watermark where
    /// it was and the next cycle re-widens instead of dropping the rebind.
    ///
    /// <para>Every sibling, not only the ones this app has edges into: an edit can ADD an edge
    /// to an app this one never referenced before, and starting that edge from a zero watermark
    /// would replay the sibling's whole publish history as pending work.</para>
    /// </summary>
    internal static void RecordConsumedGenerations(RadWorkspace consumer)
    {
        foreach (var producer in InBundle(consumer.BundleRoot))
            if (!ReferenceEquals(producer, consumer))
                consumer.RecordConsumed(producer.Identity, producer.PublishGeneration);
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
        var movedManifests = Names(ChangedManifests(inBundle));
        var unownedAl = Names(inBundle.Where(path =>
            string.Equals(Path.GetExtension(path), ".al", StringComparison.OrdinalIgnoreCase)
            && !workspaces.Any(ws => IsWithin(path, ws.SourceRoot))));
        if (movedManifests.Count > 0)
            blockers.Add($"{List(movedManifests)} changed — a manifest edit moves what the whole "
                + "module binds against, so warm metadata cannot be kept");
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

    /// <summary>
    /// Content hash per <c>app.json</c>, as of the last cycle that compiled against it — so a
    /// manifest WRITE can be told apart from a manifest CHANGE. Written by
    /// <see cref="RecordManifestState"/> and read by <see cref="PrepareBundleReload"/>, both on
    /// the single cycle thread.
    /// </summary>
    private static readonly Dictionary<string, string> _manifestHashes = new(StringComparer.Ordinal);

    /// <summary>
    /// Seed the recorded content of manifests this store has never seen, so the first cycle of
    /// a session gives later ones something to compare against. Called with the exact set the
    /// cycle resolved its dependencies from — an O(apps) list of small files the cycle has
    /// already enumerated, never a tree walk.
    ///
    /// <para>Seed-only, deliberately. A manifest a cycle actually examined was recorded by
    /// <see cref="ChangedManifests"/> from the very bytes it compared; overwriting that here
    /// from a second read later in the same cycle is exactly the window in which a write could
    /// land and be recorded as "already compiled against" — while the compile that follows binds
    /// to it, <see cref="ArmFor"/> invalidates, and the full rebuild runs with emit captures it
    /// was told it could keep and no object map left to sweep them with.</para>
    /// </summary>
    public static void RecordManifestState(IEnumerable<string> manifestPaths)
    {
        foreach (var path in manifestPaths)
        {
            var full = Path.GetFullPath(path);
            if (!_manifestHashes.ContainsKey(full)) _manifestHashes[full] = HashManifest(full);
        }
    }

    /// <summary>
    /// The manifests among <paramref name="inBundle"/> whose CONTENT moved since the last cycle.
    ///
    /// <para>Two things this deliberately does NOT do. It does not treat a non-<c>.al</c> path
    /// as a blocker on the strength of its extension: the only non-AL file a watch session can
    /// see is <c>app.json</c> (<c>WatchSource.ArmSourceWatch</c> filters the watcher to
    /// <c>*.al</c> and <c>app.json</c> and re-checks each event), and nothing else a source tree
    /// contains changes what the compiler binds against. And it does not treat a WRITE to
    /// <c>app.json</c> as a change: a branch switch, a checkout, an editor autosave or a
    /// formatter rewrites the file byte-identically, and every one of those used to cost a
    /// whole-module compile of the whole bundle — the most expensive cycle there is, bought with
    /// no edit at all.</para>
    ///
    /// <para>Content is the right question because content is what the OTHER half of this reads:
    /// <see cref="ArmFor"/> invalidates when the reference signature (dependencies, identity,
    /// preprocessor symbols) moves, and a manifest is the only thing in the tree that feeds it.
    /// An identical manifest therefore cannot make a warm app rebuild. The converse does not
    /// hold exactly — a changed <c>description</c> moves no signature — and that direction is
    /// deliberately left conservative: this decides whether emit captures may survive a rebuild
    /// that has no object map to sweep with, so an unnecessary refresh costs a cycle while a
    /// missed one leaves stale metadata behind.</para>
    ///
    /// <para>A manifest with no recorded content is reported as changed. That is the honest
    /// answer rather than a guess — it means no cycle has compiled against it, which is either
    /// the first cycle of the session (already blocked, and for a better reason) or an app
    /// appearing in the bundle mid-session.</para>
    /// </summary>
    private static List<string> ChangedManifests(IReadOnlyList<string> inBundle)
    {
        var moved = new List<string>();
        foreach (var path in inBundle)
        {
            if (!string.Equals(Path.GetFileName(path), "app.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var full = Path.GetFullPath(path);
            var hash = HashManifest(full);
            // Recorded from the SAME read the decision is made from — see RecordManifestState
            // for the window that re-reading later in the cycle would open.
            var seen = _manifestHashes.TryGetValue(full, out var previous);
            _manifestHashes[full] = hash;
            if (!seen || !string.Equals(previous, hash, StringComparison.Ordinal)) moved.Add(full);
        }
        return moved;
    }

    /// <summary>
    /// A manifest's content hash, or a sentinel for one that is absent or unreadable — both of
    /// which are states a compile reacts to, so they must be distinguishable from each other
    /// and from any real content.
    /// </summary>
    private static string HashManifest(string path)
    {
        try
        {
            if (!File.Exists(path)) return "<absent>";
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
        }
        catch (IOException)
        {
            // Mid-write, by a checkout or an editor. Unreadable is not "unchanged": returning a
            // fresh sentinel each time would thrash, so this reads as one distinct state that
            // differs from any real content and settles as soon as the file is readable again.
            return "<unreadable>";
        }
        catch (UnauthorizedAccessException)
        {
            return "<unreadable>";
        }
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
