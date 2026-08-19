// AlCacheGateDeadWorkTests — the two O(whole tree) questions asked only about a cache entry
// must not be answered when there is no cache entry to ask about.
//
// `GetOrderedDepIds` and `BcCompiler.BundleDeclaresQuery` both feed exactly one consumer each,
// and both consumers sit behind the AL-output cache gate
// (`needCompile && alCacheDir != null && radWs is null or { Generations.Count: 0 }`). They used
// to run unconditionally, ahead of that gate:
//
//   * GetOrderedDepIds builds a second DependencyResolver index — `EnsureIndexed` is an
//     instance field, so it re-walks every package-cache dir and re-reads every .app's
//     manifest out of its zip, with nothing carried over from the previous cycle.
//   * BundleDeclaresQuery decides whether a cache HIT also needs the query-symbols sidecar.
//     An app that HAS a query answers on the first file it reads; it is the app with NO query
//     that reads every .al file in the tree to prove a negative — 12.7 MB on the npcore
//     corpus, the overwhelmingly common case, on every cycle.
//
// The gate is closed on two occasions that matter: a warm `--watch` delta cycle from the second
// one onward (the app owns a loaded generation, so an entry must never resurrect the pre-edit
// DLL over it), and any run with `--no-cache`. This suite uses `--no-cache` because it is the
// one a single process can state in one invocation — a warm delta cycle needs a scripted watch
// session, and would prove the same gate.
//
// WHY BOTH DIRECTIONS ARE HERE. "The mark is absent" passes trivially against a runner that
// never emits it, a misspelled needle, or a bundle that failed before reaching the loop. So the
// same needles are asserted PRESENT on a cached run of the same fixture in the same suite. That
// pairing is what makes the absence evidence: the instrument is shown working, then shown
// silent, and only the gate differs between the two.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AlRunner.Tests;

public sealed class AlCacheGateDeadWorkTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    /// <summary>
    /// A real 20-object bundle that DECLARES A QUERY — which is why this fixture and not a
    /// smaller one. `bundleDeclaresQuery` has a second reader in the sidecar-replay block, and
    /// that block is outside the gate lexically; it is reachable only because `cachedBytes` is
    /// assigned nowhere but inside the gate. A query-declaring bundle is what makes the HIT leg
    /// below able to check that reachability argument rather than restate it: a HIT here really
    /// does take the `RegisterBundleQuerySymbolsJson` path.
    /// </summary>
    private static readonly string Fixture = Path.Combine(
        RepoRoot, "AlRunner.Tests", "Fixtures", "RadTwentyObject");

    /// <summary>The `PhaseLog.AppStage` name wrapping the dependency-id resolution.</summary>
    private const string OrderedDepIdsStage = "ordered-dep-ids";

    /// <summary>The `AppMark` label the query probe reports under BCCOMPILER_TIMING=1.</summary>
    private const string QueryProbeMark = "BundleDeclaresQuery";

    private readonly string _root;
    private readonly string _cacheDir;
    private readonly string _cachedLog;
    private readonly string _hitLog;
    private readonly string _noCacheLog;

    public AlCacheGateDeadWorkTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "al-runner-cache-gate", Guid.NewGuid().ToString("N"));
        // A private cache dir, so the cached leg's behaviour does not depend on what a previous
        // run of anything left in ~/.cache/al-runner, and so this suite writes nothing there.
        _cacheDir = Path.Combine(_root, "al-out");
        _cachedLog = Path.Combine(_root, "cached.jsonl");
        _hitLog = Path.Combine(_root, "hit.jsonl");
        _noCacheLog = Path.Combine(_root, "nocache.jsonl");
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [SkippableFact]
    public void WithTheCacheOn_BothCacheKeyProbesRun()
    {
        TestArtifacts.SkipIfMissing();

        var (output, exit) = Run(_cachedLog, $"--cache \"{_cacheDir}\"");

        Assert.Equal(0, exit);
        Assert.Contains(QueryProbeMark, output);
        Assert.Contains(OrderedDepIdsStage, AppStageNames(_cachedLog));

        // The gate was open, so it computed a key and wrote an entry. Asserted because the HIT
        // leg depends on it, and "MISS then no WROTE" would make that leg silently test nothing.
        Assert.Contains("[cache] WROTE", output);
        // And the query fixture got its query-symbols sidecar, which is the artifact the second
        // reader of bundleDeclaresQuery exists to register on the way back in. Its presence is
        // also what proves the probe answered TRUE for this fixture — a bundle the probe reported
        // as query-free would have written no such file, and the HIT leg would prove nothing.
        Assert.Single(Directory.GetFiles(_cacheDir, "*.query-symbols.json"));
    }

    /// <summary>
    /// The second reader's path, walked for real. `bundleDeclaresQuery` is declared outside the
    /// gate and assigned inside it; the sidecar-replay block that reads it again sits outside.
    /// If that block were reachable with the initialiser still in place it would read
    /// <c>false</c> for a bundle that DOES declare a query, and a HIT would skip
    /// <c>RegisterBundleQuerySymbolsJson</c> — leaving <c>NCLMetaQuery</c> null so that every
    /// query <c>Find</c> NREs inside BC's <c>NavQuery.ValidateTablesNotVirtual</c>.
    ///
    /// <para>So the assertion is not merely that a HIT happened: it is that the probe RAN on the
    /// run that took the HIT. That distinguishes "computed inside the gate, as intended" from
    /// "skipped, and the replay block read the initialiser".</para>
    /// </summary>
    [SkippableFact]
    public void ACacheHit_StillComputesTheQueryProbeItsSidecarReplayReads()
    {
        TestArtifacts.SkipIfMissing();

        // Populate the entry, then hit it. Same cache dir, same fixture, same args.
        var (first, firstExit) = Run(_cachedLog, $"--cache \"{_cacheDir}\"");
        Assert.Equal(0, firstExit);
        Assert.Contains("[cache] WROTE", first);

        var (second, secondExit) = Run(_hitLog, $"--cache \"{_cacheDir}\"");

        Assert.Equal(0, secondExit);
        Assert.Contains("[cache] HIT", second);
        Assert.Contains(QueryProbeMark, second);
        Assert.Contains(OrderedDepIdsStage, AppStageNames(_hitLog));
    }

    /// <summary>
    /// The same fixture, the same instrument, one flag different — and both probes go quiet.
    /// </summary>
    [SkippableFact]
    public void WithNoCache_NeitherCacheKeyProbeRuns()
    {
        TestArtifacts.SkipIfMissing();

        var (output, exit) = Run(_noCacheLog, "--no-cache");

        // The run has to have SUCCEEDED for its silence to mean anything. A bundle that
        // exploded before the app-group loop would also emit neither probe.
        Assert.Equal(0, exit);

        Assert.DoesNotContain(QueryProbeMark, output);
        Assert.DoesNotContain(OrderedDepIdsStage, AppStageNames(_noCacheLog));

        // Proof the run really did the work whose cache key these two feed: it compiled and
        // ran the fixture rather than short-circuiting somewhere above the loop.
        Assert.Contains("[emit-timing]", output);
    }

    /// <summary>
    /// Every stage name recorded on the run's APP rows. App rather than bundle because the
    /// deferred resolution happens inside an app group and is charged there — see the
    /// <c>AppStage</c> note at the <c>orderedDepIds</c> declaration for why a bundle stage would
    /// double-count it.
    ///
    /// <para>The stage key is absent from the row entirely when the stage never ran —
    /// <c>PhaseLog</c> writes no zero-valued placeholder — so membership is the assertion, not
    /// the value.</para>
    /// </summary>
    private static IReadOnlyList<string> AppStageNames(string phaseLogPath)
    {
        Assert.True(File.Exists(phaseLogPath),
            $"the runner wrote no phase log to {phaseLogPath}, so neither leg of this suite can "
            + "distinguish 'the stage did not run' from 'nothing was measured at all'");

        var names = new List<string>();
        var appRows = 0;
        foreach (var line in File.ReadAllLines(phaseLogPath))
        {
            if (line.Length == 0) continue;
            var row = JsonDocument.Parse(line).RootElement;
            if (row.GetProperty("kind").GetString() != "app") continue;
            appRows++;
            if (!row.TryGetProperty("stages", out var stages)) continue;
            foreach (var stage in stages.EnumerateObject()) names.Add(stage.Name);
        }
        // Without this, a run that emitted no app rows at all would satisfy every
        // DoesNotContain below for the wrong reason.
        Assert.True(appRows > 0, $"the phase log at {phaseLogPath} holds no app rows");
        return names;
    }

    private static (string Output, int Exit) Run(string phaseLogPath, string extraArgs)
    {
        var args = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
        args.Append(TestBuildConfig.BcVersionArg);
        var platformApps = Path.Combine(TestArtifacts.HomeDir() ?? "", ".al-runner", "platform-apps");
        if (Directory.Exists(platformApps)) args.Append($" --package-cache \"{platformApps}\"");
        args.Append(' ').Append(extraArgs);
        args.Append($" \"{Fixture}\"");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot,
        };
        psi.Environment["AL_RUNNER_PHASE_LOG"] = phaseLogPath;
        // The per-app marks exist only under this switch, and `BundleDeclaresQuery` is one of
        // them — without it the negative leg would assert the absence of a line the runner was
        // never going to print, and would pass however the gate behaved.
        psi.Environment["BCCOMPILER_TIMING"] = "1";
        // Log.cs installs a FilteredWriter that drops `[Component]`-tagged lines at default
        // verbosity. The emit-timing marks go to stderr through Console.Error and survive, but
        // verbose keeps the rest of the run's output legible when an assertion fails.
        psi.Environment["AL_RUNNER_VERBOSE"] = "1";

        var sb = new StringBuilder();
        using var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(300_000)) { try { p.Kill(true); } catch { } throw new TimeoutException("runner hung"); }
        p.WaitForExit();
        lock (sb) return (sb.ToString(), p.ExitCode);
    }
}
