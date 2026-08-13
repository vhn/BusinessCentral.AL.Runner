// RadBulkSwitchWatchTests — what `--watch` does when a whole version of the tree lands at
// once instead of one save at a time.
//
// The scenario is the one every developer hits and no single-file test covers: a branch
// switch. One command rewrites, adds and deletes dozens of files, and — this is the part
// that matters — it takes SECONDS, not an instant. From the FileSystemWatcher's point of
// view that is a stream of events spread over a window, with the tree in a mixed state for
// the whole of it.
//
// Two claims, and they are about different failures:
//
//   1. ONE cycle, not several. A burst is one change. Firing per-event, or firing once and
//      then again for the tail of the same burst, multiplies a real app's whole warm cycle
//      by however many times the runner woke up.
//   2. The cycle sees the SETTLED tree. Starting a compile while the tree is still being
//      rewritten reads a half-applied mixture of the two versions — which is not merely
//      wasted work, it produces a COMPILE FAIL or a red test from source that is perfectly
//      valid in both versions. A spurious red on branch switch is worse than a slow one:
//      it trains the developer to ignore the runner.
//
// The fixture (AlRunner.Tests/Fixtures/RadBulkSwitch) is built so a mixed tree cannot pass
// by accident. v2's "Bulk Switch Service" references an enum value that only v2's
// "Bulk Switch Status" declares, and the two files are written in the order a checkout
// would write them — Service before Status, alphabetically — so any compile that starts
// mid-burst binds a v2 consumer against a v1 enum and fails loudly. Every value the test
// codeunit asserts comes from a different modified file, so a mixture that somehow compiles
// still fails the assertions.

using System.Diagnostics;
using Xunit;

namespace AlRunner.Tests;

[Collection("server-serial")]
public class RadBulkSwitchWatchTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    /// <summary>
    /// How long the simulated checkout takes to write its files. It has to exceed the
    /// runner's debounce for the test to say anything — a burst that finishes inside the
    /// debounce window is coalesced by any implementation, correct or not.
    /// </summary>
    private static readonly TimeSpan WritePause = TimeSpan.FromMilliseconds(120);

    private static bool ArtifactsPresent()
    {
        try { return Directory.Exists(AlRunner.Infrastructure.BcArtifacts.ServiceTierDir); }
        catch { return false; }
    }

    [Fact]
    public async Task Watch_BulkVersionSwitch_RunsOneCycle_AgainstTheSettledTree()
    {
        if (!ArtifactsPresent()) { Console.Error.WriteLine("[skip] BC artifact cache not present"); return; }

        var bundle = Path.Combine(
            Path.GetTempPath(), "al-runner-bulk-switch-watch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundle);
        RadBulkSwitchDeltaTests.Mirror("v1", bundle);

        var lines = new List<string>();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                + $" \"{bundle}\" --watch --no-cache",
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

        int Count() { lock (lines) return lines.Count; }
        string Segment(int from, int to)
        {
            lock (lines) return string.Join("\n", lines.GetRange(from, Math.Max(0, to - from)));
        }

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
            string dump; lock (lines) dump = string.Join("\n", lines.TakeLast(60));
            throw new TimeoutException($"watch marker not seen.\n--- last output ---\n{dump}");
        }

        // Wait until the runner has produced no output at all for `quiet`. A burst that
        // triggers several cycles is still mid-flight when the first of them finishes, so
        // "saw a waiting marker" is not the same as "the runner is done" — only silence is.
        async Task SettleAsync(TimeSpan quiet, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            var lastCount = -1;
            var lastChange = DateTime.UtcNow;
            while (DateTime.UtcNow < deadline)
            {
                var now = Count();
                if (now != lastCount) { lastCount = now; lastChange = DateTime.UtcNow; }
                else if (DateTime.UtcNow - lastChange >= quiet) return;
                await Task.Delay(150);
            }
            throw new TimeoutException("the runner never went quiet after the bulk switch");
        }

        try
        {
            // Cycle 1: v1 compiles and its own assertions hold.
            int m1 = await WaitForMarkerAfter(0, TimeSpan.FromSeconds(240));
            var cycle1 = Segment(0, m1);
            Assert.Contains("PASS", cycle1);
            Assert.Contains("BulkValuesMatchTheCheckedOutVersion", cycle1);
            Assert.DoesNotContain("FAIL", cycle1);

            int burstStart = Count();

            // The checkout: mirror v2 onto the tree one file at a time, in the order the
            // filenames sort, with a pause between each. 12 files over ~1.4s.
            await MirrorSlowlyAsync("v2", bundle);

            await SettleAsync(quiet: TimeSpan.FromSeconds(8), timeout: TimeSpan.FromSeconds(300));
            var burst = Segment(burstStart, Count());

            // Claim 2 first — it is the one that makes a wrong answer visible. A cycle that
            // ran against a half-written tree binds v2's Service against v1's Status and
            // cannot compile. "FAIL" in caps covers all three shapes the runner prints it
            // in: the per-test line, "COMPILE FAIL" and "EXEC FAIL" (the summary's counters
            // are lowercase).
            Assert.DoesNotContain("error AL", burst);
            Assert.DoesNotContain("FAIL", burst);

            // v2's own assertions now hold — the new code really ran.
            Assert.Contains("PASS", burst);
            Assert.Contains("BulkValuesMatchTheCheckedOutVersion", burst);

            // Proportional: eight recompiled, two added, two gone — not the whole app.
            Assert.Contains("[watch] RAD Bulk Switch Fixture: delta +2 ~8 -2", burst);
            Assert.DoesNotContain("baseline built", burst);

            // Claim 1: the burst was ONE change, so it costs ONE cycle.
            var cycles = System.Text.RegularExpressions.Regex.Matches(
                burst, @"\[watch\] change detected").Count;
            Assert.True(cycles == 1,
                $"a single bulk switch triggered {cycles} watch cycles — each one pays the " +
                $"full warm-cycle cost. Output:\n{burst}");
        }
        finally
        {
            try { p.Kill(true); } catch { }
            try { Directory.Delete(bundle, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Mirror one fixture version onto <paramref name="destination"/> the way a checkout
    /// does: file by file, in sorted order, over a window rather than atomically. Deletions
    /// happen last, which is also what leaves the tree in its most inconsistent state
    /// mid-way through.
    /// </summary>
    private static async Task MirrorSlowlyAsync(string version, string destination)
    {
        var source = Path.Combine(RadBulkSwitchDeltaTests.FixtureRoot, version);
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory
                     .EnumerateFiles(source, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(source, file);
            wanted.Add(relative);
            var target = Path.Combine(destination, relative);
            var incoming = await File.ReadAllTextAsync(file);
            if (File.Exists(target) && await File.ReadAllTextAsync(target) == incoming) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, incoming);
            await Task.Delay(WritePause);
        }

        foreach (var file in Directory
                     .EnumerateFiles(destination, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            if (wanted.Contains(Path.GetRelativePath(destination, file))) continue;
            File.Delete(file);
            await Task.Delay(WritePause);
        }
    }
}
