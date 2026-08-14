// RadBaselineSidecarTests — a cache HIT must arrive delta-ready.
//
// The behaviour under test, and why it matters
// -------------------------------------------
// A `--watch` cache HIT loads `<key>.dll` and skips Emit+Compile entirely, so the resident
// RadWorkspace has no compiler symbol baseline. The developer's FIRST edit therefore paid a
// whole-module compile to build one — 761–862 s on a 7,000-file app — which is precisely the
// moment they are blocked waiting for a result.
//
// RadBaselineSidecar persists the workspace's delta-readiness beside the cached DLL, so a HIT
// hydrates a workspace that can serve a delta immediately.
//
// What makes these tests prove something
// --------------------------------------
// "It was fast" is not assertable. Every claim here is stated as an identity: WHICH objects
// re-emit, WHICH object is classified removed, WHICH facet a rejected hydration names. The
// 20-object fixture is what makes that falsifiable — "all of it" (20) and "the one that
// changed" (1) are different numbers, so a hydration that silently degraded to a full compile
// fails on the count rather than on a stopwatch.
//
// Four of the maps a hydrated workspace restores would fail SILENTLY if they were dropped —
// the delta would report success having done the wrong thing — so each has a test that names
// the object it must not lose track of:
//
//   Baseline (ModuleDefinition) -> HydratingAWorkspace_RestoresTheExactSymbolBaseline
//   _fileHashes + _objectsByFile -> HydratedWorkspace_DeltasTheFirstEdit…, …AsARemoval
//   _referencesByObject         -> HydratedWorkspace_RebindsTheDirectCaller…
//   _declarationsByFile         -> HydratedWorkspace_StillForcesAFullCompile_WhenADotNetPackageFileIsDeleted
//   ReferenceSignature          -> HydratedWorkspace_ThenAnAppVersionChange_…

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadBaselineSidecarTests(BcEngineFixture engine)
{
    private const string ScenarioDir = "al-runner-rad-baseline-sidecar";

    private static string EnvelopePath(string tempRoot) =>
        Path.Combine(tempRoot, ".cache", "key" + AlRunner.Infrastructure.AlCacheSidecars.RadBaselineSuffix);

    private static string SymbolsPath(string tempRoot) =>
        Path.Combine(tempRoot, ".cache", "key" + AlRunner.Infrastructure.AlCacheSidecars.RadSymbolsSuffix);

    /// <summary>
    /// Everything Program.cs's cache-HIT branch does to a workspace, in the same order: take
    /// ownership of the already-loaded generation, then hydrate the compiler state beside it.
    /// A test that only called <c>TryHydrate</c> would be exercising a workspace shape
    /// production never produces.
    /// </summary>
    private static RadWorkspace HydrateLikeACacheHit(
        string tempRoot, System.Reflection.Assembly cachedAssembly, out bool hydrated)
    {
        var fresh = new RadWorkspace(RadFixture.ModuleName, tempRoot);
        AlObjectResolution.RegisterGeneration(fresh, cachedAssembly);
        fresh.Generations.Add(cachedAssembly);
        hydrated = RadBaselineSidecar.TryHydrate(
            fresh, [tempRoot], EnvelopePath(tempRoot), SymbolsPath(tempRoot));
        return fresh;
    }

    // ── the premise: a serialized baseline is the same baseline ────────────────────────

    /// <summary>
    /// The design rests on one claim: the full-compile symbol baseline — built by BC's
    /// <c>SerializableSymbolModelConverter</c> — survives a write/read round trip through
    /// <c>SymbolReferenceJsonWriter</c> intact. The delta path already round-trips a MERGED
    /// baseline that way on every cycle (see <c>MergeRadBaseline</c>), but the full-compile
    /// one comes from a different producer and had never been round-tripped.
    ///
    /// <para>Asserted at the representation level, by re-serializing both: if the two writes
    /// are byte-identical then nothing the writer can express was lost. A behavioural check
    /// alone could pass while quietly dropping something no fixture object happens to use.</para>
    /// </summary>
    [SkippableFact]
    public void HydratingAWorkspace_RestoresTheExactSymbolBaseline()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);

            Assert.True(RadBaselineSidecar.TrySave(
                baseline.Workspace, EnvelopePath(tempRoot), SymbolsPath(tempRoot)));

            var fresh = HydrateLikeACacheHit(
                tempRoot, baseline.Types["Codeunit71000"].Assembly, out var hydrated);
            Assert.True(hydrated);
            Assert.True(fresh.HasBaseline);

            var fromCompile = File.ReadAllBytes(BcCompiler.WriteWorkspaceSymbols(
                baseline.Workspace, Path.Combine(tempRoot, "compiled.symbols.json")));
            var fromSidecar = File.ReadAllBytes(BcCompiler.WriteWorkspaceSymbols(
                fresh, Path.Combine(tempRoot, "hydrated.symbols.json")));

            Assert.Equal(fromCompile, fromSidecar);
            // Guard against both being empty/skeletal, which would satisfy Equal vacuously.
            Assert.Contains("RAD Perf Service", System.Text.Encoding.UTF8.GetString(fromCompile));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── the headline claim ────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole point: after a HIT, one edit costs one object — not the module. The second
    /// half of the assertion is what makes it a proof rather than a description. The SAME
    /// edited tree, given to a workspace that was NOT hydrated, re-emits all twenty; so a
    /// hydration that silently failed and fell back could not produce this result.
    /// </summary>
    [SkippableFact]
    public void HydratedWorkspace_DeltasTheFirstEdit_WhereAnUnhydratedOneRebuildsTheModule()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            Assert.True(RadBaselineSidecar.TrySave(
                baseline.Workspace, EnvelopePath(tempRoot), SymbolsPath(tempRoot)));
            var cached = baseline.Types["Codeunit71000"].Assembly;

            // Cycle 1 of a restarted watch: the tree has not moved, which is what makes the
            // cache HIT, so hydration happens against the tree the cached DLL was built from.
            var hydratedWs = HydrateLikeACacheHit(tempRoot, cached, out var hydrated);
            Assert.True(hydrated);

            // …and only THEN the edit a developer makes first: one procedure body.
            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "exit(40);", "exit(41);");

            var delta = new BcCompiler().EmitIncremental([tempRoot], RadFixture.ModuleName, hydratedWs);

            Assert.False(delta.FullRebuild, "a hydrated baseline must delta the first edit");
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal(["RAD Perf Service"], RadFixture.EmittedNames(delta));
            var modified = Assert.Single(delta.Changes.Modified);
            Assert.Equal(new RadObjectKey("Codeunit", 71000), modified.Key);

            var overlay = RadFixture.AssembleAndLoad(hydratedWs, delta.Emit.Sources);
            delta.Commit(hydratedWs, overlay);
            // Exactly one runtime object moved; the other nineteen are still the cached DLL's.
            Assert.Same(overlay, AlObjectResolution.FindOwned("Codeunit71000", null)?.Assembly);
            foreach (var (name, type) in baseline.Types)
                if (name != "Codeunit71000")
                    Assert.Same(type, AlObjectResolution.FindOwned(name, null));

            // The contrast: no sidecar, same tree, same edit — the whole module.
            var unhydrated = new RadWorkspace(RadFixture.ModuleName, tempRoot);
            var full = new BcCompiler().EmitIncremental([tempRoot], RadFixture.ModuleName, unhydrated);
            Assert.True(full.FullRebuild);
            Assert.Equal(RadFixture.ObjectCount, full.Emit.Sources.Count);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The reverse one-hop dependency graph is the map whose loss is most dangerous, because
    /// losing it reports SUCCESS: generated calls bake Microsoft's member ids, so a moved
    /// callable surface whose callers are not rebound leaves those callers executing against
    /// the previous contract while the cycle claims to have handled the edit.
    ///
    /// <para>Asserted on the caller's NAME, not on a count — a delta that emitted two
    /// arbitrary objects would satisfy a count.</para>
    /// </summary>
    [SkippableFact]
    public void HydratedWorkspace_RebindsTheDirectCaller_WhenACallableSurfaceMoves()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            Assert.True(RadBaselineSidecar.TrySave(
                baseline.Workspace, EnvelopePath(tempRoot), SymbolsPath(tempRoot)));
            var cached = baseline.Types["Codeunit71000"].Assembly;

            var hydratedWs = HydrateLikeACacheHit(tempRoot, cached, out var hydrated);
            Assert.True(hydrated);

            // RAD Perf Caller calls Service.Coerce; retyping the parameter moves its member id.
            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "procedure Coerce(Input: Integer): Integer",
                "procedure Coerce(Input: Decimal): Integer");

            var delta = new BcCompiler().EmitIncremental([tempRoot], RadFixture.ModuleName, hydratedWs);

            Assert.False(delta.FullRebuild);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal(["RAD Perf Caller", "RAD Perf Service"], RadFixture.EmittedNames(delta));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// A deletion is the object map's claim. Without it every declared object reads as an
    /// ADDITION and the deleted one is never noticed at all — its symbol survives in the
    /// baseline and its CLR type stays resolvable in the cached DLL, so a test can keep
    /// passing against a file the developer removed.
    /// </summary>
    [SkippableFact]
    public void HydratedWorkspace_ClassifiesADeletedFileAsARemoval_AndTombstonesItsType()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            Assert.True(RadBaselineSidecar.TrySave(
                baseline.Workspace, EnvelopePath(tempRoot), SymbolsPath(tempRoot)));
            var cached = baseline.Types["Codeunit71000"].Assembly;

            var hydratedWs = HydrateLikeACacheHit(tempRoot, cached, out var hydrated);
            Assert.True(hydrated);

            // Unrelated D is the one object nothing else references, so deleting it is legal.
            File.Delete(RadFixture.SourceFile(tempRoot, "RadPerfUnrelatedD.Codeunit.al"));

            var delta = new BcCompiler().EmitIncremental([tempRoot], RadFixture.ModuleName, hydratedWs);

            Assert.False(delta.FullRebuild);
            Assert.Empty(delta.Emit.Sources);          // a removal generates no C# at all
            Assert.Empty(delta.Changes.Modified);
            Assert.Empty(delta.Changes.Added);
            var removed = Assert.Single(delta.Changes.Removed);
            Assert.Equal(new RadObjectKey("Codeunit", 71005), removed.Key);

            delta.Commit(hydratedWs, assembly: null);
            Assert.True(AlObjectResolution.IsTombstoned("Codeunit71005"),
                "the deleted object must not resolve out of the still-loaded cached DLL");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The per-file declaration record, tested through the one case that can ONLY come from
    /// it: DELETING a <c>dotnet</c> package file. There is no file left to parse, so the fact
    /// that it used to declare a package — which every object in the module binds against —
    /// is recoverable only from what the previous compile remembered. Lose that and the
    /// deletion passes for a comment-only edit.
    /// </summary>
    [SkippableFact]
    public void HydratedWorkspace_StillForcesAFullCompile_WhenADotNetPackageFileIsDeleted()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);

            // Get a dotnet package into the COMMITTED baseline: adding one forces a full
            // compile, whose commit is what records the flag for that file.
            var packageFile = RadFixture.WriteDotNetPackageFile(tempRoot, "SidecarPackageProbe");
            var withPackage = baseline.Cycle(tempRoot);
            Assert.True(withPackage.FullRebuild);
            withPackage.Commit(
                baseline.Workspace,
                RadFixture.AssembleAndLoad(baseline.Workspace, withPackage.Emit.Sources));

            Assert.True(RadBaselineSidecar.TrySave(
                baseline.Workspace, EnvelopePath(tempRoot), SymbolsPath(tempRoot)));
            var cached = baseline.Workspace.Generations[^1];

            var hydratedWs = HydrateLikeACacheHit(tempRoot, cached, out var hydrated);
            Assert.True(hydrated);

            File.Delete(packageFile);
            RadCycleNotes.Drain();
            var cycle = new BcCompiler().EmitIncremental([tempRoot], RadFixture.ModuleName, hydratedWs);

            Assert.True(cycle.FullRebuild,
                "deleting a dotnet package declaration must rebuild the module, not delta");
            Assert.Contains(
                "declares a dotnet package",
                string.Join(" | ", RadCycleNotes.Drain()));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The AL-output cache key hashes the module name, the <c>--define</c> symbols, the
    /// resolved dependency ids and the <c>.al</c> contents — but NOT the app version,
    /// publisher or id. So a HIT can legitimately serve a tree whose <c>app.json</c> identity
    /// has moved since the sidecar was written, and a delta bound under the new identity
    /// against the old baseline is exactly the divergence the reference signature exists to
    /// catch. Hydration restores that signature so the very next cycle notices.
    /// </summary>
    [SkippableFact]
    public void HydratedWorkspace_ThenAnAppVersionChange_RebuildsTheModule_NamingTheFacet()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            SeededBaseline baseline;
            using (BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion))
            {
                baseline = RadFixture.Seed(tempRoot);
                Assert.True(RadBaselineSidecar.TrySave(
                    baseline.Workspace, EnvelopePath(tempRoot), SymbolsPath(tempRoot)));
            }

            // Hydration itself does not read the identity — it restores the signature the
            // baseline was built under and leaves the comparison to the next cycle's ArmFor.
            var hydratedWs = HydrateLikeACacheHit(
                tempRoot, baseline.Types["Codeunit71000"].Assembly, out var hydrated);
            Assert.True(hydrated, "the sidecar itself is valid — only the identity moves below");
            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "exit(40);", "exit(41);");

            using (BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, new Version(1, 0, 0, 1)))
            {
                RadCycleNotes.Drain();

                var cycle = new BcCompiler().EmitIncremental(
                    [tempRoot], RadFixture.ModuleName, hydratedWs);

                Assert.True(cycle.FullRebuild,
                    "a hydrated baseline built under another app version must not serve a delta");
                Assert.Contains(
                    "app.json changed the app version: 1.0.0.0 → 1.0.0.1",
                    string.Join(" | ", RadCycleNotes.Drain()));
                Assert.Equal(RadFixture.ObjectCount, cycle.Emit.Sources.Count);
            }
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── hydration fails closed ────────────────────────────────────────────────────────

    /// <summary>
    /// The substantive rejection. A cache HIT implies the tree matches the key, but the cache
    /// directory is shared and long-lived, so "the sidecar describes THIS tree" is verified
    /// rather than assumed: content hashes are compared file by file. Rejection must leave the
    /// workspace exactly as it was — no baseline, no half-restored object map — so the cycle
    /// takes the ordinary full-compile path.
    /// </summary>
    [SkippableFact]
    public void Hydration_IsRejected_WhenTheTreeMovedSinceTheSidecarWasWritten()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            Assert.True(RadBaselineSidecar.TrySave(
                baseline.Workspace, EnvelopePath(tempRoot), SymbolsPath(tempRoot)));

            // A file the sidecar has never seen: its hash map no longer describes this tree.
            File.WriteAllText(
                RadFixture.SourceFile(tempRoot, "RadPerfIntruder.Codeunit.al"),
                "namespace AlRunner.Tests.RadTwentyObject;\n\n"
                + "codeunit 71090 \"RAD Perf Intruder\"\n{\n    procedure V(): Integer\n"
                + "    begin\n        exit(1);\n    end;\n}\n");

            var fresh = HydrateLikeACacheHit(
                tempRoot, baseline.Types["Codeunit71000"].Assembly, out var hydrated);

            Assert.False(hydrated);
            Assert.False(fresh.HasBaseline);
            Assert.Empty(fresh.AllObjects());

            var cycle = new BcCompiler().EmitIncremental([tempRoot], RadFixture.ModuleName, fresh);
            Assert.True(cycle.FullRebuild);
            Assert.Equal(RadFixture.ObjectCount + 1, cycle.Emit.Sources.Count);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// A file edited in place — same path, different bytes — is the case a path-set comparison
    /// alone would miss, and the one that would bind a delta against symbols for source that
    /// is not on disk.
    /// </summary>
    [SkippableFact]
    public void Hydration_IsRejected_WhenAFileWasEditedInPlace()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            Assert.True(RadBaselineSidecar.TrySave(
                baseline.Workspace, EnvelopePath(tempRoot), SymbolsPath(tempRoot)));

            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "exit(40);", "exit(41);");

            _ = HydrateLikeACacheHit(
                tempRoot, baseline.Types["Codeunit71000"].Assembly, out var hydrated);

            Assert.False(hydrated);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [SkippableFact]
    public void Hydration_IsRejected_WhenTheSymbolsFileIsMissing()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            Assert.True(RadBaselineSidecar.TrySave(
                baseline.Workspace, EnvelopePath(tempRoot), SymbolsPath(tempRoot)));

            File.Delete(SymbolsPath(tempRoot));

            var fresh = HydrateLikeACacheHit(
                tempRoot, baseline.Types["Codeunit71000"].Assembly, out var hydrated);

            Assert.False(hydrated);
            Assert.False(fresh.HasBaseline);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// An envelope from a future or past runner must miss, not be reinterpreted. The AL-output
    /// cache key is deliberately NOT bumped for this sidecar — it is optional, so bumping would
    /// invalidate every existing entry for no gain — which means an older or newer envelope can
    /// legitimately be found beside a matching DLL. Its own schema field is what rejects it.
    /// </summary>
    [SkippableFact]
    public void Hydration_IsRejected_WhenTheEnvelopeSchemaIsUnknown()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            Assert.True(RadBaselineSidecar.TrySave(
                baseline.Workspace, EnvelopePath(tempRoot), SymbolsPath(tempRoot)));

            var envelope = EnvelopePath(tempRoot);
            File.WriteAllText(
                envelope,
                File.ReadAllText(envelope).Replace(
                    $"\"schema\":{RadBaselineSidecar.Schema}",
                    "\"schema\":999",
                    StringComparison.Ordinal));

            var fresh = HydrateLikeACacheHit(
                tempRoot, baseline.Types["Codeunit71000"].Assembly, out var hydrated);

            Assert.False(hydrated);
            Assert.False(fresh.HasBaseline);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// A workspace with no baseline has no delta-readiness to persist, and writing an envelope
    /// without one would produce an entry that always fails hydration — a file that looks like
    /// coverage and is not.
    /// </summary>
    [Fact]
    public void Saving_IsDeclined_WhenTheWorkspaceHasNoBaseline()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), ScenarioDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var empty = new RadWorkspace(RadFixture.ModuleName, tempRoot);

            Assert.False(RadBaselineSidecar.TrySave(
                empty, EnvelopePath(tempRoot), SymbolsPath(tempRoot)));
            Assert.False(File.Exists(EnvelopePath(tempRoot)));
            Assert.False(File.Exists(SymbolsPath(tempRoot)));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
