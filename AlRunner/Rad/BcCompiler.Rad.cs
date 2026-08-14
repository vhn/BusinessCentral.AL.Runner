// BcCompiler — RAD delta compilation.
//
// `Compilation.CreateForRad` is the same public factory Microsoft's own RAD ("Rapid
// Application Development" — what F5 in the VS Code AL extension drives) uses. Given a
// change model and the previously-built module's symbol picture, it binds and generates
// C# for ONLY the changed objects, resolving everything else from the baseline symbols
// instead of re-parsing and re-binding it. Measured on this codebase's probe harness:
// a one-object change in a four-object app produced exactly one
// ModuleOutputter.AddApplicationObject callback, not four.
//
// The changed objects MUST be stripped from `packagedModuleDefinition`. Left in, the
// stale baseline symbol shadows the new source and a changed object calling another
// changed object fails to bind (AL0126). See ModuleDefinitionOps.
using System.Collections.Immutable;
using NavCA = Microsoft.Dynamics.Nav.CodeAnalysis;
using NavSyntax = Microsoft.Dynamics.Nav.CodeAnalysis.Syntax;
using NavDiag = Microsoft.Dynamics.Nav.CodeAnalysis.Diagnostics;
using NavSymRef = Microsoft.Dynamics.Nav.CodeAnalysis.SymbolReference;

namespace AlRunner;

/// <summary>
/// Outcome of one incremental compile.
/// </summary>
/// <param name="Emit">Sources the compile produced — the delta only, when <paramref name="FullRebuild"/> is false.</param>
/// <param name="FullRebuild">True when the whole module was (re)compiled from source.</param>
/// <param name="NoChange">True when nothing in the source tree moved and no compile ran.</param>
public sealed record RadEmitResult(
    BcEmitOutput Emit,
    bool FullRebuild,
    bool NoChange)
{
    private int _committed;

    internal AlRunner.Rad.RadWorkspaceUpdate? WorkspaceUpdate { get; init; }
    internal bool CanCommit => WorkspaceUpdate != null;
    public RadChangeSet Changes { get; init; } = RadChangeSet.Empty;

    /// <summary>
    /// Runtime metadata this generation's AL emit produced, held back until the
    /// generation loads. Null for a full compile, which writes through immediately.
    /// </summary>
    internal AlRunner.Rad.RadMetadataCapture? Metadata { get; init; }

    /// <summary>
    /// Make a successful AL emit current only after its C# assembly has compiled and
    /// loaded. <paramref name="assembly"/> is null only for a deletion-only delta.
    /// </summary>
    public void Commit(AlRunner.Rad.RadWorkspace workspace, System.Reflection.Assembly? assembly)
    {
        if (NoChange) return;
        if (WorkspaceUpdate == null)
            throw new InvalidOperationException("This RAD result has no committable workspace update.");
        if (Interlocked.Exchange(ref _committed, 1) != 0)
            throw new InvalidOperationException("This RAD result was already committed.");

        if (FullRebuild)
        {
            if (assembly == null)
                throw new InvalidOperationException("A full RAD generation requires a loaded assembly.");
            AlRunner.Rad.AlObjectResolution.RegisterGeneration(workspace, assembly);
            workspace.Generations.Clear();
            workspace.Generations.Add(assembly);

            // A warm watch cycle keeps the metadata registries across reloads, so a full
            // compile is not a clean slate. Its re-emit has already overwritten every
            // entry keyed by an id that still exists; what it cannot say anything about is
            // an identity that is GONE. Two shapes of that, both resolved here while the
            // workspace still remembers what the app used to declare:
            var declared = WorkspaceUpdate.ObjectsByFile.Values
                .SelectMany(objects => objects)
                .ToDictionary(item => item.Key);
            foreach (var previous in workspace.AllObjects())
            {
                //  1. the object was deleted — nothing re-registered it.
                if (!declared.TryGetValue(previous.Key, out var current))
                {
                    AlRunner.Rad.RadMetadataCapture.Drop(workspace, previous);
                    continue;
                }
                //  2. it survives, but an enumextension registers under (base enum id, its
                //     own name) rather than under its own id — so a rename or a change of
                //     target adds a second registration instead of replacing the first,
                //     and the merged enum would carry values from both.
                if (previous.Key.Kind == "EnumExtension"
                    && workspace.TryGetExtensionTarget(previous.Key, out var wasTarget)
                    && (!WorkspaceUpdate.ExtensionTargets.TryGetValue(previous.Key, out var target)
                        || wasTarget != target
                        || !string.Equals(previous.Name, current.Name, StringComparison.Ordinal)))
                    AlRunner.Rad.RadMetadataCapture.Drop(workspace, previous);
            }
        }
        else
        {
            if (Emit.Sources.Count > 0 && assembly == null)
                throw new InvalidOperationException("A non-empty RAD delta requires a loaded assembly.");
            AlRunner.Rad.AlObjectResolution.RegisterDelta(
                workspace,
                assembly,
                Changes.Removed.Select(item => item.Key.ClrTypeName).OfType<string>());
            if (assembly != null) workspace.Generations.Add(assembly);
        }

        // Before workspace.Commit: dropping the previous identity of a renamed
        // enumextension or a removed report needs the object map as it was.
        Metadata?.Apply(workspace, Changes);
        workspace.Commit(WorkspaceUpdate);
    }
}

public sealed record RadChangeSet(
    IReadOnlyList<AlRunner.Rad.RadObjectRef> Added,
    IReadOnlyList<AlRunner.Rad.RadObjectRef> Modified,
    IReadOnlyList<AlRunner.Rad.RadObjectRef> Removed)
{
    public static RadChangeSet Empty { get; } = new(
        Array.Empty<AlRunner.Rad.RadObjectRef>(),
        Array.Empty<AlRunner.Rad.RadObjectRef>(),
        Array.Empty<AlRunner.Rad.RadObjectRef>());
}

public sealed partial class BcCompiler
{
    /// <summary>
    /// Compile <paramref name="alFolders"/> into <paramref name="ws"/>, doing as little
    /// work as the change since the last call allows:
    ///
    /// <list type="bullet">
    /// <item>nothing changed → no compile at all;</item>
    /// <item>a body-only edit to existing codeunits → a RAD delta;</item>
    /// <item>anything structural or binding-visible → a full compile.</item>
    /// </list>
    ///
    /// A delta that fails for any reason falls back to a full compile rather than
    /// shipping a half-updated module.
    /// </summary>
    public RadEmitResult EmitIncremental(IEnumerable<string> alFolders, string moduleName, AlRunner.Rad.RadWorkspace ws)
    {
        // BCCOMPILER_TIMING marks: the delta emit itself reports its own duration, but on a
        // 7,000-file app the work AROUND it — enumerating the tree, hashing it, resolving
        // the reference surface — is what a warm cycle actually spends its time on.
        bool timing = Environment.GetEnvironmentVariable("BCCOMPILER_TIMING") == "1";
        var step = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string label)
        {
            if (timing) Console.Error.WriteLine($"[emit-timing] {moduleName}: {label}: {step.ElapsedMilliseconds}ms");
            step.Restart();
        }

        var dirs = alFolders.Where(Directory.Exists).Distinct().ToList();
        if (dirs.Count == 0)
            throw new InvalidOperationException("BcCompiler.EmitIncremental: no source folders");
        var alFiles = dirs
            .SelectMany(d => Directory.EnumerateFiles(d, "*.al", SearchOption.AllDirectories))
            .Distinct()
            .ToList();
        if (alFiles.Count == 0)
            throw new InvalidOperationException(
                $"BcCompiler.EmitIncremental: no .al files under {string.Join(", ", dirs)}");
        Mark($"enumerate {alFiles.Count} files");

        var hashes = AlRunner.Rad.RadWorkspace.HashSourceTree(alFiles);
        Mark("hash source tree");

        // The reference surface has to be established before the delta/full decision:
        // a dependency or preprocessor change invalidates every cached object.
        var bundleAlpackages = dirs
            .SelectMany(d => Directory.EnumerateDirectories(d, ".alpackages", SearchOption.AllDirectories))
            .Distinct()
            .ToList();
        var (refLoader, specs) = GetSharedReferences(bundleAlpackages);
        // The full-compile path emits this same mark: this call is the one that goes cold
        // if the resident dependency loader is ever rebuilt, and a warm watch cycle that
        // quietly pays the ~40s reload looks exactly like a slow compile from outside.
        Mark($"GetSharedReferences ({specs.Length} specs)");
        var signature = ReferenceSignature(moduleName, specs, dirs);
        bool canDelta = ws.ArmFor(signature, previous => DescribeSignatureChange(previous, signature));

        if (!canDelta)
        {
            // A reason an EARLIER cycle knew about. Failing to record a baseline makes every
            // later cycle a full compile, and the cycle that discovers the failure is not the
            // cycle the developer watches rebuilding — so that reason is parked on the workspace
            // and consumed here, by the compile that actually pays for it. Nothing is parked for
            // the invalidation paths (a reference-surface change, the overlay-chain reset, a
            // missing loaded module): those call Invalidate, which reports in the same cycle.
            if (ws.TakePendingFullCompileReason() is { } parked)
                FullCompileBecause(moduleName, parked);
            return FullCompile(alFolders, moduleName, ws, hashes);
        }

        var (changedFiles, removedFiles) = ws.DiffFiles(hashes);
        if (changedFiles.Count == 0 && removedFiles.Count == 0)
            return new RadEmitResult(
                new BcEmitOutput(Array.Empty<EmittedSource>(), Array.Empty<string>(), Array.Empty<string>()),
                FullRebuild: false, NoChange: true);

        try
        {
            var delta = DeltaCompile(moduleName, dirs, ws, hashes, changedFiles, removedFiles, refLoader, specs);
            if (delta != null) return delta;
        }
        catch (Exception ex)
        {
            FullCompileBecause(
                moduleName,
                $"the delta compile threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
        // A fallback is still only a candidate until its generated C# loads. Keep the
        // committed hashes/baseline intact so a backend failure retries the same edit.
        return FullCompile(alFolders, moduleName, ws, hashes);
    }

    /// <summary>
    /// Log and record one decision to compile a whole module instead of deltaing it.
    ///
    /// <para>Both, always. The stderr line is what a redirected/CI run and the RAD watch tests
    /// read; the recorded note is what the interactive dashboard shows, because the bundle loop
    /// silences stderr while it runs (see <see cref="AlRunner.Rad.RadCycleNotes"/>). A fallback
    /// that reaches only one of the two is invisible in the other, which is how "why did that
    /// cycle take four minutes" became unanswerable.</para>
    /// </summary>
    private static void FullCompileBecause(string moduleName, string reason)
    {
        Console.Error.WriteLine($"  [watch] {moduleName}: full compile — {reason}");
        AlRunner.Rad.RadCycleNotes.FullCompile(moduleName, reason);
    }

    private RadEmitResult FullCompile(
        IEnumerable<string> alFolders, string moduleName,
        AlRunner.Rad.RadWorkspace ws, Dictionary<string, string> hashes)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var output = Emit(alFolders, moduleName);
        var comp = LastCompilation;
        if (output.Sources.Count > 0 && output.ExcludedObjects.Count == 0 && comp != null)
        {
            var update = TryBuildBaselineSnapshot(comp, moduleName, hashes, out var failure);
            if (update != null)
            {
                Console.Error.WriteLine(
                    $"  [watch] {moduleName}: baseline built — {output.Sources.Count} object(s) " +
                    $"({sw.ElapsedMilliseconds}ms)");
                return new RadEmitResult(output, FullRebuild: true, NoChange: false)
                {
                    WorkspaceUpdate = update,
                };
            }
            // Losing the baseline costs speed, never correctness: the next cycle just
            // compiles in full again. Say so rather than looking mysteriously slow —
            // this is the one that makes EVERY later cycle slow, not just this one.
            var reason =
                $"the baseline snapshot failed ({failure}), so the next cycle is a full compile too";
            FullCompileBecause(moduleName, reason);
            ws.PendingFullCompileReason = reason;
        }
        else if (output.Sources.Count > 0)
        {
            // An emit-retry exclusion means the module is missing objects — never make that
            // the baseline for future deltas.
            const string reason =
                "an earlier full compile excluded objects, so its result could not become a " +
                "delta baseline and this cycle is a full compile too";
            FullCompileBecause(moduleName, reason);
            ws.PendingFullCompileReason = reason;
        }
        return new RadEmitResult(output, FullRebuild: true, NoChange: false);
    }

    /// <summary>
    /// Everything a completed whole-module compile can hand forward as delta-readiness: the
    /// compiler's symbol picture plus the four maps a later delta reads off it. Returns null
    /// with <paramref name="failure"/> set when the compilation cannot produce one.
    ///
    /// <para>Extracted from <see cref="FullCompile"/> because it has a second caller with no
    /// RAD workspace at all: one-shot and <c>--server</c> runs persist this beside their cached
    /// AL output so a later <c>--watch</c> over the same tree starts delta-ready instead of
    /// paying for a baseline on the developer's first edit. Producing it in one place is what
    /// keeps a baseline written by one mode identical to one written by another.</para>
    ///
    /// <para>Phase-instrumented under <c>BCCOMPILER_TIMING=1</c> because the five phases are not
    /// remotely equal in cost: four read already-computed symbols, while the reference graph asks
    /// Microsoft for a semantic model per tree and calls <c>GetSymbolInfo</c> on every node of
    /// every file. A cost added to every one-shot and CI run has to be attributable.</para>
    /// </summary>
    internal AlRunner.Rad.RadWorkspaceUpdate? TryBuildBaselineSnapshot(
        NavCA.Compilation comp,
        string moduleName,
        Dictionary<string, string> hashes,
        out string? failure)
    {
        bool timing = Environment.GetEnvironmentVariable("BCCOMPILER_TIMING") == "1";
        var step = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string label)
        {
            if (timing)
                Console.Error.WriteLine($"[baseline-timing] {moduleName}: {label}: {step.ElapsedMilliseconds}ms");
            step.Restart();
        }

        try
        {
            var objectsByFile = MapObjectsToFiles(comp);
            Mark("object map");
            var declarations = MapFileDeclarations(comp.SyntaxTrees, objectsByFile);
            Mark("file declarations");
            var references = MapObjectReferences(comp);
            Mark("reference graph");
            var extensionTargets = MapExtensionTargets(comp);
            Mark("extension targets");
            var module = SymbolJsonWriter.BuildModuleDefinition(comp);
            Mark("module definition");

            failure = null;
            return new AlRunner.Rad.RadWorkspaceUpdate(
                hashes,
                objectsByFile,
                declarations,
                references,
                extensionTargets,
                Array.Empty<AlRunner.Rad.RadObjectKey>(),
                module,
                Full: true);
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
            return null;
        }
    }

    private RadEmitResult? DeltaCompile(
        string moduleName,
        List<string> dirs,
        AlRunner.Rad.RadWorkspace ws,
        Dictionary<string, string> hashes,
        List<string> changedFiles,
        List<string> removedFiles,
        NavCA.ISymbolReferenceLoader? refLoader,
        NavCA.SymbolReferenceSpecification[] specs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var parseOpts = ParseOptionsForCompile();
        var compOpts = EmitCompilationOptions();
        var appId = _currentAppId ?? DeterministicGuid(moduleName);
        var publisher = _currentPublisher ?? "AlRunner";
        var version = _currentVersion ?? new Version(1, 0, 0, 0);
        // Bind the delta with the same internalsVisibleTo grants as the full compile;
        // otherwise internal calls and the exported surface comparison can differ from
        // the module dependents compiled against.
        var ivt = CurrentInternalsVisibleTo(dirs);

        var trees = new NavSyntax.SyntaxTree[changedFiles.Count];
        Parallel.For(0, changedFiles.Count, i =>
        {
            var src = File.ReadAllText(changedFiles[i]);
            trees[i] = NavSyntax.SyntaxTree.ParseObjectText(
                src, path: changedFiles[i], encoding: null!, parseOpts, default);
        });

        var parseErrors = trees
            .SelectMany(t => t.GetDiagnostics())
            .Where(d => d.Severity == NavDiag.DiagnosticSeverity.Error)
            .Select(d => $"{d.Location}: error {d.Id}: {d.GetMessage().Split('\n', 2)[0]}")
            .ToList();
        if (parseErrors.Count > 0)
            // A syntax error is the developer's, not the delta path's. Report it exactly
            // as a full compile would, without burning a full rebuild on it — and leave
            // the workspace untouched so the next save re-diffs against the last good state.
            return new RadEmitResult(
                new BcEmitOutput(Array.Empty<EmittedSource>(), parseErrors, Array.Empty<string>()),
                FullRebuild: false, NoChange: false);

        // What do the changed files declare NOW? A declaration-only compilation over just
        // those trees answers exactly that, with BC's own parser rather than a regex over
        // object headers — and it costs O(changed files), not O(module).
        var probe = NavCA.Compilation.Create(
            moduleName: moduleName, publisher: publisher, version: version,
            appId: appId, syntaxTrees: trees, options: compOpts);
        if (refLoader != null)
        {
            probe = probe.WithReferenceLoader(refLoader);
            if (specs.Length > 0) probe = probe.AddReferences(specs);
        }
        probe = probe.WithDotNetResolverFactory(GetOrCreateDotNetFactory());

        var objectsByFile = new Dictionary<string, List<AlRunner.Rad.RadObjectRef>>(StringComparer.Ordinal);
        foreach (var f in changedFiles) objectsByFile[f] = new List<AlRunner.Rad.RadObjectRef>();
        foreach (var f in removedFiles) objectsByFile[f] = new List<AlRunner.Rad.RadObjectRef>();

        var declaredSymbols = probe.GetDeclaredApplicationObjectSymbols().ToList();
        // The id-less kinds the symbol API omits, read off the syntax of the changed files.
        var idlessNow = IdlessDeclarations(trees).ToList();
        // …and what BC's parser says those files declare, which is the only source that can
        // answer "nothing at all" positively. A file with no ObjectSyntax node contributes
        // nothing to the module, so the delta carries it as a hash change and no more.
        var declarationsNow = trees
            .Where(tree => FilePathOf(tree) != null)
            .ToDictionary(
                tree => FilePathOf(tree)!,
                tree => ObjectDeclarations(tree).Select(item => item.Node).ToList(),
                StringComparer.Ordinal);

        // Four ways a changed file cannot be accounted for. Each names itself and the file:
        // the whole point of the fallback is that a delta here would be WRONG, and a developer
        // who cannot see which file caused it has no way to tell that from the delta path
        // being broken.
        var touched = changedFiles.Concat(removedFiles).ToList();
        var unaccounted =
            declaredSymbols.Where(sym => !IsKeyable(sym))
                .Select(sym =>
                    $"{sym.Kind} '{sym.Name}' in {Path.GetFileName(FileOf(sym) ?? "an unknown file")} " +
                    "has no id and no supported name key")
            .Concat(declarationsNow
                .SelectMany(pair => pair.Value.Select(node => (pair.Key, Node: node)))
                .Where(item => !IsRecognisedDeclaration(item.Node))
                .Select(item =>
                    $"{Path.GetFileName(item.Key)} declares {item.Node.Kind}, which this delta " +
                    "does not know how to identify"))
            // A `dotnet` package is recognised and still gets the whole module: the types it
            // publishes are what every object in the app binds against, and a RAD object
            // compilation carries no package declaration trees at all. Both directions —
            // declaring one now, and having declared one before it was edited away or deleted.
            .Concat(touched
                .Where(f => ws.DeclarationsIn(f).DotNetPackage
                    || declarationsNow.TryGetValue(f, out var nodes)
                        && nodes.Any(node => node is NavSyntax.DotNetPackageSyntax))
                .Select(f =>
                    $"{Path.GetFileName(f)} declares a dotnet package, which every object in " +
                    "the module binds against"))
            .Concat(touched
                .Where(f => ws.DeclarationsIn(f).Unrecorded)
                .Select(f =>
                    $"{Path.GetFileName(f)} declared something the last full compile could not " +
                    "identify, so what it used to contribute is not known"))
            // Distinct because a file can earn the same reason twice — a changed file that
            // declares a dotnet package now is usually one that declared one before, too.
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (unaccounted.Count > 0)
        {
            // Why not just proceed: a file whose declarations the delta cannot identify is
            // indistinguishable from one that declared something and no longer does, so
            // deleting an object there would pass for a comment-only edit while its symbol
            // survived in the baseline.
            FullCompileBecause(
                moduleName,
                string.Join("; ", unaccounted.Take(3))
                    + (unaccounted.Count > 3 ? $" (+{unaccounted.Count - 3} more)" : string.Empty));
            return null;
        }

        // Keyed, so two declarations of one key in the changed files collapse to one and the
        // ownership guard below cannot see it — there is no untouched owner to compare against
        // when both files are new. That is deliberate, and no guard is added for it: a key
        // collision within a kind IS an AL duplicate (nothing legal produces two objects with
        // one RadObjectKey), and `CreateForRad` is handed the syntax trees of every changed
        // file rather than the change model's object list — so both declarations reach the
        // compiler, its declaration pass reports the duplicate with the same AL id a cold build
        // reports, and this cycle returns diagnostics and commits nothing. Pinned by
        // RadIdlessObjectTests.TwoFilesAddedInOneCycleDeclaringOneKey_DoNotCollapseIntoOne
        // over a codeunit, an interface and an entitlement — one per discovery route.
        var declaredNow = new Dictionary<AlRunner.Rad.RadObjectKey, AlRunner.Rad.RadObjectRef>();
        foreach (var sym in declaredSymbols)
        {
            var objRef = new AlRunner.Rad.RadObjectRef(
                ObjectKey(sym), sym.Name ?? string.Empty, NamespaceOf(sym));
            declaredNow[objRef.Key] = objRef;
            var file = FileOf(sym);
            if (file != null && objectsByFile.TryGetValue(file, out var list)) list.Add(objRef);
        }
        foreach (var (file, objRef) in idlessNow)
        {
            if (!declaredNow.TryAdd(objRef.Key, objRef)) continue;
            if (objectsByFile.TryGetValue(file, out var list)) list.Add(objRef);
        }

        // An `entitlement` is the one AL kind BC refuses to bind against the packaged baseline.
        // `ObjectEntitlements` may only name permission sets declared in the SAME module, and
        // a permission set resolved from the previously committed module definition does not
        // satisfy that: the delta fails with AL0683 ("belongs to a different module and cannot
        // be used when defining entitlements") on a tree that compiles clean cold. Measured:
        // with the permission set's own file in the same delta it binds without a diagnostic.
        // So an entitlement is compiled together with the app's permission sets. All of them,
        // because which ones it names is only recoverable by parsing the property — and an app
        // has a handful of permission sets against approximately never editing an entitlement.
        if (declaredNow.Values.Any(item => string.Equals(item.Key.Kind, "Entitlement", StringComparison.Ordinal)))
        {
            var permissionSetFiles = ws.FilesDeclaring("PermissionSet")
                .Where(File.Exists)
                .Where(path => !changedFiles.Contains(path, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (permissionSetFiles.Count > 0)
            {
                Console.Error.WriteLine(
                    $"  [watch] {moduleName}: an entitlement changed — also binding " +
                    $"{permissionSetFiles.Count} permission-set file(s) it may name");
                return DeltaCompile(
                    moduleName,
                    dirs,
                    ws,
                    hashes,
                    changedFiles.Concat(permissionSetFiles).Order(StringComparer.Ordinal).ToList(),
                    removedFiles,
                    refLoader,
                    specs);
            }
        }

        // Which FILE owns a key answers both questions this loop has to answer, from one lookup
        // that replaces the `ws.Declares` scan this used to do:
        //
        //  * owned already → a modification, not an addition;
        //  * owned by a file this cycle did not touch → a DUPLICATE declaration, and only the
        //    compiler can say what that means. `ws.Declares` answered "does the module declare
        //    this key", which is true either way, so a new object reusing an existing id or
        //    name passed as a modification of the other file's object — that file's copy was
        //    then stripped from the packaged baseline and the cycle reported either success or
        //    an unrelated dangling-reference error. Measured against a cold compile of the same
        //    tree: a duplicated `interface` name is four AL0197s cold and NO diagnostic at all
        //    through the delta; a duplicated codeunit id is four AL0264s cold and one AL0185
        //    through the delta. Hand the whole module over instead.
        var touchedFiles = changedFiles.Concat(removedFiles).ToHashSet(StringComparer.Ordinal);
        var added = new List<AlRunner.Rad.RadObjectRef>();
        var modified = new List<AlRunner.Rad.RadObjectRef>();
        foreach (var objRef in declaredNow.Values)
        {
            var owner = ws.FileOf(objRef.Key);
            if (owner != null && !touchedFiles.Contains(owner))
            {
                FullCompileBecause(
                    moduleName,
                    $"{objRef.Key.Kind} '{objRef.Name}' is also declared by " +
                    $"{Path.GetFileName(owner)}, which this cycle did not touch — only the " +
                    "compiler can say which of the two is the duplicate");
                return null;
            }
            (owner != null ? modified : added).Add(objRef);
        }

        // Removed = objects the touched files used to declare and nothing declares now.
        // An object cannot escape to an untouched file: moving it edits its new home.
        var removed = new List<AlRunner.Rad.RadObjectRef>();
        var seenRemoved = new HashSet<AlRunner.Rad.RadObjectKey>();
        foreach (var f in changedFiles.Concat(removedFiles))
            foreach (var prev in ws.ObjectsIn(f))
                if (!declaredNow.ContainsKey(prev.Key) && seenRemoved.Add(prev.Key))
                    removed.Add(prev);

        var fileDeclarations = MapFileDeclarations(trees, objectsByFile);

        // Files moved, no OBJECT did — creating an empty file, editing a comment-only one,
        // deleting it again. There is nothing for a compiler to do, so none runs: the cycle
        // records the new hashes and the module keeps every loaded type it already had.
        // Reaching CreateForRad with an empty change model would at best emit nothing at
        // greater cost, and this is also the case where the RAD emitter has no object to
        // initialize its module from, which the baseline merge below requires.
        if (added.Count == 0 && modified.Count == 0 && removed.Count == 0)
        {
            Console.Error.WriteLine(
                $"  [watch] {moduleName}: {changedFiles.Count + removedFiles.Count} file(s) changed, " +
                $"no AL object did — nothing to compile ({sw.ElapsedMilliseconds}ms)");
            return new RadEmitResult(
                new BcEmitOutput(Array.Empty<EmittedSource>(), Array.Empty<string>(), Array.Empty<string>()),
                FullRebuild: false, NoChange: false)
            {
                WorkspaceUpdate = new AlRunner.Rad.RadWorkspaceUpdate(
                    hashes,
                    objectsByFile,
                    fileDeclarations,
                    new Dictionary<AlRunner.Rad.RadObjectKey, HashSet<AlRunner.Rad.RadObjectKey>>(),
                    new Dictionary<AlRunner.Rad.RadObjectKey, AlRunner.Rad.RadObjectKey>(),
                    Array.Empty<AlRunner.Rad.RadObjectKey>(),
                    WorkspaceBaseline(ws),
                    Full: false),
            };
        }

        // Microsoft's RAD model is object-kind generic. Modified and removed objects
        // must be absent from the packaged baseline so their stale definitions cannot
        // shadow the supplied syntax (especially when several changed objects call one
        // another). Added objects, by definition, have no baseline entry to strip.
        var diags = new List<string>();
        var model = new NavCA.ObjectChangeModelDefinition
        {
            Added = added.Select(ToChangeElement).ToArray(),
            Modified = modified.Select(ToChangeElement).ToArray(),
            Removed = removed.Select(ToChangeElement).ToArray(),
        };
        var replacedOrRemoved = modified
            .Select(item => ws.Object(item.Key) ?? item)
            .Concat(removed)
            .Select(ToChangeElement)
            .ToArray();
        // …with one exception: an EXTENSION object stays. Its fields/controls/values reach
        // the target object only through the packaged module — strip a tableextension and
        // `Rec."<its own field>"` inside its own trigger stops binding with AL0132, because
        // the target table's symbol is resolved from the packaged definition and now has no
        // extension at all. Measured on NP Retail, where adding one field to
        // GeneralPostingSetup.TableExt failed the cycle with three AL0132s against fields
        // declared in that same file. Leaving it in does not shadow the edit: the supplied
        // syntax tree is still the authority for the object being rebound, so a field the
        // edit adds binds and one it removes stops binding — both pinned by
        // RadTableExtensionSelfReferenceTests.
        var packaged = AlRunner.Rad.ModuleDefinitionOps.WithoutObjects(
            WorkspaceBaseline(ws),
            modified.Concat(removed).Select(item => item.Key)
                .Where(key => !key.IsExtension)
                .ToArray());
        var rad = NavCA.Compilation.CreateForRad(
            moduleName: moduleName,
            objectChangeModelDefinition: model,
            packagedModuleDefinition: packaged,
            symbolReferenceLoader: refLoader!,
            symbolReferences: specs,
            publisher: publisher,
            version: version,
            appId: appId,
            alternateIds: ImmutableArray<Guid>.Empty,
            internalsVisibleTo: ivt,
            syntaxTrees: trees,
            options: compOpts,
            dotNetResolverFactory: GetOrCreateDotNetFactory());

        // Ask for binding errors BEFORE code generation. BC's RAD emitter does not
        // survive them: a reference to an object this delta removed makes it throw out of
        // codegen ("Unexpected value 'None' of type NavTypeKind") rather than report the
        // AL0185 its own declaration pass already found. Asking first turns a dangling
        // reference into one diagnostic naming the missing object, instead of a whole-module
        // rebuild whose emit-retry then silently drops the caller — and the caller's caller.
        foreach (var d in rad.GetDeclarationDiagnostics().Where(d => d.Severity == NavDiag.DiagnosticSeverity.Error))
            diags.Add($"{d.Location}: error {d.Id}: {d.GetMessage().Split('\n', 2)[0]}");
        if (diags.Count > 0)
            return new RadEmitResult(
                new BcEmitOutput(Array.Empty<EmittedSource>(), diags, Array.Empty<string>()),
                FullRebuild: false, NoChange: false);

        // Metadata the emit registers is held here until the generation loads — see
        // RadMetadataCapture. Without it a candidate the C# backend rejects still leaves
        // the live runtime describing objects whose code never loaded.
        using var capture = AlRunner.Rad.RadMetadataCapture.Begin();
        var outputter = new CaptureOutputter();
        NavCA.Emit.EmitResult? emitResult = null;
        Exception? caught = null;
        try { emitResult = rad.Emit(NavCA.EmitOptions.Default, outputter); }
        catch (Exception ex) { caught = ex; }

        if (emitResult != null && !emitResult.Success)
            foreach (var d in emitResult.Diagnostics.Where(d => d.Severity == NavDiag.DiagnosticSeverity.Error))
                diags.Add($"{d.Location}: error {d.Id}: {d.GetMessage().Split('\n', 2)[0]}");

        if (caught != null)
        {
            FullCompileBecause(
                moduleName,
                $"the delta emit crashed ({caught.GetType().Name}: " +
                $"{caught.Message.Split('\n', 2)[0]})");
            return null;
        }

        // An id-less object generates no C#, so counting it here would make every delta that
        // touches one look like it silently dropped an object and fall back to a full compile.
        int expectedEmits = added.Concat(modified).Count(item => item.Key.EmitsCode);
        if (outputter.Captured.Count != expectedEmits)
        {
            if (diags.Count > 0)
                return new RadEmitResult(
                    new BcEmitOutput(Array.Empty<EmittedSource>(), diags, Array.Empty<string>()),
                    FullRebuild: false, NoChange: false);
            FullCompileBecause(
                moduleName,
                $"the delta emitted {outputter.Captured.Count} object(s) but {expectedEmits} " +
                "were added/modified, with no diagnostics to explain the difference");
            return null;
        }

        if (diags.Count > 0)
            return new RadEmitResult(
                new BcEmitOutput(Array.Empty<EmittedSource>(), diags, Array.Empty<string>()),
                FullRebuild: false, NoChange: false);

        NavSymRef.ModuleDefinition mergedBaseline;
        try
        {
            if (outputter.Module == null)
                throw new InvalidOperationException("the RAD emitter did not initialize its module");
            mergedBaseline = MergeRadBaseline(
                outputter.Module,
                // Microsoft's writer drops a removed object from the previous module by
                // matching the change element it is given, and a serialized id-less element
                // carries a synthesized id that the element built from a compiler symbol
                // cannot reproduce — so a deleted interface or control add-in survived the
                // merge and the next delta still resolved it. Strip those by name first;
                // the writer then has nothing to carry forward.
                AlRunner.Rad.ModuleDefinitionOps.WithoutObjects(
                    WorkspaceBaseline(ws),
                    removed.Select(item => item.Key)
                        .Where(key => AlRunner.Rad.RadObjectKey.IsIdlessKind(key.Kind))
                        .ToArray()),
                replacedOrRemoved,
                rad.RuntimeVersion!);
        }
        catch (Exception ex)
        {
            FullCompileBecause(
                moduleName,
                "the delta symbol baseline could not be merged " +
                $"({ex.GetType().Name}: {ex.Message.Split('\n')[0]})");
            return null;
        }

        // Generated calls to codeunit procedures bake Microsoft's member id. When that
        // callable surface moves, rebind only the direct callers recorded from the last
        // full semantic model. Their own unchanged surface does not pull in transitive
        // callers. Object removal likewise rebinds direct users so a dangling reference
        // becomes an AL diagnostic instead of silently executing an old loaded type.
        var changedSurfaces = modified
            // Codeunits because generated calls bake Microsoft's member id; id-less objects
            // because they are binding contracts (an interface's method set, a control
            // add-in's surface) that their users were compiled against.
            .Where(item => item.Key.IsCodeunit
                || AlRunner.Rad.RadObjectKey.IsIdlessKind(item.Key.Kind))
            .Where(item =>
            {
                var previous = ws.Object(item.Key);
                var before = AlRunner.Rad.ModuleDefinitionOps.ObjectSurfaceFingerprint(
                    WorkspaceBaseline(ws), item.Key);
                var after = AlRunner.Rad.ModuleDefinitionOps.ObjectSurfaceFingerprint(
                    mergedBaseline, item.Key);
                return previous == null
                    || !string.Equals(previous.Name, item.Name, StringComparison.Ordinal)
                    || !string.Equals(previous.Namespace, item.Namespace, StringComparison.Ordinal)
                    || before == null
                    || after == null
                    || !string.Equals(before, after, StringComparison.Ordinal);
            })
            .Select(item => item.Key)
            .Concat(removed.Select(item => item.Key))
            .ToArray();
        // …plus every entitlement, when a permission set's NAME moved. An entitlement has no
        // compiler symbol, so no semantic model ever reports the edge from one to the
        // permission sets its `ObjectEntitlements` names — which meant renaming or removing a
        // permission set left each of them naming something that no longer exists, with the
        // delta reporting success where a cold compile reports AL0185.
        //
        // `ObjectEntitlements` names permission sets by NAME, so the name is the whole trigger:
        // a removal, or a modification that renamed one. Not `changedSurfaces`, which is keyed
        // — a permission set has a real object id, so renaming one keeps its key and arrives
        // here as a modification, not as a removal plus an addition. And not every permission
        // set edit either: changing `Assignable` or a permission line cannot break a reference
        // by name, and pulling every entitlement (and with it, via the rule above, every
        // permission set) into an ordinary permission-set edit would be a real cost for none.
        var permissionSetNameMoved =
            removed.Any(item => string.Equals(item.Key.Kind, "PermissionSet", StringComparison.Ordinal))
            || modified.Any(item =>
                string.Equals(item.Key.Kind, "PermissionSet", StringComparison.Ordinal)
                && ws.Object(item.Key) is { } previous
                && !string.Equals(previous.Name, item.Name, StringComparison.OrdinalIgnoreCase));
        var entitlementFiles = permissionSetNameMoved
            ? ws.FilesDeclaring("Entitlement")
            : Array.Empty<string>();
        var callerFiles = ws.DirectUsersOf(changedSurfaces)
            .Select(ws.FileOf)
            .OfType<string>()
            .Concat(entitlementFiles)
            .Where(File.Exists)
            .Where(path => !changedFiles.Contains(path, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (callerFiles.Count > 0)
        {
            Console.Error.WriteLine(
                $"  [watch] {moduleName}: rebinding {callerFiles.Count} direct caller file(s)");
            return DeltaCompile(
                moduleName,
                dirs,
                ws,
                hashes,
                changedFiles.Concat(callerFiles).Order(StringComparer.Ordinal).ToList(),
                removedFiles,
                refLoader,
                specs);
        }

        var update = new AlRunner.Rad.RadWorkspaceUpdate(
            hashes,
            objectsByFile,
            fileDeclarations,
            MapObjectReferences(rad),
            MapExtensionTargets(rad),
            removed.Select(item => item.Key).ToArray(),
            mergedBaseline,
            Full: false);

        Console.Error.WriteLine(
            $"  [watch] {moduleName}: delta +{added.Count} ~{modified.Count} -{removed.Count} " +
            $"over {changedFiles.Count} changed file(s) → " +
            $"{outputter.Captured.Count} object(s) re-emitted ({sw.ElapsedMilliseconds}ms)");

        return new RadEmitResult(
            new BcEmitOutput(outputter.Captured, diags, Array.Empty<string>()),
            FullRebuild: false, NoChange: false)
        {
            WorkspaceUpdate = update,
            Changes = new RadChangeSet(added, modified, removed),
            Metadata = capture,
        };
    }

    private static NavSymRef.ModuleDefinition MergeRadBaseline(
        NavCA.IModuleSymbol deltaModule,
        NavSymRef.ModuleDefinition previous,
        IList<NavCA.ObjectChangeElement> replacedOrRemoved,
        Version runtimeVersion)
    {
        using var stream = new MemoryStream();
        NavCA.CompilationUtilities.WriteSymbolReference(
            stream,
            deltaModule,
            previous,
            replacedOrRemoved,
            runtimeVersion);
        stream.Position = 0;
        var merged = NavSymRef.SymbolReferenceJsonReader.ReadModule(stream);

        // The writer uses the delta module's packages. RAD object compilations carry no
        // dotnet declaration trees, so retain the complete committed package surface.
        merged.DotNetPackages = previous.DotNetPackages;
        return merged;
    }

    /// <summary>
    /// Everything a delta may not silently change. Two compiles with the same signature
    /// resolve identical external symbols, so an object compiled under one is
    /// interchangeable with the same object compiled under the other.
    ///
    /// <para>Every line is <c>&lt;facet&gt;|&lt;value&gt;</c> so that
    /// <see cref="DescribeSignatureChange"/> can say WHICH facet moved. A watch cycle that
    /// rebuilds the whole module has to explain itself: switching a git branch usually also
    /// switches <c>app.json</c>, and "full rebuild" with no reason looks like the delta path
    /// simply failing.</para>
    /// </summary>
    private static string ReferenceSignature(
        string moduleName, NavCA.SymbolReferenceSpecification[] specs, IReadOnlyList<string> dirs)
    {
        var parts = specs
            .Select(s => $"ref|{s.Publisher}|{s.Name}|{s.Version}|{s.AppId}")
            .Concat((CurrentInternalsVisibleTo(dirs) ?? [])
                .Select(s => $"ivt|{s.Publisher}|{s.Name}|{s.Version}|{s.AppId}"))
            .OrderBy(s => s, StringComparer.Ordinal);
        return string.Join("\n",
            new[]
            {
                $"name|{moduleName}",
                $"id|{_currentAppId?.ToString() ?? "-"}",
                $"publisher|{_currentPublisher ?? "-"}",
                $"version|{_currentVersion?.ToString() ?? "-"}",
                $"define|{string.Join(",", _extraPreprocessorSymbols ?? Array.Empty<string>())}",
            }.Concat(parts));
    }

    /// <summary>
    /// Which facet of the reference signature moved, in the words the developer who moved it
    /// would use. Four of the six come straight out of <c>app.json</c>, so the message names
    /// the file — a branch switch that carries a different <c>app.json</c> is by far the most
    /// common way a warm watch loses its baseline, and the developer needs to recognise the
    /// cause rather than read it as the delta path giving up.
    /// </summary>
    private static string DescribeSignatureChange(string previous, string current)
    {
        var was = previous.Split('\n');
        var now = current.Split('\n');

        string? Single(string facet)
        {
            var a = was.FirstOrDefault(line => line.StartsWith(facet + "|", StringComparison.Ordinal));
            var b = now.FirstOrDefault(line => line.StartsWith(facet + "|", StringComparison.Ordinal));
            return string.Equals(a, b, StringComparison.Ordinal)
                ? null
                : $"{a?[(facet.Length + 1)..] ?? "none"} → {b?[(facet.Length + 1)..] ?? "none"}";
        }

        int Count(string[] lines, string facet) =>
            lines.Count(line => line.StartsWith(facet + "|", StringComparison.Ordinal));

        string? Set(string facet, string label) =>
            Count(was, facet) == Count(now, facet)
                && was.Where(l => l.StartsWith(facet + "|", StringComparison.Ordinal))
                    .SequenceEqual(now.Where(l => l.StartsWith(facet + "|", StringComparison.Ordinal)), StringComparer.Ordinal)
                ? null
                : $"{label} ({Count(was, facet)} → {Count(now, facet)})";

        var reasons = new List<string>();
        if (Single("name") is { } name) reasons.Add($"app.json changed the app name: {name}");
        if (Single("id") is { } id) reasons.Add($"app.json changed the app id: {id}");
        if (Single("publisher") is { } publisher) reasons.Add($"app.json changed the publisher: {publisher}");
        if (Single("version") is { } version) reasons.Add($"app.json changed the app version: {version}");
        if (Single("define") is { } define) reasons.Add($"the --define preprocessor symbols changed: {define}");
        // A resolved reference can move because app.json's `dependencies` changed OR because a
        // different .app landed in .alpackages. Both are true causes; name both rather than
        // guess, because a branch switch routinely does both at once.
        if (Set("ref", "the resolved dependency set changed") is { } refs)
            reasons.Add($"{refs} — app.json dependencies, or the .app files in .alpackages");
        if (Set("ivt", "the internalsVisibleTo grants changed") is { } ivt) reasons.Add(ivt);

        return reasons.Count > 0
            ? string.Join("; ", reasons)
            : "the compilation's reference surface changed";
    }

    private NavCA.ParseOptions ParseOptionsForCompile() => new(
        runtimeVersion: null!,
        preprocessorSymbols: Enumerable.Range(1, 25).Select(n => $"CLEANSCHEMA{n}")
            .Concat(_extraPreprocessorSymbols ?? []),
        documentationMode: NavCA.DocumentationMode.None);

    /// <summary>
    /// Whether <see cref="AlRunner.Rad.RadObjectKey"/> can identify this object at all.
    ///
    /// An id identifies it when there is one; otherwise the name does. A <c>profile</c> is
    /// why this is not simply the <c>ISymbolWithId</c> test: it implements that interface and
    /// then reports id 0, so keying on the id alone made every profile in an app key as
    /// <c>Profile:0</c>. Measured on NP Retail, that collision threw out of the baseline
    /// snapshot and left the app without one — every cycle a full compile, no delta ever.
    ///
    /// The id-less kinds are listed explicitly in <see cref="AlRunner.Rad.RadObjectKey"/> rather
    /// than inferred from "reports no id", because being name-keyed is only half of being
    /// supported: for a MODIFIED object the module definition has to carry it so a delta can
    /// strip the pre-edit copy, and that has to be verified per kind. All six kinds AL gives no
    /// id are on that list now; <c>entitlement</c> is the one with no serialized form at all,
    /// which is safe for the opposite reason — there is no copy to shadow an edit.
    /// </summary>
    private static bool IsKeyable(NavCA.ISymbol symbol) =>
        symbol is NavCA.ISymbolWithId { Id: > 0 }
        || (AlRunner.Rad.RadObjectKey.IsIdlessKind(symbol.Kind.ToString())
            && !string.IsNullOrEmpty(symbol.Name));

    /// <summary>
    /// The id-less objects Microsoft's symbol API does not report at all.
    ///
    /// <para><c>GetDeclaredApplicationObjectSymbols()</c> filters the module's declared
    /// symbols to <c>IApplicationObjectTypeSymbol</c>, and <c>interface</c> and
    /// <c>controladdin</c> symbols do not implement it — an app can declare them and the
    /// workspace would never learn which file did, so that file stayed untracked for the
    /// life of the process and every edit to it, comment included, forced a whole-module
    /// rebuild. Their declarations are read off the syntax tree instead, which is all that
    /// is needed: a kind, a name and a file is the entire identity an id-less object has.</para>
    ///
    /// <para>An <c>entitlement</c> is the third, and the one with no serialized form at all —
    /// <c>ModuleDefinition</c> has no <c>Entitlements</c> array. Reading it off the syntax is
    /// therefore the ONLY way the workspace can know its file declares anything; without it,
    /// deleting an entitlement looked exactly like a comment-only edit.</para>
    ///
    /// <para><c>pagecustomization</c> and <c>profileextension</c> are deliberately absent:
    /// the symbol API DOES return them (as application objects reporting id 0), so they are
    /// keyed from symbols like every other object and listing them here would register each
    /// one twice.</para>
    /// </summary>
    private static IEnumerable<(string File, AlRunner.Rad.RadObjectRef Object)> IdlessDeclarations(
        IEnumerable<NavSyntax.SyntaxTree> trees)
    {
        foreach (var tree in trees)
        {
            if (FilePathOf(tree) is not string file) continue;
            foreach (var (node, ns) in ObjectDeclarations(tree))
            {
                if (IdlessKindOf(node) is not string kind) continue;
                var name = UnquoteAlName(SyntaxName(node));
                if (string.IsNullOrEmpty(name)) continue;
                yield return (file, new AlRunner.Rad.RadObjectRef(
                    AlRunner.Rad.RadObjectKey.For(kind, 0, name), name, ns));
            }
        }
    }

    private static string? FilePathOf(NavSyntax.SyntaxTree tree)
    {
        try { return tree.FilePath is { Length: > 0 } path ? path : null; }
        catch { return null; }
    }

    /// <summary>
    /// Every top-level AL declaration in a parsed file, with its enclosing namespace.
    ///
    /// <para><c>ObjectSyntax</c> is the base type of ALL of them — the id-bearing kinds through
    /// <c>ApplicationObjectSyntax</c>, and <c>profile</c>, <c>interface</c>, <c>controladdin</c>,
    /// <c>entitlement</c> and <c>dotnet</c> directly. Verified by reflecting on BC 28.1's syntax
    /// hierarchy rather than inferred, because the two hierarchies disagree: a <c>profile</c> is
    /// reported as an application object SYMBOL and is not an <c>ApplicationObjectSyntax</c>.</para>
    ///
    /// <para>That makes "this tree has no <c>ObjectSyntax</c> node" BC's own parser stating that
    /// the file declares nothing — which is what a delta needs before it may skip such a file.
    /// The absence of a SYMBOL says something weaker: an unidentifiable declaration looks the
    /// same from there.</para>
    /// </summary>
    private static IEnumerable<(NavSyntax.ObjectSyntax Node, string Namespace)> ObjectDeclarations(
        NavSyntax.SyntaxTree tree)
    {
        NavSyntax.CompilationUnitSyntax? unit;
        try { unit = tree.GetRoot() as NavSyntax.CompilationUnitSyntax; }
        catch { return []; }
        if (unit == null) return [];
        return TopLevelDeclarations(unit, string.Empty)
            .Where(item => item.Node is NavSyntax.ObjectSyntax)
            .Select(item => ((NavSyntax.ObjectSyntax)item.Node, item.Namespace));
    }

    /// <summary>
    /// Whether the delta path knows how to identify a declaration of this shape — i.e. whether
    /// one of its two discovery routes (a compiler symbol, or <see cref="IdlessDeclarations"/>)
    /// is expected to return it.
    ///
    /// <para>The list is complete for BC 28.1 and is a guard, not a lookup: an AL kind a future
    /// compiler adds arrives here as an unrecognised <c>ObjectSyntax</c> node and takes the
    /// full-compile path, instead of being silently skipped as a file that declares nothing.</para>
    /// </summary>
    private static bool IsRecognisedDeclaration(NavSyntax.ObjectSyntax node) =>
        node is NavSyntax.ApplicationObjectSyntax    // every id-bearing kind, pagecustomization, profileextension
             or NavSyntax.ProfileSyntax              // a symbol reporting id 0 — but not an ApplicationObjectSyntax
             or NavSyntax.InterfaceSyntax
             or NavSyntax.ControlAddInSyntax
             or NavSyntax.EntitlementSyntax
             or NavSyntax.DotNetPackageSyntax;       // recognised, and always a full compile — see RadFileDeclarations

    /// <summary>
    /// The per-file record a later delta consults for the files this compile is about to
    /// commit: which declared a <c>dotnet</c> package, and which declared more than the
    /// object map could record. Only such files get an entry — on a real app that is almost
    /// none of them.
    /// </summary>
    private static Dictionary<string, AlRunner.Rad.RadFileDeclarations> MapFileDeclarations(
        IEnumerable<NavSyntax.SyntaxTree> trees,
        IReadOnlyDictionary<string, List<AlRunner.Rad.RadObjectRef>> objectsByFile)
    {
        var map = new Dictionary<string, AlRunner.Rad.RadFileDeclarations>(StringComparer.Ordinal);
        foreach (var tree in trees)
        {
            if (FilePathOf(tree) is not string file) continue;
            var declarations = ObjectDeclarations(tree).Select(item => item.Node).ToList();
            var packages = declarations.Count(node => node is NavSyntax.DotNetPackageSyntax);
            // Counting is what catches a key CLAIMED BY TWO FILES: neither node classification
            // nor the symbol API reports that, but the object map drops one of the two, so the
            // file's recorded objects come up short of what it declares.
            var recorded = objectsByFile.TryGetValue(file, out var objects) ? objects.Count : 0;
            var record = new AlRunner.Rad.RadFileDeclarations(
                DotNetPackage: packages > 0,
                Unrecorded: declarations.Count - packages != recorded);
            if (record != default) map[file] = record;
        }
        return map;
    }

    /// <summary>
    /// Object declarations in a compilation unit, descending through namespace declarations
    /// and carrying the enclosing namespace down — BC nests a namespaced file's objects
    /// under a <c>NamespaceDeclarationSyntax</c> rather than at the root.
    /// </summary>
    private static IEnumerable<(NavCA.SyntaxNode Node, string Namespace)> TopLevelDeclarations(
        NavCA.SyntaxNode parent, string ns)
    {
        foreach (var node in parent.ChildNodes())
        {
            if (node is NavSyntax.NamespaceDeclarationSyntax nested)
            {
                var name = SyntaxName(nested) ?? ns;
                foreach (var inner in TopLevelDeclarations(nested, name)) yield return inner;
            }
            else
            {
                yield return (node, ns);
            }
        }
    }

    private static string? IdlessKindOf(NavCA.SyntaxNode node) => node switch
    {
        NavSyntax.InterfaceSyntax => "Interface",
        NavSyntax.ControlAddInSyntax => "ControlAddIn",
        NavSyntax.EntitlementSyntax => "Entitlement",
        _ => null,
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?>
        _syntaxNameProperties = new();

    /// <summary>
    /// The declared name of a syntax node. Read by reflection because the id-less kinds do
    /// not share a base type that exposes one — an <c>ApplicationObjectSyntax</c> has
    /// <c>Name</c> and <c>ObjectId</c> together, and these kinds are precisely the ones that
    /// are not application objects.
    /// </summary>
    private static string? SyntaxName(NavCA.SyntaxNode node)
    {
        var property = _syntaxNameProperties.GetOrAdd(
            node.GetType(), static type => type.GetProperty("Name"));
        try { return property?.GetValue(node)?.ToString(); }
        catch { return null; }
    }

    /// <summary>
    /// Strip AL's identifier quoting. Symbols report <c>RAD Idless Contract</c>; the syntax
    /// tree reports <c>"RAD Idless Contract"</c>. The two must agree, because the name IS
    /// the key for these objects and it is also what Microsoft's change model is given.
    /// </summary>
    private static string UnquoteAlName(string? name)
    {
        var text = name?.Trim() ?? string.Empty;
        if (text.Length < 2 || text[0] != '"' || text[^1] != '"') return text;
        // AL escapes a quote inside a quoted identifier by doubling it, and the compiler
        // reports the decoded value. Stripping only the delimiters leaves `A ""B""` where
        // every other source says `A "B"` — two keys for one object, and the delta then
        // fails to strip its own baseline copy.
        return text[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// The declared objects a delta can track: keyable, and uniquely keyed within the
    /// module. A key claimed by more than one object identifies neither, so both are left
    /// out — their files then look untracked and changing one takes the full-compile path.
    /// </summary>
    private static List<NavCA.IApplicationObjectTypeSymbol> UniquelyKeyedObjects(
        NavCA.Compilation compilation)
    {
        var byKey = new Dictionary<AlRunner.Rad.RadObjectKey, NavCA.IApplicationObjectTypeSymbol?>();
        foreach (var symbol in compilation.GetDeclaredApplicationObjectSymbols())
        {
            if (!IsKeyable(symbol)) continue;
            var key = ObjectKey(symbol);
            byKey[key] = byKey.ContainsKey(key) ? null : symbol;
        }
        return byKey.Values.OfType<NavCA.IApplicationObjectTypeSymbol>().ToList();
    }

    /// <summary>Which source file declares each application object in a bound compilation.</summary>
    private static Dictionary<string, List<AlRunner.Rad.RadObjectRef>> MapObjectsToFiles(NavCA.Compilation compilation)
    {
        var map = new Dictionary<string, List<AlRunner.Rad.RadObjectRef>>(StringComparer.Ordinal);
        var seen = new HashSet<AlRunner.Rad.RadObjectKey>();

        void Record(string? file, AlRunner.Rad.RadObjectRef obj)
        {
            if (file == null || !seen.Add(obj.Key)) return;
            if (!map.TryGetValue(file, out var list))
                map[file] = list = new List<AlRunner.Rad.RadObjectRef>();
            list.Add(obj);
        }

        foreach (var sym in UniquelyKeyedObjects(compilation))
            Record(FileOf(sym), new AlRunner.Rad.RadObjectRef(
                ObjectKey(sym), sym.Name ?? string.Empty, NamespaceOf(sym)));

        // …plus the kinds the symbol API never returns. Recorded second so a symbol always
        // wins if both routes see the same object.
        foreach (var (file, obj) in IdlessDeclarations(compilation.SyntaxTrees))
            Record(file, obj);

        return map;
    }

    /// <summary>
    /// Compact source-object dependency edges from Microsoft's already-bound semantic
    /// models. Stored only as object keys; no generated C# or syntax trees survive the
    /// baseline. A later callable-surface change uses the reverse one-hop relation to
    /// rebind direct callers without dragging in their transitive users.
    /// </summary>
    private static Dictionary<AlRunner.Rad.RadObjectKey, HashSet<AlRunner.Rad.RadObjectKey>>
        MapObjectReferences(NavCA.Compilation compilation)
    {
        var declared = UniquelyKeyedObjects(compilation);
        var ownAppId = declared.FirstOrDefault()?.ContainingModule?.AppId;
        var result = declared.ToDictionary(
            ObjectKey,
            _ => new HashSet<AlRunner.Rad.RadObjectKey>());

        foreach (var group in declared
            .Where(symbol => symbol.DeclaringSyntaxReference?.SyntaxTree != null)
            .GroupBy(symbol => symbol.DeclaringSyntaxReference!.SyntaxTree))
        {
            var sources = group.Select(ObjectKey).Distinct().ToArray();
            var model = compilation.GetSemanticModel(group.Key);
            foreach (var node in group.Key.GetRoot().DescendantNodesAndSelf())
            {
                try
                {
                    var info = model.GetSymbolInfo(node);
                    AddReference(info.Symbol);
                    foreach (var candidate in info.CandidateSymbols) AddReference(candidate);
                }
                catch
                {
                    // A malformed/incomplete node has no useful dependency edge. AL
                    // diagnostics still reject its compilation; graph capture is best effort.
                }
            }

            void AddReference(NavCA.ISymbol? symbol)
            {
                if (ReferenceTargetKey(symbol, ownAppId) is not { } targetKey) return;
                foreach (var source in sources)
                    if (source != targetKey) result[source].Add(targetKey);
            }
        }

        foreach (var extension in declared.OfType<NavCA.IApplicationObjectExtensionTypeSymbol>())
            if (IsKeyable(extension.Target))
                result[ObjectKey(extension)].Add(ObjectKey(extension.Target));

        return result;
    }

    private static Dictionary<AlRunner.Rad.RadObjectKey, AlRunner.Rad.RadObjectKey>
        MapExtensionTargets(NavCA.Compilation compilation)
    {
        var result = new Dictionary<AlRunner.Rad.RadObjectKey, AlRunner.Rad.RadObjectKey>();
        foreach (var extension in UniquelyKeyedObjects(compilation)
            .OfType<NavCA.IApplicationObjectExtensionTypeSymbol>())
            if (IsKeyable(extension.Target))
                result[ObjectKey(extension)] = ObjectKey(extension.Target);
        return result;
    }

    private static NavCA.IApplicationObjectTypeSymbol? ContainingApplicationObject(NavCA.ISymbol? symbol)
    {
        for (var current = symbol; current != null; current = current.ContainingSymbol)
            if (current is NavCA.IApplicationObjectTypeSymbol applicationObject)
                return applicationObject;
        return null;
    }

    /// <summary>
    /// The object a referenced symbol belongs to, as a dependency-graph key — or null when
    /// the reference leaves this app or names nothing trackable.
    ///
    /// <para>The id-less walk is the half that is easy to miss. A codeunit that implements an
    /// interface, or a page that hosts a control add-in, depends on it exactly as much as on
    /// any codeunit it calls — but an interface is not an <c>IApplicationObjectTypeSymbol</c>,
    /// so <see cref="ContainingApplicationObject"/> never returns one and the edge was simply
    /// absent. With no recorded users, a delta that changed an interface's surface had nobody
    /// to rebind: it reported success, emitted nothing, and left every implementer bound to
    /// the previous contract. Verified against the compiler — the semantic model answers the
    /// <c>implements</c> clause with the interface symbol.</para>
    /// </summary>
    private static AlRunner.Rad.RadObjectKey? ReferenceTargetKey(NavCA.ISymbol? symbol, Guid? ownAppId)
    {
        var target = ContainingApplicationObject(symbol);
        if (target != null && IsKeyable(target))
            return target.ContainingModule?.AppId == ownAppId ? ObjectKey(target) : null;

        for (var current = symbol; current != null; current = current.ContainingSymbol)
        {
            var kind = current.Kind.ToString();
            if (!AlRunner.Rad.RadObjectKey.IsIdlessKind(kind)) continue;
            if (current.ContainingModule?.AppId != ownAppId) return null;
            return AlRunner.Rad.RadObjectKey.For(kind, 0, current.Name);
        }
        return null;
    }

    private static AlRunner.Rad.RadObjectKey ObjectKey(NavCA.IApplicationObjectTypeSymbol symbol) =>
        AlRunner.Rad.RadObjectKey.For(
            symbol.Kind.ToString(),
            symbol is NavCA.ISymbolWithId withId ? withId.Id : 0,
            symbol.Name);

    private static string? FileOf(NavCA.ISymbol symbol)
    {
        try
        {
            var path = symbol.DeclaringSyntaxReference?.SyntaxTree?.FilePath;
            if (!string.IsNullOrEmpty(path)) return path;
            return symbol.Location?.SourceTree?.FilePath is { Length: > 0 } p ? p : null;
        }
        catch { return null; }
    }

    private static string NamespaceOf(NavCA.ISymbol symbol)
    {
        try
        {
            var ns = symbol.ContainingNamespace;
            // BC models the module root as an unnamed namespace; ObjectChangeElement wants
            // the dotted name or empty, matching what ObjectChangeElement.Create produces.
            return ns == null || string.IsNullOrEmpty(ns.Name) ? string.Empty : ns.ToString() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static NavCA.ObjectChangeElement ToChangeElement(AlRunner.Rad.RadObjectRef obj) => new()
    {
        Id = obj.Key.Id,
        Kind = Enum.Parse<NavCA.SymbolKind>(obj.Key.Kind),
        Name = obj.Name,
        Namespace = obj.Namespace,
    };

    private static NavSymRef.ModuleDefinition WorkspaceBaseline(AlRunner.Rad.RadWorkspace ws) =>
        (NavSymRef.ModuleDefinition)(ws.Baseline
            ?? throw new InvalidOperationException($"RAD workspace {ws.ModuleName} has no symbol baseline"));

    /// <summary>Write the workspace's stable compiler-owned SymbolReference baseline.</summary>
    public static string WriteWorkspaceSymbols(AlRunner.Rad.RadWorkspace ws, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        NavSymRef.SymbolReferenceJsonWriter.WriteModule(fs, WorkspaceBaseline(ws));
        return path;
    }
}
