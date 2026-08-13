using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// Proves `--watch` re-runs IN-PROCESS and picks up a source edit on the next
/// cycle (the same-bundle reload working inside the resident watch process — the
/// gap that previously forced watch to spawn a child). We edit a table trigger so
/// the result flips PASS→FAIL on the second cycle; stale in-process state would
/// keep it passing.
///
/// Spawns the real runner; needs the BC artifact cache. Skips (no-op) when absent.
/// </summary>
[Collection("server-serial")]
public class WatchTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");
    private static readonly string FixtureSrc = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "RecordTriggerXRec"));

    // Ask the runner where its artifacts are rather than hardcoding one of the several
    // directories they can live in. Probing ~/.bcartifacts.cache/sandbox directly made
    // both tests in this file skip silently on any machine provisioned into
    // ~/.local/share/al-runner/artifacts — which is the default the runner itself uses.
    private static bool ArtifactsPresent()
    {
        try { return Directory.Exists(AlRunner.Infrastructure.BcArtifacts.ServiceTierDir); }
        catch { return false; }
    }

    private static string CurrentFramework()
    {
        var v = Environment.Version;
        return $"net{v.Major}.{v.Minor}";
    }

    /// <summary>
    /// Starting a watch on an unchanged tree must cost a load, not a whole-module compile.
    /// Delta compilation needs a compiler symbol baseline that a cached DLL cannot supply,
    /// so the temptation is to skip the cache entirely and compile the module up front —
    /// which on a large app is minutes of waiting before the developer sees a single test
    /// result. The contract instead is: serve cycle 1 from the cache, and let the FIRST
    /// EDIT pay for the baseline.
    ///
    /// The edit half is the part that makes this test prove something. A cache HIT that
    /// also served the second cycle would look identical here on timing alone and be
    /// completely wrong — the developer's change would simply not run.
    /// </summary>
    [Fact]
    public async Task Watch_FirstCycle_ServesTheCache_AndTheFirstEditBuildsTheBaseline()
    {
        if (!ArtifactsPresent()) { Console.Error.WriteLine("[skip] BC artifact cache not present"); return; }

        var bundle = Path.Combine(Path.GetTempPath(), "al-runner-watch-cache", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundle);
        foreach (var f in Directory.GetFiles(FixtureSrc))
            File.Copy(f, Path.Combine(bundle, Path.GetFileName(f)));
        var tablePath = Path.Combine(bundle, "XRecProbe.Table.al");
        var cacheDir = Path.Combine(bundle, ".cache");

        // Prime the cache with a one-shot run over the identical tree.
        var prime = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                + $" \"{bundle}\" --cache \"{cacheDir}\"",
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        using (var p0 = Process.Start(prime)!)
        {
            var so = p0.StandardOutput.ReadToEndAsync();
            var se = p0.StandardError.ReadToEndAsync();
            await p0.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(240));
            var primed = (await so) + (await se);
            Assert.True(p0.ExitCode == 0, primed);
            Assert.Contains("[cache] WROTE", primed);
        }

        var lines = new List<string>();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                + $" \"{bundle}\" --watch --cache \"{cacheDir}\"",
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        using var p = Process.Start(psi)!;
        void Pump(StreamReader r) => Task.Run(async () =>
        {
            string? l;
            while ((l = await r.ReadLineAsync()) != null) lock (lines) lines.Add(l);
        });
        Pump(p.StandardOutput);
        Pump(p.StandardError);

        async Task<int> Marker(int fromIndex, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (lines)
                    for (int i = fromIndex; i < lines.Count; i++)
                        if (lines[i].Contains("[watch] waiting for AL source")) return i;
                await Task.Delay(200);
            }
            string dump; lock (lines) dump = string.Join("\n", lines.TakeLast(40));
            throw new TimeoutException($"watch marker not seen.\n--- last output ---\n{dump}");
        }
        string Segment(int from, int to)
        {
            lock (lines) return string.Join("\n", lines.GetRange(from, Math.Max(0, to - from)));
        }

        try
        {
            int m1 = await Marker(0, TimeSpan.FromSeconds(180));
            var cycle1 = Segment(0, m1);
            Assert.Contains("[cache] HIT", cycle1);
            Assert.DoesNotContain("baseline built", cycle1);
            Assert.Contains("PASS", cycle1);

            // The same edit the sibling test uses: the fixture asserts '1', so a cycle that
            // really recompiled and reloaded the table now FAILS. A second cache HIT would
            // keep it green while silently ignoring the developer.
            var table = await File.ReadAllTextAsync(tablePath);
            var edited = table.Replace("xRec.\"Counter\" + 1", "xRec.\"Counter\" + 9");
            Assert.NotEqual(table, edited);
            await File.WriteAllTextAsync(tablePath, edited);

            int m2 = await Marker(m1 + 1, TimeSpan.FromSeconds(240));
            var cycle2 = Segment(m1 + 1, m2);
            Assert.DoesNotContain("[cache] HIT", cycle2);
            Assert.Contains("baseline built", cycle2);
            Assert.Contains("FAIL", cycle2);
        }
        finally
        {
            try { p.Kill(true); } catch { }
        }
    }

    [Fact]
    public async Task Watch_PicksUpEdit_InProcess_OnNextCycle()
    {
        if (!ArtifactsPresent()) { Console.Error.WriteLine("[skip] BC artifact cache not present"); return; }

        var bundle = Path.Combine(Path.GetTempPath(), "al-runner-watch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundle);
        foreach (var f in Directory.GetFiles(FixtureSrc))
            File.Copy(f, Path.Combine(bundle, Path.GetFileName(f)));
        var tablePath = Path.Combine(bundle, "XRecProbe.Table.al");
        var manifestPath = Path.Combine(bundle, "app.json");
        var cacheDir = Path.Combine(bundle, ".cache");

        var lines = new List<string>();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                + $" \"{bundle}\" --watch --cache \"{cacheDir}\"",
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        psi.Environment["BCCOMPILER_TIMING"] = "1"; // emit GetSharedReferences timing lines
        using var p = Process.Start(psi)!;
        void Pump(StreamReader r) => Task.Run(async () =>
        {
            string? l;
            while ((l = await r.ReadLineAsync()) != null) lock (lines) lines.Add(l);
        });
        Pump(p.StandardOutput);
        Pump(p.StandardError);

        async Task<int> WaitForMarkerAfter(int fromIndex, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (lines)
                    for (int i = fromIndex; i < lines.Count; i++)
                        if (lines[i].Contains("[watch] waiting for AL source")) return i;
                await Task.Delay(200);
            }
            string dump; lock (lines) dump = string.Join("\n", lines.TakeLast(40));
            throw new TimeoutException($"watch marker not seen.\n--- last output ---\n{dump}");
        }

        string Segment(int from, int to)
        {
            lock (lines) return string.Join("\n", lines.GetRange(from, Math.Max(0, to - from)));
        }

        try
        {
            // Cycle 1 (cold): the fixture test passes (Counter 0 -> 1, asserts '1').
            int m1 = await WaitForMarkerAfter(0, TimeSpan.FromSeconds(150));
            var cycle1 = Segment(0, m1);
            Assert.Contains("PASS", cycle1);
            Assert.Contains("Insert_OnInsertReadsXRec_BuildsConcreteBeforeImage", cycle1);
            Assert.DoesNotContain("FAIL  Codeunit", cycle1);

            // Edit ONLY the table trigger (+1 -> +9). The test still asserts '1', so
            // the next cycle MUST now FAIL — proving the edited table was reloaded
            // in-process (stale state would keep it passing).
            var table = await File.ReadAllTextAsync(tablePath);
            var edited = table.Replace("xRec.\"Counter\" + 1", "xRec.\"Counter\" + 9");
            Assert.NotEqual(table, edited);
            await File.WriteAllTextAsync(tablePath, edited);

            // Cycle 2 (warm, after the edit).
            //
            // This budget is only "did the cycle finish at all" — it is NOT the warm-vs-cold
            // claim, which the GetSharedReferences timing assertion below makes on its own. So
            // it should be generous: at 60s this was the single flaky test in the suite, passing
            // in 57s when run alone and timing out when the full 193-test suite had just driven
            // the machine through several BC engine boots. A cycle that has genuinely gone cold
            // still fails loudly on the <5s assertion below, so a longer wait here costs nothing
            // and removes a false red that trains people to re-run and shrug.
            int m2 = await WaitForMarkerAfter(m1 + 1, TimeSpan.FromSeconds(240));
            var cycle2 = Segment(m1 + 1, m2);
            Assert.Contains("FAIL", cycle2);
            Assert.Contains("Insert_OnInsertReadsXRec_BuildsConcreteBeforeImage", cycle2);
            Assert.Contains("[rad] Runner Tests Fixture - Record Trigger xRec: delta +0 ~1 -0", cycle2);
            Assert.Contains("→ 1 object(s) re-emitted", cycle2);
            Assert.DoesNotContain("[rad] Runner Tests Fixture - Record Trigger xRec: baseline built", cycle2);
            // The dependency loader stayed warm in-process across the edit: the
            // re-emit's symbol load is near-instant, not a cold ~40s reload.
            //
            // Assert the MAGNITUDE, not the exact string. This used to be
            // Assert.Contains("0ms"), which demanded the step round to exactly zero
            // milliseconds — on a Release build or a loaded machine it reports "1ms"
            // and the test failed despite the warm path working perfectly. The real
            // claim is warm (milliseconds) vs cold (tens of seconds), so a generous
            // ceiling still fails loudly if the loader ever goes cold here.
            Assert.Contains("GetSharedReferences", cycle2);
            // The label carries a parenthetical, e.g.
            //   [emit-timing] GetSharedReferences (5 specs): 787ms
            var timing = System.Text.RegularExpressions.Regex.Match(
                cycle2, @"GetSharedReferences[^:]*:\s*(\d+)ms");
            Assert.True(timing.Success,
                $"expected an '[emit-timing] GetSharedReferences: <n>ms' line in cycle 2, got:\n{cycle2}");
            var elapsedMs = int.Parse(timing.Groups[1].Value);
            Assert.True(elapsedMs < 5_000,
                $"warm in-process symbol load took {elapsedMs}ms — a warm re-emit must not pay " +
                "the cold ~40s dependency reload. The loader did not stay warm across the edit.");

            // A manifest-only change invalidates the RAD baseline but does not change the
            // AL-source cache key. Once a generation is loaded, the workspace must force a
            // real baseline rebuild rather than resurrecting the pre-manifest cached DLL.
            var manifest = await File.ReadAllTextAsync(manifestPath);
            var versioned = manifest.Replace("\"version\": \"1.0.0.0\"", "\"version\": \"1.0.0.1\"");
            Assert.NotEqual(manifest, versioned);
            await File.WriteAllTextAsync(manifestPath, versioned);

            int m3 = await WaitForMarkerAfter(m2 + 1, TimeSpan.FromSeconds(240));
            var cycle3 = Segment(m2 + 1, m3);
            Assert.Contains("[rad] Runner Tests Fixture - Record Trigger xRec: baseline built", cycle3);
            Assert.DoesNotContain("[cache] HIT", cycle3);
        }
        finally
        {
            try { p.Kill(true); } catch { }
        }
    }
}
