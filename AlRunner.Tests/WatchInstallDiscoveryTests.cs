// WatchInstallDiscoveryTests — install-trigger seeding must survive a warm --watch cycle.
//
// The corpus witness (.context/npcore-nsfree-witness.log) shows a cold cycle and two warm
// cycles over the same unedited npcore bundle disagreeing about the run itself:
//
//   cold    2317 tests   432 pass   1885 fail
//   warm    2314 tests   415 pass   1899 fail
//
// Three tests DISAPPEAR and seventeen flip pass→fail, every warm cycle, deterministically —
// and the only other thing the warm cycles print that the cold one does not is
//
//   [install-trigger] Codeunit6014448.OnInstallAppPerCompany (NP Retail#…g1) threw:
//   NullReferenceException
//
// The two halves have DIFFERENT causes, and the fixture reproduces both.
//
//   The three vanished tests are the whole of the app that owns Codeunit6014448, NP Retail's
//   Subtype=Install codeunit. Its first statement raises the table-declared integration event
//   `"NPR POS Sales Workflow".OnDiscoverPOSSalesWorkflows`, whose subscribers write through
//   the publishing record (`Sender.DiscoverPOSSalesWorkflow`) — and the dispatcher passed that
//   sender as null, because it only recognised a NavCodeunitHandle sender and a table
//   publisher emits an INavRecordHandle. Cold never saw it: the publisher's static
//   γeventScope was still unarmed when the install trigger ran, so the event dispatched to
//   nobody. Warm saw it because that static survives a cycle. The trigger then threw out of
//   TestExecutor.Run before the test loop, Program recorded an EXEC-FAIL no summary line
//   reports, and the app group contributed zero results.
//
//   The seventeen flipped tests are all page-opening tests (nine reporting a raw
//   NullReferenceException from NCLMetaForm.GetFrozenPageDefinitionWithExtension…, the rest
//   reporting whatever their page trigger should have done). Their cause is separate:
//   ResetForReload drops _metaFormCache but kept _pagesWithRealMetadata, so on the next cycle
//   a brand-new skeleton NCLMetaForm was short-circuited as "already loaded" and TestPage
//   silently degraded to record-only access — OnOpenPage never ran.
//
// This fixture is both shapes with npcore's scale removed: a dependency app whose install
// trigger raises a table integration event, a subscriber that writes through the sender, a
// page whose OnOpenPage raises the same event, and tests in both the owning app and the
// depending app. The warm cycles are provoked the way the witness provokes them — a
// comment-only edit to a file in the OTHER app, so the app under test is untouched and must
// behave identically to the cold cycle.

using System.Diagnostics;
using Xunit;

namespace AlRunner.Tests;

[Collection("server-serial")]
public class WatchInstallDiscoveryTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");
    private static readonly string FixtureSrc = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "WatchInstallDiscovery"));

    private const string TestsFile = "Tests/src/DiscoveryTests.Codeunit.al";
    private const string ControlAnchor = "codeunit 60996 \"Watch Discovery Dep Tests\"";

    /// <summary>
    /// The results a healthy cycle reports, whichever cycle it is — in declaration order,
    /// owning app first (that is the order the runner walks app groups and test methods).
    /// </summary>
    private static readonly string[] Expected =
    [
        "PASS  Codeunit60994.InstallSeededTheOwningApp",
        "PASS  Codeunit60994.InstallDiscoveredNothingItWasNotToldTo",
        "PASS  Codeunit60996.OpeningTheListRediscoversDeletedEntries",
        "PASS  Codeunit60996.ExportingThroughTheXmlPortEmitsTheDiscoveredRows",
        "PASS  Codeunit60996.UndiscoveredEntryIsAbsentAndReportsNotFound",
    ];

    /// <summary>
    /// Three cycles over a bundle whose Lib app never changes must report the same tests with
    /// the same outcomes.
    ///
    /// <para><b>Why the whole result LIST and not a count.</b> The npcore witness drifted in
    /// two directions at once — three tests disappeared and seventeen flipped — and a count
    /// cannot tell those apart (there, 3 vanishing FAILs and 17 PASS→FAILs happened to net out
    /// to "3 fewer, 17 less green"). Comparing the ordered PASS/FAIL lines fails on either.</para>
    ///
    /// <para><b>Why three cycles and not two.</b> Every defect this pins is a stale-state
    /// defect, and stale state needs a cycle to accumulate: cycle 1 seeds the publisher's
    /// static γeventScope and marks page 60989 as metadata-loaded, and it is cycle 2 that
    /// consumes them. A two-cycle test proves cycle 2 recovered; the third proves the runner
    /// is not simply alternating.</para>
    ///
    /// <para><b>Both directions.</b> Positive: every named result, asserted by value.
    /// Negative: no <c>[install-trigger] … threw</c> line and no <c>EXEC-FAIL</c> in any
    /// cycle. Those are the runner being loud about work it could not do
    /// (<c>.claude/rules/loud-failures.md</c>), so their presence is a defect report — and the
    /// EXEC-FAIL one is what silently deleted a whole app group's tests from the run.</para>
    ///
    /// <para><b>It discriminates.</b> Measured against the pre-fix runner, one defect at a
    /// time: reverting the arming call turns <c>InstallSeededTheOwningApp</c> FAIL on the COLD
    /// cycle and PASS on the warm ones; reverting the sender binding brings back
    /// <c>[install-trigger] Codeunit60992.OnInstallAppPerCompany … threw:
    /// NullReferenceException</c> and drops <c>Codeunit60994</c>'s whole app group from the
    /// warm cycles; reverting the page-metadata reset flips
    /// <c>OpeningTheListRediscoversDeletedEntries</c> PASS→FAIL on warm cycles only, because
    /// TestPage silently degrades to record-only access.</para>
    /// </summary>
    [SkippableFact]
    public async Task Watch_UnrelatedEdits_LeaveEveryCycleReportingTheSameResults()
    {
        TestArtifacts.SkipIfMissing();

        using var session = WatchSession.Start("al-runner-watch-install-discovery", FixtureSrc);

        // Cycle 1 (cold) — the reference answer, measured rather than assumed.
        var cold = await session.NextCycleAsync();
        AssertHealthyCycle("cold", cold);

        // Cycles 2 and 3 (warm) — comment-only edits to the DEPENDING app. The Lib app that
        // owns the install codeunit, the discovery event and the page declares nothing new and
        // is reported "unchanged — reusing the loaded module", so its answers must not move.
        for (int cycle = 2; cycle <= 3; cycle++)
        {
            session.Edit(TestsFile, ControlAnchor, $"// watch control {cycle}\n{ControlAnchor}");
            AssertHealthyCycle($"warm cycle {cycle}", await session.NextCycleAsync());
        }
    }

    private static void AssertHealthyCycle(string label, string output)
    {
        Assert.False(output.Contains("[install-trigger]", StringComparison.Ordinal),
            $"{label}: an install trigger failed — {Tail(output)}");
        Assert.False(output.Contains("EXEC-FAIL", StringComparison.Ordinal),
            $"{label}: an app group contributed no tests — {Tail(output)}");

        // Strip the "(12ms)" suffix: the outcome and the order are the claim, the duration is not.
        var reported = output.Split(Environment.NewLine)
            .Where(l => l.StartsWith("PASS  ", StringComparison.Ordinal)
                     || l.StartsWith("FAIL  ", StringComparison.Ordinal))
            .Select(l => l.LastIndexOf(" (", StringComparison.Ordinal) is var cut && cut > 0 ? l[..cut] : l)
            .ToArray();
        Assert.True(Expected.SequenceEqual(reported),
            $"{label} reported a different run:{Environment.NewLine}"
            + $"  expected: {string.Join(" | ", Expected)}{Environment.NewLine}"
            + $"  actual:   {string.Join(" | ", reported)}");
    }

    private static string Tail(string output) =>
        string.Join(Environment.NewLine, output.Split(Environment.NewLine).TakeLast(25));

    private sealed class WatchSession : IDisposable
    {
        private const string Marker = "[watch] waiting for AL source";

        private readonly Process _process;
        private readonly List<string> _lines = new();
        private readonly string _bundle;
        private int _cursor;

        private WatchSession(string bundle, Process process)
        {
            _bundle = bundle;
            _process = process;
        }

        internal static WatchSession Start(string scenarioDir, string fixtureSrc)
        {
            var bundle = Path.Combine(Path.GetTempPath(), scenarioDir, Guid.NewGuid().ToString("N"));
            CopyTree(fixtureSrc, bundle);

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                // --no-cache: a cached whole-module DLL carries no compiler symbol baseline,
                // so RAD would spend cycle 2 establishing one and cycle 2 would not be the
                // warm cycle this test is about.
                Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                    + $" \"{bundle}\" --watch --no-cache",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = RepoRoot,
            };
            var session = new WatchSession(bundle, Process.Start(psi)!);
            session.Pump(session._process.StandardOutput);
            session.Pump(session._process.StandardError);
            return session;
        }

        private void Pump(StreamReader reader) => Task.Run(async () =>
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
                lock (_lines) _lines.Add(line);
        });

        /// <summary>Everything the runner printed up to the end of the next cycle.</summary>
        internal async Task<string> NextCycleAsync()
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(240);
            while (DateTime.UtcNow < deadline)
            {
                lock (_lines)
                    for (int i = _cursor; i < _lines.Count; i++)
                        if (_lines[i].Contains(Marker, StringComparison.Ordinal))
                        {
                            var cycle = string.Join(
                                Environment.NewLine, _lines.GetRange(_cursor, i - _cursor));
                            _cursor = i + 1;
                            return cycle;
                        }
                await Task.Delay(200);
            }
            string tail;
            lock (_lines) tail = string.Join(Environment.NewLine, _lines.TakeLast(60));
            throw new TimeoutException(
                $"watch cycle did not finish.{Environment.NewLine}--- last output ---{Environment.NewLine}{tail}");
        }

        internal void Edit(string relativePath, string before, string after)
        {
            var path = Path.Combine(_bundle, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var source = File.ReadAllText(path);
            Assert.Equal(1, source.Split(before, StringSplitOptions.None).Length - 1);
            File.WriteAllText(path, source.Replace(before, after, StringComparison.Ordinal));
        }

        public void Dispose()
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
            _process.Dispose();
            try { Directory.Delete(_bundle, recursive: true); } catch { }
        }

        /// <summary>
        /// Copy a tree without the live watcher noticing — see the identical remark on
        /// <c>RadSameAppOverloadWatchTests</c>: <see cref="File.Copy(string,string,bool)"/>
        /// clones on macOS/APFS and touches the SOURCE inode, which FSEvents reports as a
        /// change, so a spurious cycle lands between the cycle under test and the next edit.
        /// </summary>
        private static void CopyTree(string from, string to)
        {
            foreach (var source in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(to, Path.GetRelativePath(from, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var reader = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var writer = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                reader.CopyTo(writer);
            }
        }
    }
}
