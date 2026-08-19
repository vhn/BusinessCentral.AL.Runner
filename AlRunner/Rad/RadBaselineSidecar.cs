// RadBaselineSidecar — makes an AL-output cache HIT arrive delta-ready.
//
// The problem
// -----------
// A `--watch` cache HIT loads `<key>.dll` and skips Emit+Compile entirely, so the resident
// RadWorkspace has no compiler symbol baseline and cannot serve a delta. The developer's
// FIRST edit therefore paid one whole-module compile just to establish one — 761–862 s on a
// 7,000-file app, at exactly the moment they are blocked waiting for a result. A fast start
// bought at the price of a slow first edit is the wrong trade: under `--watch` the developer
// is going to edit.
//
// Why this is possible at all
// ---------------------------
// The baseline already IS a serializable module definition. Microsoft's own
// `CompilationUtilities.WriteSymbolReference` merges each delta back into the previous
// `ModuleDefinition` to produce the next one — the same mechanism that puts
// SymbolReference.json inside a `.app` — and BcCompiler.MergeRadBaseline already reads the
// result back with `SymbolReferenceJsonReader.ReadModule`. So from the first delta onward the
// live baseline is itself a JSON-reconstituted definition. Persisting one is not a new
// capability; it is the existing round trip pointed at a file instead of a MemoryStream.
//
// Why the ModuleDefinition alone is not enough
// --------------------------------------------
// A delta reads six things off the workspace, and three of them are not in the module
// definition at all. Two of those three fail SILENTLY when missing — the delta reports
// success having done the wrong thing:
//
//   _referencesByObject  a moved callable surface rebinds NOBODY, and its callers keep
//                        executing against the previous contract (generated calls bake
//                        Microsoft's member ids)
//   _extensionTargets    a renamed or retargeted enumextension leaves its old registration
//                        behind, so the merged enum carries values from both
//
// and the rest fail loudly but expensively: no _fileHashes means the whole tree reads as
// changed, no _objectsByFile means every object reads as an addition, no _declarationsByFile
// means deleting a `dotnet` package declaration passes for a comment-only edit.
//
// So what is persisted is the whole commit token (RadWorkspace.Snapshot) plus the reference
// signature it was built under — not "the baseline". Keeping it that shape is deliberate: a
// map added to the workspace but not to RadWorkspaceUpdate cannot be committed at all, so the
// persisted set cannot silently fall behind the set a delta reads.
//
// Two artifacts, both optional for a HIT
// --------------------------------------
//   <key>.rad-symbols.json   the ModuleDefinition, in BC's own serialized form
//   <key>.rad-baseline.json  this envelope: schema, module, signature and the four maps
//
// Neither is in AlCacheSidecars.IsCompleteEntry. Their absence is not a broken cache entry,
// it is an older or one-shot-written one: the HIT still serves correct results and simply
// cannot delta until the first edit has built a baseline (which then writes these). See the
// comment on the suffix constants for why gating a HIT on them would be actively harmful.
using System.Text.Json;
using System.Text.Json.Serialization;
using NavSymRef = Microsoft.Dynamics.Nav.CodeAnalysis.SymbolReference;

namespace AlRunner.Rad;

/// <summary>
/// Persists and restores one app's RAD delta-readiness beside its cached AL output, so a
/// cache HIT can serve a delta on the first edit instead of rebuilding the module.
/// </summary>
public static class RadBaselineSidecar
{
    /// <summary>
    /// Envelope format version. Bumped whenever the shape below changes.
    ///
    /// <para>This is why the AL-output cache key does NOT need bumping for this sidecar: the
    /// key stays stable, an envelope of an unrecognised version is simply refused here, and
    /// the cycle falls back to building a baseline the old way. Bumping the cache key instead
    /// would discard every existing DLL — including all of CI's — to withhold an
    /// optimisation.</para>
    ///
    /// <para><b>2</b> adds <see cref="EnvelopeDto.CrossAppReferences"/>. The bump is not
    /// cosmetic: <c>System.Text.Json</c> ignores members it does not find, so a schema-2 reader
    /// handed a schema-1 envelope would deserialize it happily and get ZERO cross-app edges —
    /// a hydrated workspace that silently rebinds no sibling caller, which is the exact bug
    /// those edges exist to fix. Accepting it would cost correctness and state nothing.</para>
    ///
    /// <para><b>What the refusal costs — measured, not assumed.</b> Two separate questions, and
    /// the second one has a much larger answer than "one full compile per app, once".</para>
    ///
    /// <para><i>How often it fires: through an ordinary upgrade, never.</i> The envelope lives at
    /// <c>&lt;cacheDir&gt;/&lt;cacheKey&gt;.rad-baseline.json</c>, and <c>ComputeAlCacheKey</c>'s
    /// second line is <c>runner:&lt;sha256 of al-runner.dll&gt;</c>
    /// (<c>RunnerFingerprint.WriteKeyLines</c>). Changing this constant changes that DLL, so the
    /// runner that reads schema 2 computes a DIFFERENT key from the one that wrote schema 1 and
    /// never opens the old envelope — the <c>.dll</c> beside it MISSes too, and the full compile
    /// that follows writes a fresh schema-2 pair. The refusal below is therefore a guard for a
    /// cache directory that is shared or hand-modified, not an upgrade path, and the bump's cost
    /// on an upgrade is zero over what the binary change already costs.</para>
    ///
    /// <para><i>What it costs when it does fire: a whole-bundle compile, once per watch session,
    /// indefinitely.</i> Measured on the three-app <c>DeltaTwoApp</c> fixture by rewriting one
    /// app's envelope as a genuine schema 1 and starting <c>--watch</c>:</para>
    /// <code>
    /// [watch] Delta Lib: full rebuild — 1 app(s) in the bundle have no baseline
    /// [watch] Delta Lib Tests: full rebuild — 1 app(s) in the bundle have no baseline
    /// [watch] Delta Bridge: full compile — the cached module's delta baseline could not be
    ///         used (its envelope is schema 1, not 2), so this compile establishes a new one
    /// </code>
    /// <para>Not one app: <c>RadWorkspaceStore.PrepareBundleReload</c> refuses warm metadata for
    /// the whole bundle while ANY app in it lacks a baseline, and invalidates every workspace —
    /// so one refused envelope turned a 2-object delta into all 9 objects of the bundle. And it
    /// does not heal: the write site is guarded on the sidecar paths, which
    /// <c>Program.cs</c> only assigns while <c>radWs is null or { Generations.Count: 0 }</c>. A
    /// cache HIT loads a generation on the spot, so on the cycle that pays the full compile
    /// those paths are null and <c>TrySave</c> never runs — the schema-1 envelope is still on
    /// disk afterwards, and the next watch session over the same tree pays the same bundle-wide
    /// compile again.</para>
    /// </summary>
    internal const int Schema = 2;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    // ── on-disk shape ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A <see cref="RadObjectKey"/> as stored: the triple verbatim, not a display name to be
    /// re-derived. <see cref="RadObjectKey.For"/>'s "name only when there is no id" rule is
    /// applied when a key is BUILT from a symbol; re-applying it on read would make the
    /// restored keys depend on that rule not changing, and a key that hashes differently from
    /// the one the baseline was written with silently matches nothing.
    /// </summary>
    private sealed class KeyDto
    {
        public string Kind { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public static KeyDto From(RadObjectKey key) =>
            new() { Kind = key.Kind, Id = key.Id, Name = key.Name };

        public RadObjectKey ToKey() => new(Kind, Id, Name);
    }

    private sealed class ObjectDto
    {
        public KeyDto Key { get; set; } = new();
        /// <summary>Display spelling — what Microsoft's change model and the log lines get.</summary>
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;

        public static ObjectDto From(RadObjectRef item) => new()
        {
            Key = KeyDto.From(item.Key), Name = item.Name, Namespace = item.Namespace,
        };

        public RadObjectRef ToRef() => new(Key.ToKey(), Name, Namespace);
    }

    private sealed class FileDto
    {
        /// <summary>Relative to the file set's common root, with <c>/</c> separators.</summary>
        public string Path { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public bool DotNetPackage { get; set; }
        public bool Unrecorded { get; set; }
        public ObjectDto[] Objects { get; set; } = Array.Empty<ObjectDto>();
    }

    private sealed class EdgeDto
    {
        public KeyDto From { get; set; } = new();
        public KeyDto[] To { get; set; } = Array.Empty<KeyDto>();
    }

    /// <summary>
    /// A reference into another app of the same bundle: the sibling's workspace identity plus
    /// the object's own key. The identity is stored, not derived, for the same reason
    /// <see cref="KeyDto"/> stores the triple verbatim — it is produced by one rule
    /// (<c>RadWorkspaceStore.IdentityOf</c>) whose output must not be re-derived on read.
    /// </summary>
    private sealed class AppKeyDto
    {
        public string App { get; set; } = string.Empty;
        public KeyDto Key { get; set; } = new();

        public static AppKeyDto From(RadAppObjectRef item) =>
            new() { App = item.App, Key = KeyDto.From(item.Key) };

        public RadAppObjectRef ToRef() => new(App, Key.ToKey());
    }

    private sealed class CrossAppEdgeDto
    {
        public KeyDto From { get; set; } = new();
        public AppKeyDto[] To { get; set; } = Array.Empty<AppKeyDto>();
    }

    private sealed class ExtensionDto
    {
        public KeyDto Extension { get; set; } = new();
        public KeyDto Target { get; set; } = new();
    }

    private sealed class EnvelopeDto
    {
        // First, so a reader (and the schema-rejection test) can find it without parsing the
        // whole file — on a 7,000-object app the maps below run to tens of megabytes.
        public int Schema { get; set; }
        public string Module { get; set; } = string.Empty;
        /// <summary>The reference signature the baseline was built under — see RadWorkspace.Hydrate.</summary>
        public string Signature { get; set; } = string.Empty;
        /// <summary>The common root the paths are relative TO, for diagnostics only.</summary>
        public string Root { get; set; } = string.Empty;
        public FileDto[] Files { get; set; } = Array.Empty<FileDto>();
        public EdgeDto[] References { get; set; } = Array.Empty<EdgeDto>();
        /// <summary>Edges into a SIBLING app of the same bundle — schema 2 onwards.</summary>
        public CrossAppEdgeDto[] CrossAppReferences { get; set; } = Array.Empty<CrossAppEdgeDto>();
        public ExtensionDto[] Extensions { get; set; } = Array.Empty<ExtensionDto>();
    }

    // ── write ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Persist <paramref name="ws"/>'s delta-readiness. Returns false — writing nothing — when
    /// there is none to persist, so a cache entry never carries an envelope that could only
    /// ever fail hydration.
    /// </summary>
    public static bool TrySave(RadWorkspace ws, string envelopePath, string symbolsPath) =>
        ws.Snapshot() is { } state
        && ws.ReferenceSignature is { } signature
        && TrySave(ws.ModuleName, state, signature, envelopePath, symbolsPath);

    /// <summary>
    /// The same write, for a mode that has no <see cref="RadWorkspace"/> to snapshot.
    ///
    /// <para>One-shot and <c>--server</c> runs never build a delta workspace — they compile
    /// once and exit — but they DO write the AL-output cache entry a later <c>--watch</c> will
    /// hit. Without this overload only a previous watch could leave a baseline behind, so
    /// one-shot-then-watch (the ordinary way a developer arrives at watch) paid a whole-module
    /// compile on the very first edit. They build the state with
    /// <c>BcCompiler.TryBuildBaselineSnapshot</c> — the same method the watch full-compile path
    /// uses — so a baseline written by one mode is indistinguishable from another's.</para>
    /// </summary>
    internal static bool TrySave(
        string moduleName,
        RadWorkspaceUpdate state,
        string signature,
        string envelopePath,
        string symbolsPath)
    {
        if (state.Baseline is not NavSymRef.ModuleDefinition module) return false;
        if (signature.Length == 0) return false;
        var root = CommonRoot(state.FileHashes.Keys);
        if (root == null) return false;

        try
        {
            var envelope = new EnvelopeDto
            {
                Schema = Schema,
                Module = moduleName,
                Signature = signature,
                Root = root,
                Files = state.FileHashes
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair =>
                    {
                        var declarations = state.DeclarationsByFile.TryGetValue(pair.Key, out var d)
                            ? d : default;
                        return new FileDto
                        {
                            Path = Relative(root, pair.Key),
                            Hash = pair.Value,
                            DotNetPackage = declarations.DotNetPackage,
                            Unrecorded = declarations.Unrecorded,
                            Objects = state.ObjectsByFile.TryGetValue(pair.Key, out var objects)
                                ? objects.Select(ObjectDto.From).ToArray()
                                : Array.Empty<ObjectDto>(),
                        };
                    })
                    .ToArray(),
                References = state.ReferencesByObject
                    .Where(pair => pair.Value.Count > 0)
                    .Select(pair => new EdgeDto
                    {
                        From = KeyDto.From(pair.Key),
                        To = pair.Value.Select(KeyDto.From).ToArray(),
                    })
                    .ToArray(),
                CrossAppReferences = state.CrossAppReferencesByObject
                    .Where(pair => pair.Value.Count > 0)
                    .Select(pair => new CrossAppEdgeDto
                    {
                        From = KeyDto.From(pair.Key),
                        To = pair.Value.Select(AppKeyDto.From).ToArray(),
                    })
                    .ToArray(),
                Extensions = state.ExtensionTargets
                    .Select(pair => new ExtensionDto
                    {
                        Extension = KeyDto.From(pair.Key), Target = KeyDto.From(pair.Value),
                    })
                    .ToArray(),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(envelopePath))!);
            // Symbols first, envelope last: hydration requires both and checks the envelope
            // first, so the envelope becoming visible is the commit point. Same discipline as
            // the enum/query sidecars, for the same reason — a concurrent reader must never
            // see an envelope whose symbols are not there yet.
            AlRunner.Infrastructure.AlCacheWriter.AtomicPublish(symbolsPath, tmp =>
            {
                using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
                NavSymRef.SymbolReferenceJsonWriter.WriteModule(fs, module);
            });
            AlRunner.Infrastructure.AlCacheWriter.AtomicPublish(envelopePath, tmp =>
            {
                using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(fs, envelope, _json);
            });
            return true;
        }
        catch (Exception ex)
        {
            // Never fail a cycle over an optimisation that did not get written. The entry
            // simply has no envelope and the next watch start behaves as it did before.
            Console.Error.WriteLine(
                $"  [cache] rad baseline not persisted for {moduleName} " +
                $"({ex.GetType().Name}: {ex.Message.Split('\n')[0]})");
            return false;
        }
    }

    // ── read ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Restore <paramref name="ws"/> from a previously saved pair so it can serve a delta
    /// immediately. Returns false — leaving the workspace untouched — when the pair is absent,
    /// unreadable, of another schema, for another module, or does not describe the tree now on
    /// disk. In every one of those cases the cycle behaves exactly as it did before this
    /// existed: the first edit builds the baseline.
    /// </summary>
    /// <param name="alFolders">
    /// The same source folders the compile is given. Hydration is validated against the tree
    /// they actually contain, not against the cache key: the key proves the CONTENT matched
    /// when the entry was written, and a cache directory is shared and long-lived, so
    /// "this envelope describes this tree" is checked rather than assumed.
    /// </param>
    public static bool TryHydrate(
        RadWorkspace ws, IReadOnlyList<string> alFolders, string envelopePath, string symbolsPath)
    {
        // Never overwrite live compiler state with something off disk.
        if (ws.HasBaseline) return false;

        if (!File.Exists(envelopePath) || !File.Exists(symbolsPath))
        {
            // The ordinary case for a cache entry written by a one-shot run (all of CI's) —
            // no warning, but parked so the full compile the first edit pays for can say what
            // it is doing rather than looking like an unexplained stall.
            ws.PendingFullCompileReason =
                "the cached module carried no delta baseline — its cache entry was written by a " +
                "run that did not build one — so this compile establishes one";
            return false;
        }

        try
        {
            EnvelopeDto? envelope;
            using (var fs = File.OpenRead(envelopePath))
                envelope = JsonSerializer.Deserialize<EnvelopeDto>(fs, _json);
            if (envelope == null) return Reject(ws, "its envelope is empty");
            if (envelope.Schema != Schema)
                return Reject(ws, $"its envelope is schema {envelope.Schema}, not {Schema}");
            if (!string.Equals(envelope.Module, ws.ModuleName, StringComparison.Ordinal))
                return Reject(ws, $"it was written for '{envelope.Module}', not '{ws.ModuleName}'");
            if (envelope.Signature.Length == 0)
                return Reject(ws, "it carries no reference signature");

            // Does it describe the tree that is actually here? Compared by content, file by
            // file: a stale entry, a partially checked-out tree, a cache shared between two
            // checkouts whose layouts differ — all land here, and all must fail closed. A
            // hydrated baseline that disagrees with the source on disk would let a delta bind
            // against symbols for code that is not there, which is the one outcome worse than
            // being slow (see .claude/rules/loud-failures.md).
            var alFiles = RadWorkspace.EnumerateAlFiles(alFolders);
            var currentRoot = CommonRoot(alFiles);
            if (currentRoot == null) return Reject(ws, "this tree has no common source root");
            var hashes = RadWorkspace.HashSourceTree(alFiles);
            var byRelative = hashes.ToDictionary(
                pair => Relative(currentRoot, pair.Key), pair => pair.Key, StringComparer.Ordinal);

            if (byRelative.Count != envelope.Files.Length)
                return Reject(ws,
                    $"it describes {envelope.Files.Length} file(s) and this tree has {byRelative.Count}");
            foreach (var file in envelope.Files)
            {
                if (!byRelative.TryGetValue(file.Path, out var absolute))
                    return Reject(ws, $"this tree has no {file.Path}");
                if (!string.Equals(hashes[absolute], file.Hash, StringComparison.Ordinal))
                    return Reject(ws, $"{file.Path} has changed since it was written");
            }

            NavSymRef.ModuleDefinition module;
            using (var fs = File.OpenRead(symbolsPath))
                module = NavSymRef.SymbolReferenceJsonReader.ReadModule(fs);

            // Rebuilt against THIS tree's absolute paths — the envelope's relative ones exist
            // only so the same entry can serve a different checkout of the same sources.
            var objectsByFile = new Dictionary<string, List<RadObjectRef>>(StringComparer.Ordinal);
            var declarationsByFile = new Dictionary<string, RadFileDeclarations>(StringComparer.Ordinal);
            foreach (var file in envelope.Files)
            {
                var absolute = byRelative[file.Path];
                if (file.Objects.Length > 0)
                    objectsByFile[absolute] = file.Objects.Select(item => item.ToRef()).ToList();
                var declarations = new RadFileDeclarations(file.DotNetPackage, file.Unrecorded);
                if (declarations != default) declarationsByFile[absolute] = declarations;
            }

            ws.Hydrate(
                new RadWorkspaceUpdate(
                    hashes,
                    objectsByFile,
                    declarationsByFile,
                    envelope.References.ToDictionary(
                        edge => edge.From.ToKey(),
                        edge => edge.To.Select(item => item.ToKey()).ToHashSet()),
                    envelope.CrossAppReferences.ToDictionary(
                        edge => edge.From.ToKey(),
                        edge => edge.To.Select(item => item.ToRef()).ToHashSet()),
                    envelope.Extensions.ToDictionary(
                        item => item.Extension.ToKey(), item => item.Target.ToKey()),
                    Array.Empty<RadObjectKey>(),
                    // Hydration is not a compile and must announce nothing: the module it
                    // restores is byte-identical to the one the sidecar describes.
                    Array.Empty<RadObjectKey>(),
                    module,
                    Full: true),
                envelope.Signature);

            Console.Error.WriteLine(
                $"  [cache] rad baseline hydrated for {ws.ModuleName} — " +
                $"{objectsByFile.Values.Sum(list => list.Count)} object(s) over " +
                $"{envelope.Files.Length} file(s), so the first edit is a delta");
            return true;
        }
        catch (Exception ex)
        {
            return Reject(ws, $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    /// <summary>
    /// Refuse a persisted baseline, and park WHY on the workspace so the full compile the next
    /// edit pays for explains itself. The reason is parked rather than only logged because the
    /// interactive dashboard silences both console streams while the bundle loop runs, and
    /// because the cycle that discovers this is not the cycle the developer watches rebuilding.
    /// </summary>
    private static bool Reject(RadWorkspace ws, string reason)
    {
        var message =
            $"the cached module's delta baseline could not be used ({reason}), " +
            "so this compile establishes a new one";
        Console.Error.WriteLine($"  [cache] {ws.ModuleName}: {message}");
        ws.PendingFullCompileReason = message;
        return false;
    }

    // ── paths ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The deepest directory containing every file in the set. Paths are stored relative to it
    /// because a bundle's sources are not all under one app directory — <c>CollectSuitePaths</c>
    /// can add <c>&lt;bucketRoot&gt;/_shared</c>, which is a sibling of the app, so relative-to-
    /// the-app-root cannot express them.
    ///
    /// <para>Deliberately independent of <c>Program.ComputeAlCacheKey</c>'s own common-root
    /// walk, and not shared with it: this one only needs write/read self-consistency (the same
    /// function over the same tree at both ends), never agreement with how the key is framed.
    /// Coupling them would make the cache key's framing a compatibility surface for every
    /// persisted envelope.</para>
    ///
    /// <para>Null when there is no such directory — an empty set, or files on two Windows
    /// volumes. Both simply mean this entry is not persistable.</para>
    /// </summary>
    private static string? CommonRoot(IEnumerable<string> files)
    {
        string? common = null;
        foreach (var file in files)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(file));
            if (directory == null) return null;
            if (common == null) { common = directory; continue; }
            while (!IsSameOrUnder(directory, common))
            {
                common = Path.GetDirectoryName(common);
                if (common == null) return null;
            }
        }
        return common;
    }

    private static bool IsSameOrUnder(string directory, string root) =>
        string.Equals(directory, root, StringComparison.OrdinalIgnoreCase)
        || directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // '/' so the stored form does not carry the writing platform's separator. Two platforms
    // sharing one cache directory is not a supported case, but a path that differs only by
    // separator would be REJECTED rather than mis-resolved, which is the safe direction.
    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, Path.GetFullPath(file)).Replace(Path.DirectorySeparatorChar, '/');
}
