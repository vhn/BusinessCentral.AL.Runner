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
        bool canDelta = ws.ArmFor(ReferenceSignature(moduleName, specs, dirs));

        if (!canDelta)
            return FullCompile(alFolders, moduleName, ws, hashes);

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
            Console.Error.WriteLine(
                $"  [rad] {moduleName}: delta compile threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
        // A fallback is still only a candidate until its generated C# loads. Keep the
        // committed hashes/baseline intact so a backend failure retries the same edit.
        return FullCompile(alFolders, moduleName, ws, hashes);
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
            try
            {
                var update = new AlRunner.Rad.RadWorkspaceUpdate(
                    hashes,
                    MapObjectsToFiles(comp),
                    MapObjectReferences(comp),
                    MapExtensionTargets(comp),
                    Array.Empty<AlRunner.Rad.RadObjectKey>(),
                    SymbolJsonWriter.BuildModuleDefinition(comp),
                    Full: true);
                Console.Error.WriteLine(
                    $"  [rad] {moduleName}: baseline built — {output.Sources.Count} object(s) " +
                    $"({sw.ElapsedMilliseconds}ms)");
                return new RadEmitResult(output, FullRebuild: true, NoChange: false)
                {
                    WorkspaceUpdate = update,
                };
            }
            catch (Exception ex)
            {
                // Losing the baseline costs speed, never correctness: the next cycle just
                // compiles in full again. Say so rather than looking mysteriously slow.
                Console.Error.WriteLine(
                    $"  [rad] {moduleName}: baseline snapshot failed " +
                    $"({ex.GetType().Name}: {ex.Message.Split('\n')[0]})");
            }
        }
        else if (output.Sources.Count > 0)
        {
            // An emit-retry exclusion means the module is missing objects — never make that
            // the baseline for future deltas.
            Console.Error.WriteLine(
                $"  [rad] {moduleName}: the full compile excluded objects; " +
                "its result cannot become a RAD baseline");
        }
        return new RadEmitResult(output, FullRebuild: true, NoChange: false);
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
        var keyedFiles = declaredSymbols
            .Where(IsKeyable)
            .Select(FileOf).OfType<string>()
            .Concat(idlessNow.Select(item => item.File))
            .ToHashSet(StringComparer.Ordinal);
        if (declaredSymbols.Any(sym => !IsKeyable(sym))
            || removedFiles.Any(f => ws.ObjectsIn(f).Count == 0)
            || changedFiles.Any(f => ws.ObjectsIn(f).Count == 0 && !keyedFiles.Contains(f)))
        {
            // Id-less application objects (notably controladdin) cannot be represented
            // by RadObjectKey. Treat their files conservatively: otherwise deleting one
            // looks like a comment-only edit and the old object survives in the baseline.
            Console.Error.WriteLine(
                $"  [rad] {moduleName}: an id-less or untracked object file changed — " +
                "falling back to a full compile");
            return null;
        }

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

        var added = new List<AlRunner.Rad.RadObjectRef>();
        var modified = new List<AlRunner.Rad.RadObjectRef>();
        foreach (var objRef in declaredNow.Values)
            (ws.Declares(objRef.Key) ? modified : added).Add(objRef);

        // Removed = objects the touched files used to declare and nothing declares now.
        // An object cannot escape to an untouched file: moving it edits its new home.
        var removed = new List<AlRunner.Rad.RadObjectRef>();
        var seenRemoved = new HashSet<AlRunner.Rad.RadObjectKey>();
        foreach (var f in changedFiles.Concat(removedFiles))
            foreach (var prev in ws.ObjectsIn(f))
                if (!declaredNow.ContainsKey(prev.Key) && seenRemoved.Add(prev.Key))
                    removed.Add(prev);

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
            Console.Error.WriteLine(
                $"  [rad] {moduleName}: delta emit crashed ({caught.GetType().Name}: " +
                $"{caught.Message.Split('\n', 2)[0]}) — falling back to a full compile");
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
            Console.Error.WriteLine(
                $"  [rad] {moduleName}: delta emitted {outputter.Captured.Count} object(s) but " +
                $"{expectedEmits} were added/modified, with no diagnostics — " +
                "falling back to a full compile");
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
            Console.Error.WriteLine(
                $"  [rad] {moduleName}: could not merge the delta symbol baseline " +
                $"({ex.GetType().Name}: {ex.Message.Split('\n')[0]}) — falling back to a full compile");
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
        var callerFiles = ws.DirectUsersOf(changedSurfaces)
            .Select(ws.FileOf)
            .OfType<string>()
            .Where(File.Exists)
            .Where(path => !changedFiles.Contains(path, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (callerFiles.Count > 0)
        {
            Console.Error.WriteLine(
                $"  [rad] {moduleName}: rebinding {callerFiles.Count} direct caller file(s)");
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
            MapObjectReferences(rad),
            MapExtensionTargets(rad),
            removed.Select(item => item.Key).ToArray(),
            mergedBaseline,
            Full: false);

        Console.Error.WriteLine(
            $"  [rad] {moduleName}: delta +{added.Count} ~{modified.Count} -{removed.Count} " +
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
                moduleName,
                _currentAppId?.ToString() ?? "-",
                _currentPublisher ?? "-",
                _currentVersion?.ToString() ?? "-",
                string.Join(",", _extraPreprocessorSymbols ?? Array.Empty<string>()),
            }.Concat(parts));
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
    /// The id-less kinds are listed explicitly rather than inferred from "reports no id",
    /// because being name-keyed is only half of being supported: the module definition has to
    /// carry the object so a delta can strip its pre-edit copy, and that has to be verified
    /// per kind. <c>pagecustomization</c> and <c>entitlement</c> report a name too and are
    /// deliberately NOT on the list — they keep taking the full-compile path until something
    /// declares one and proves the round trip.
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
    /// <para>Other id-less kinds (<c>pagecustomization</c>, <c>entitlement</c>) are
    /// deliberately not listed. They stay invisible and their files keep taking the
    /// full-compile path — the behaviour that was there before. Adding one is a line in
    /// <see cref="IdlessKindOf"/> plus a fixture that declares it.</para>
    /// </summary>
    private static IEnumerable<(string File, AlRunner.Rad.RadObjectRef Object)> IdlessDeclarations(
        IEnumerable<NavSyntax.SyntaxTree> trees)
    {
        foreach (var tree in trees)
        {
            string? file;
            NavCA.SyntaxNode root;
            try
            {
                file = tree.FilePath;
                if (string.IsNullOrEmpty(file)) continue;
                if (tree.GetRoot() is not NavSyntax.CompilationUnitSyntax unit) continue;
                root = unit;
            }
            catch { continue; }

            foreach (var (node, ns) in TopLevelDeclarations(root, string.Empty))
            {
                if (IdlessKindOf(node) is not string kind) continue;
                var name = UnquoteAlName(SyntaxName(node));
                if (string.IsNullOrEmpty(name)) continue;
                yield return (file!, new AlRunner.Rad.RadObjectRef(
                    AlRunner.Rad.RadObjectKey.For(kind, 0, name), name, ns));
            }
        }
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
