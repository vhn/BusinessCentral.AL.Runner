using System.Diagnostics;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The `--watch` half of <see cref="CrossBundleModuleIdentityDedupTests"/>: an app and its
/// separate test app passed as two bundle args to ONE resident watch session — the shape the
/// README documents (`al-runner --watch <app> <app>.Test`).
///
/// <para>Why this file exists. The #1683 cross-bundle module-identity dedup — bundle 1
/// compiles the dep app, bundle 2 resolves the SAME app as a dependency and must reuse that
/// exact Assembly rather than compile a second one — was gated `!watchMode` when it landed,
/// because <c>DependencyLoader.RegisterLoaded</c> was then a first-wins <c>TryAdd</c> and
/// registering under watch would have pinned cycle 1's pre-edit module forever. #1910 gave
/// <c>RegisterLoaded</c> the same-sourcePath OVERWRITE (and <c>TryGetByAppId</c> the matching
/// same-sourcePath null) that server mode's warm edit-and-rerun loop needs, which removed the
/// reason for the gate — but the gate stayed, so watch kept running the pre-#1683 path:
/// <c>DependencyLoader</c> Tier-3 source-compiles the dep app from the package's
/// <c>src/*.al</c> into a second live module for one AL identity. On a fixture that merely
/// duplicates the module; on an app too large for Tier-3 to compile at all (npcore NP Retail,
/// ~7,000 files) it is a hard <c>EMIT-ZERO</c> and the session dies in cycle 1.
///
/// <para>Every other `--watch` test in this suite passes exactly one bundle, and the
/// one-shot dedup test never passes `--watch`, so both halves were covered and their
/// intersection was not. These two tests are that intersection.</para>
///
/// <para>Neither test can pass on the old behaviour, and neither passes on a gutted fix:</para>
/// <list type="bullet">
/// <item>Cycle 1 asserts the dependent bundle's test PASSES <b>and</b> that no
/// <c>compiled-deps</c> entry was written into this run's isolated cache root — a
/// filesystem fact no log filtering can hide (unindented <c>[deps]</c> lines are dropped at
/// default verbosity, <c>Log.cs</c>, which is why the recompile went unnoticed; the run is
/// `--verbose` so the log assertion is not vacuous either).</item>
/// <item>The edit test then proves the reuse is not a STALE reuse: it re-runs after editing
/// the dep app and requires the dependent bundle to observe the NEW value, by value. A
/// first-wins registration — the thing the original gate was protecting against — keeps
/// cycle 2 green and fails this test.</item>
/// </list>
///
/// Spawns the real runner; needs the BC artifact cache. Skips (no-op) when absent.
/// </summary>
public class WatchCrossBundleModuleIdentityTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private const string DepAppId = "a1b2c3d4-1683-4a1b-9c3d-000000000001";

    /// <summary>
    /// Writes the two-app fixture: a dep app carrying a setup table, an install trigger that
    /// seeds it, a table-event subscriber that fires during that install, and a marker codeunit
    /// whose return value the dependent app asserts by value. Returns (root, depDir, markerFile).
    /// </summary>
    private static (string root, string depDir, string markerPath) WriteFixture(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), name, Guid.NewGuid().ToString("N"));
        var depDir = Path.Combine(root, "dep-app");
        var testDir = Path.Combine(root, "test-app");
        Directory.CreateDirectory(depDir);
        Directory.CreateDirectory(testDir);

        File.WriteAllText(Path.Combine(depDir, "app.json"), $$"""
        {
          "id": "{{DepAppId}}",
          "name": "TE Dep App 1683",
          "publisher": "Repro1683",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 61860, "to": 61869 } ],
          "runtime": "14.0"
        }
        """);
        File.WriteAllText(Path.Combine(depDir, "TeDep.al"), """
        table 61860 "TE Setup 1683"
        {
            DataClassification = SystemMetadata;
            fields
            {
                field(1; "Primary Key"; Code[10]) { }
                field(2; "Value"; Integer) { }
            }
            keys { key(PK; "Primary Key") { Clustered = true; } }
        }

        codeunit 61861 "TE Subscriber 1683"
        {
            [EventSubscriber(ObjectType::Table, Database::"TE Setup 1683", 'OnAfterModifyEvent', '', false, false)]
            local procedure OnAfterModifyTeSetup(var Rec: Record "TE Setup 1683")
            begin
            end;
        }

        codeunit 61862 "TE Install 1683"
        {
            Subtype = Install;
            trigger OnInstallAppPerCompany()
            var
                Setup: Record "TE Setup 1683";
            begin
                if not Setup.Get('X') then begin
                    Setup."Primary Key" := 'X';
                    Setup.Insert();
                end;
                Setup.Value += 1;
                Setup.Modify(true); // fires OnAfterModifyEvent -> "TE Subscriber 1683"
            end;
        }
        """);
        var markerPath = Path.Combine(depDir, "TeMarker.al");
        File.WriteAllText(markerPath, """
        codeunit 61863 "TE Marker 1683"
        {
            procedure Marker(): Integer
            begin
                exit(1);
            end;
        }
        """);

        File.WriteAllText(Path.Combine(testDir, "app.json"), $$"""
        {
          "id": "b2c3d4e5-1683-4a1b-9c3d-000000000002",
          "name": "TE Main Tests 1683",
          "publisher": "Repro1683",
          "version": "1.0.0.0",
          "dependencies": [
            { "id": "{{DepAppId}}", "name": "TE Dep App 1683", "publisher": "Repro1683", "version": "1.0.0.0" }
          ],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 61870, "to": 61879 } ],
          "runtime": "14.0"
        }
        """);
        File.WriteAllText(Path.Combine(testDir, "TeMainTests.al"), """
        codeunit 61870 "TE Main Tests 1683"
        {
            Subtype = Test;

            [Test]
            procedure DepInstallTriggerRanAndMarkerIsCurrent()
            var
                Setup: Record "TE Setup 1683";
                Marker: Codeunit "TE Marker 1683";
            begin
                if not Setup.Get('X') then
                    Error('TE Setup "X" missing - dep install trigger did not run');
                if Setup.Value < 1 then
                    Error('TE Setup "X" Value is %1 - Modify(true) did not commit', Setup.Value);
                if Marker.Marker() <> 1 then
                    Error('marker=%1', Marker.Marker());
            end;
        }
        """);

        return (root, depDir, markerPath);
    }

    private sealed class WatchSession : IDisposable
    {
        private readonly Process _p;
        private readonly List<string> _lines = new();

        public WatchSession(string root, string cacheDir)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                    + $" \"{Path.Combine(root, "dep-app")}\" \"{Path.Combine(root, "test-app")}\""
                    + $" --watch --verbose --cache \"{cacheDir}\"",
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
            };
            _p = Process.Start(psi)!;
            Pump(_p.StandardOutput);
            Pump(_p.StandardError);
        }

        private void Pump(StreamReader r) => Task.Run(async () =>
        {
            string? l;
            while ((l = await r.ReadLineAsync()) != null) lock (_lines) _lines.Add(l);
        });

        /// <summary>Index of the next "cycle finished, idle" marker at or after <paramref name="fromIndex"/>.</summary>
        public async Task<int> WaitForIdle(int fromIndex, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                // Marker first, exit second: a marker already in the buffer is the answer even
                // if the process has since gone away, and only then is an exit a failure.
                lock (_lines)
                    for (int i = fromIndex; i < _lines.Count; i++)
                        if (_lines[i].Contains("[watch] waiting for AL source")) return i;
                if (_p.HasExited)
                {
                    // A FATAL dep-load (the pre-fix npcore symptom) exits instead of idling —
                    // report the output rather than time out on a dead process.
                    throw new Xunit.Sdk.XunitException(
                        $"watch process exited with {_p.ExitCode} before reaching idle.\n{Dump()}");
                }
                await Task.Delay(200);
            }
            throw new TimeoutException($"watch idle marker not seen.\n{Dump()}");
        }

        public string Segment(int from, int to)
        {
            lock (_lines) return string.Join("\n", _lines.GetRange(from, Math.Max(0, to - from)));
        }

        private string Dump()
        {
            lock (_lines) return "--- last output ---\n" + string.Join("\n", _lines.TakeLast(60));
        }

        public void Dispose() { try { _p.Kill(true); } catch { } _p.Dispose(); }
    }

    /// <summary>
    /// Cycle 1 of a two-bundle watch session must reuse bundle 1's loaded module as bundle 2's
    /// dependency — one loaded module per AL app identity, exactly as one-shot does.
    ///
    /// <para>RED (pre-fix): <c>DependencyLoader</c> Tier-3 source-compiles the dep app, writing
    /// a <c>compiled-deps</c> entry and logging <c>[deps] compiled-on-the-fly</c>, and the
    /// dependent bundle's test fails because the module it binds to is not the one whose install
    /// trigger seeded the table. On an app Tier-3 cannot compile the session dies with
    /// <c>EMIT-ZERO</c>, which is why that string is asserted against too.</para>
    /// </summary>
    [SkippableFact]
    public async Task Watch_DepAppBundlePlusDependentTestApp_ReusesTheModule_WithoutASecondCompile()
    {
        TestArtifacts.SkipIfMissing();

        var (root, _, _) = WriteFixture("al-runner-watch-xbundle");
        var cacheDir = Path.Combine(root, ".cache");

        using var session = new WatchSession(root, cacheDir);
        int m1 = await session.WaitForIdle(0, TimeSpan.FromSeconds(240));
        var cycle1 = session.Segment(0, m1);

        // The dependent bundle's test ran and passed — the whole point. Pre-fix it FAILs with
        // `TE Setup "X" missing`: two modules, so the row the dep's install trigger inserted is
        // not in the table type the test binds to.
        Assert.Contains("PASS  Codeunit61870.DepInstallTriggerRanAndMarkerIsCurrent", cycle1);
        Assert.DoesNotContain("FAIL", cycle1);
        Assert.DoesNotContain("EMIT-ZERO", cycle1);
        Assert.DoesNotContain("TargetException", cycle1);
        Assert.DoesNotContain("Object does not match target type", cycle1);

        // The mechanism, on the filesystem, and the load-bearing half of this test: a Tier-3
        // dependency compile publishes into <cache>/compiled-deps. The directory not existing
        // is proof no second module for this identity was ever built — and unlike the log
        // checks below it cannot be satisfied by output filtering (unindented `[deps]` lines
        // drop at default verbosity, Log.cs, which is why this recompile went unnoticed; the
        // session runs --verbose so they would appear).
        Assert.False(
            Directory.Exists(Path.Combine(cacheDir, "compiled-deps")),
            "the dep app was source-compiled as a dependency instead of reusing bundle 1's module");
        Assert.DoesNotContain("compiled-on-the-fly", cycle1);
        Assert.DoesNotContain("source-cache WROTE", cycle1);
    }

    /// <summary>
    /// The other direction, and the reason the `!watchMode` gate was defensible when it was
    /// written: reusing a module across cycles must never mean reusing a STALE one. Editing the
    /// dep app has to change what the dependent bundle observes, by value.
    ///
    /// <para>Cycle 1 passes with <c>Marker() = 1</c>; the edit makes it <c>9</c>, so cycle 2 must
    /// FAIL with <c>marker=9</c> — a specific value that only the edited dep app can produce.
    /// A first-wins registration keeps cycle 2 green (the dependent still calls the pre-edit
    /// module) and fails here, which is what makes this more than "the second cycle ran".</para>
    /// </summary>
    [SkippableFact]
    public async Task Watch_EditingTheDepApp_TheDependentBundleSeesTheEditedModule()
    {
        TestArtifacts.SkipIfMissing();

        var (root, _, markerPath) = WriteFixture("al-runner-watch-xbundle-edit");
        var cacheDir = Path.Combine(root, ".cache");

        using var session = new WatchSession(root, cacheDir);
        int m1 = await session.WaitForIdle(0, TimeSpan.FromSeconds(240));
        Assert.Contains(
            "PASS  Codeunit61870.DepInstallTriggerRanAndMarkerIsCurrent", session.Segment(0, m1));

        var marker = await File.ReadAllTextAsync(markerPath);
        var edited = marker.Replace("exit(1);", "exit(9);");
        Assert.NotEqual(marker, edited);
        await File.WriteAllTextAsync(markerPath, edited);

        int m2 = await session.WaitForIdle(m1 + 1, TimeSpan.FromSeconds(240));
        var cycle2 = session.Segment(m1 + 1, m2);
        Assert.Contains("FAIL", cycle2);
        Assert.Contains("marker=9", cycle2);
    }
}
