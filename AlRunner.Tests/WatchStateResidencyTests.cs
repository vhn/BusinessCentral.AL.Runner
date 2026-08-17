using System.Diagnostics;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// A test execution must start from the same state the previous one started from,
/// whether "previous" means an earlier test codeunit in the same run or an earlier
/// <c>--watch</c> cycle in the same resident process. Anything parked in process-wide
/// state and not ended at the isolation boundary is silently shared between runs a
/// developer reads as independent — the shape behind "passes cold, fails warm" reports.
///
/// The fixture (Fixtures/WatchStateResidency) leaves three kinds of state behind on
/// purpose and then checks for all three on the way in:
///
///   1. <b>Manual event bindings</b> — an instance bound with <c>BindSubscription</c>,
///      which BC records in <c>Session.EventBindings</c> and removes as the bound
///      instance's tree is disposed. Bound twice, from two different owners:
///      <list type="bullet">
///        <item>a global of the TEST codeunit — already correct, because TestExecutor
///              disposes the test codeunit at the end of its run;</item>
///        <item>a global of a <b>SingleInstance</b> codeunit — the defect. Those are not
///              disposed with the test codeunit; they are cached and reset by
///              <c>BcRuntime.ResetSingleInstanceCache</c>, which only DROPS its dictionary
///              entries. The instances stay rooted in the session's own tree, so
///              forgetting the pointer never ended their life, and the subscriber they
///              had bound stayed live for the whole process.
///              <c>BcRuntime.ClearManualEventBindings</c> is what ends it now.</item>
///      </list>
///   2. <b>SingleInstance codeunit field state</b> — same reset, same cache.
///   3. <b>Committed table content</b> — <c>RecordPatches.RestoreInstallBaseline</c>.
///
/// (3) was already correct and is asserted so removing that sweep fails a test rather
/// than quietly changing what "a fresh test" means. (2) reads as correct either way,
/// which is exactly why (1)'s SingleInstance half is the load-bearing probe: a reset
/// that hands out a fresh instance zeroes the fields while leaving the old instance —
/// and its binding — alive, and only the binding notices.
///
/// The AL test's own closing block proves the probe can see each kind of state while it
/// IS live, so a gutted runtime cannot pass this by making all of it unobservable.
///
/// Spawns the real runner; needs the BC artifact cache. Skips (no-op) when absent.
/// </summary>
[Collection("server-serial")]
public class WatchStateResidencyTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");
    private static readonly string FixtureSrc = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "WatchStateResidency"));

    // Two test codeunits running the same body. Whichever runs second is the one asking
    // about the per-codeunit boundary inside one cycle; both ask about the cycle boundary
    // from cycle 2 on. Their order is not fixed, so both are asserted.
    private static readonly string[] TestIds =
    {
        "Codeunit60985.NoStateSurvivesAnEarlierExecution_A",
        "Codeunit60987.NoStateSurvivesAnEarlierExecution_B",
    };

    [SkippableFact]
    public async Task Watch_ReRunningTheSameTest_SeesNoStateFromThePreviousCycle()
    {
        TestArtifacts.SkipIfMissing();

        var bundle = Path.Combine(Path.GetTempPath(), "al-runner-watch-residency", Guid.NewGuid().ToString("N"));
        CopyTree(FixtureSrc, bundle);
        // Edits land in the LIBRARY app, never in the app that owns the subscriber and
        // the test. Re-emitting the subscriber's own app supersedes the leaked instance's
        // CLR type, which stops it matching the freshly-registered subscription — the
        // leak is then invisible and the test measures nothing. Verified: with the edit
        // pointed at the test app instead, all three cycles pass even with the bug
        // present. See the fixture's own header.
        var editTarget = Path.Combine(bundle, "Lib", "src", "ResidencyLib.Codeunit.al");

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

        string Segment(int from, int to)
        {
            lock (lines) return string.Join("\n", lines.GetRange(from, Math.Max(0, to - from)));
        }

        void AssertCycleIsClean(string cycle, int cycleNumber)
        {
            // Asserted separately from the PASS check, and with the cycle's own output in
            // the message, so a failure says WHICH kind of state leaked in WHICH cycle
            // instead of just "the test failed".
            Assert.False(cycle.Contains("RESIDENCY-LEAK"),
                $"cycle {cycleNumber} saw state left behind by an earlier execution.\n--- cycle {cycleNumber} ---\n{cycle}");
            // If this fires the cycle proved nothing: the probe half of the AL test cannot
            // observe live state, so its "everything is clean" verdict is vacuous.
            Assert.False(cycle.Contains("RESIDENCY-PROBE-BROKEN"),
                $"cycle {cycleNumber} could not observe state it had just created, so its leak probe is meaningless.\n--- cycle {cycleNumber} ---\n{cycle}");
            foreach (var id in TestIds)
            {
                Assert.False(cycle.Contains($"FAIL  {id}"),
                    $"cycle {cycleNumber} failed {id} for a reason other than residency.\n--- cycle {cycleNumber} ---\n{cycle}");
                Assert.True(cycle.Contains($"PASS  {id}"),
                    $"cycle {cycleNumber} never ran {id} at all.\n--- cycle {cycleNumber} ---\n{cycle}");
            }
        }

        try
        {
            // Cycle 1 (cold). Nothing has run before it, so its probe half is trivially
            // satisfied — what matters is that it PASSES, which establishes the test is
            // green when state genuinely is fresh. It then leaves a bound subscriber, a
            // bumped SingleInstance codeunit and a committed row behind.
            int m1 = await WaitForMarkerAfter(0, TimeSpan.FromSeconds(240));
            var cycle1 = Segment(0, m1);
            AssertCycleIsClean(cycle1, 1);

            // Cycle 2 (warm). Same resident process, same test, re-run after an edit to
            // the OTHER app. This is the cycle the bug lived in.
            await File.AppendAllTextAsync(editTarget, "\n// residency probe: cycle 2\n");
            int m2 = await WaitForMarkerAfter(m1 + 1, TimeSpan.FromSeconds(240));
            var cycle2 = Segment(m1 + 1, m2);
            AssertCycleIsClean(cycle2, 2);

            // Cycle 3. Two warm cycles, not one: the leak accumulated one bound instance
            // per execution, so a fix that only dropped the most recent leftovers would
            // still pass with cycle 2 alone asserted.
            await File.AppendAllTextAsync(editTarget, "\n// residency probe: cycle 3\n");
            int m3 = await WaitForMarkerAfter(m2 + 1, TimeSpan.FromSeconds(240));
            var cycle3 = Segment(m2 + 1, m3);
            AssertCycleIsClean(cycle3, 3);
        }
        finally
        {
            try { p.Kill(true); } catch { }
            try { Directory.Delete(bundle, recursive: true); } catch { }
        }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to), overwrite: true);
    }
}
