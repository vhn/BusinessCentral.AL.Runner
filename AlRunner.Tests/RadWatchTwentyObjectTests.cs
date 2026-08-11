// RadWatchTwentyObjectTests — the same proportionality claim as RadObjectDeltaTests, but
// made against the real `--watch --rad` process instead of the compiler seam.
//
// The compiler-level suites prove which objects re-emit and which CLR types change owner.
// They deliberately stop below Program.cs, so they cannot see the half of a watch cycle
// that has broken before: the per-cycle runtime reset, the metadata the reload preserves or
// rebuilds, and whether the tests that run afterwards execute the code the developer just
// saved. Those are exactly where a "faster" reload turns into a silently wrong one — a
// green test against the previous generation is worse than a slow one.
//
// So every cycle here asserts two things together:
//
//   * the [rad] log line for each app — `delta +0 ~1 -0` / `overlay … 1 object(s)` for the
//     edited app, `unchanged` for the one that was not touched, and never `baseline built`
//     after the cold cycle. That is the performance contract, in the runner's own words.
//   * the AL test outcome — which test flipped, and what value it reported. An edit whose
//     new code did not actually run leaves the outcome unchanged, so this is what separates
//     "reloaded the right object" from "reported success and ran the old one".
//
// Timings are never asserted. A cycle budget of 240 s means "the cycle finished", and the
// proportionality claim is carried entirely by the log and outcome assertions above.
//
// Spawns the real runner; needs the BC artifact cache. Skips (no-op) when absent.

using System.Diagnostics;
using Xunit;

namespace AlRunner.Tests;

[Collection("server-serial")]
public class RadWatchTwentyObjectTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");
    private static readonly string FixtureRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures"));

    private const string App = "RAD Twenty Object Fixture";
    private const string TestApp = "RAD Perf Tests";
    private const string TestCodeunit = "Codeunit71202";
    private const int TestCount = 6;

    private static bool ArtifactsPresent()
    {
        try { return Directory.Exists(AlRunner.Infrastructure.BcArtifacts.ServiceTierDir); }
        catch { return false; }
    }

    /// <summary>
    /// Body edits to two different object kinds — a codeunit and a table — each replacing
    /// exactly one object, with the AL test outcome proving the new body ran. The table row
    /// is the interesting one: the runner has no database, so a table edit is an object
    /// recompile plus a metadata refresh and has no business costing more than a codeunit.
    /// </summary>
    [Fact]
    public async Task Watch_EditingOneObjectBody_ReplacesOnlyThatObject()
    {
        if (!ArtifactsPresent()) { Console.Error.WriteLine("[skip] BC artifact cache not present"); return; }

        using var session = WatchSession.Start("al-runner-rad-watch-bodies");
        var cold = await session.NextCycleAsync();
        AssertColdBaseline(cold);

        // A codeunit body. The test app is untouched and must not be recompiled at all.
        session.Edit("App/src/RadPerfService.Codeunit.al", "exit(40);", "exit(41);");
        var codeunit = await session.NextCycleAsync();
        AssertOneObjectDelta(codeunit, App, unchanged: TestApp);
        Assert.Contains($"FAIL  {TestCodeunit}.ServiceValueIsForty", codeunit);
        Assert.Contains("Service value returned 41, expected 40", codeunit);
        AssertOutcomes(codeunit, failures: 1);

        session.Edit("App/src/RadPerfService.Codeunit.al", "exit(41);", "exit(40);");
        var repaired = await session.NextCycleAsync();
        AssertOneObjectDelta(repaired, App, unchanged: TestApp);
        AssertOutcomes(repaired, failures: 0);

        // A table's own trigger body.
        session.Edit("App/src/RadPerfHeader.Table.al", "'header-v1'", "'header-v2'");
        var table = await session.NextCycleAsync();
        AssertOneObjectDelta(table, App, unchanged: TestApp);
        Assert.Contains($"FAIL  {TestCodeunit}.HeaderInsertStampsV1", table);
        Assert.Contains("Header insert trigger returned header-v2, expected header-v1", table);
        AssertOutcomes(table, failures: 1);

        session.Edit("App/src/RadPerfHeader.Table.al", "'header-v2'", "'header-v1'");
        AssertOutcomes(await session.NextCycleAsync(), failures: 0);

        // A save that changed nothing (formatter no-op, git checkout, editor autosave) wakes
        // the watcher but must not compile: change detection is on content, not timestamps.
        session.Touch("App/src/RadPerfService.Codeunit.al");
        var touched = await session.NextCycleAsync();
        Assert.Contains($"[rad] {App}: unchanged — reusing the loaded module", touched);
        Assert.Contains($"[rad] {TestApp}: unchanged — reusing the loaded module", touched);
        Assert.DoesNotContain($"[rad] {App}: delta", touched);
        Assert.DoesNotContain($"[rad] {App}: overlay", touched);
        AssertOutcomes(touched, failures: 0);
    }

    /// <summary>
    /// Schema and enum additions, each in two cycles: the app grows a field or a value, then
    /// the untouched test app starts using it. The second cycle is the proof — new AL source
    /// binds against the delta's merged symbols AND the runtime resolves the new field or
    /// ordinal, so the structural change really landed instead of merely compiling.
    ///
    /// The last cycle deletes an object nobody references. That is a delta with no C# at
    /// all, and the surviving tests must still run: an empty emit once left the app out of
    /// the cycle's assembly list entirely, which reads as "no tests" rather than a failure.
    /// </summary>
    [Fact]
    public async Task Watch_AddingAndRemovingObjects_StaysProportional()
    {
        if (!ArtifactsPresent()) { Console.Error.WriteLine("[skip] BC artifact cache not present"); return; }

        using var session = WatchSession.Start("al-runner-rad-watch-structural");
        AssertColdBaseline(await session.NextCycleAsync());

        // 1. A tableextension grows a field. Extension field 71002: 71000/71001 already
        //    extend RAD Perf Header, so it is the next free field id on that target table.
        session.Edit(
            "App/src/RadPerfHeaderExtA.TableExt.al",
            "field(71000; \"Extension A\"; Text[30])",
            """
            field(71002; "Extension A Note"; Text[30]) { DataClassification = SystemMetadata; }
                    field(71000; "Extension A"; Text[30])
            """);
        var extField = await session.NextCycleAsync();
        AssertOneObjectDelta(extField, App, unchanged: TestApp);
        AssertOutcomes(extField, failures: 0);

        session.Edit(
            "Tests/src/RadPerfWatchTests.Codeunit.al",
            "        Header.Insert();",
            """
                    Header."Extension A Note" := 'noted';
                    Header.Insert();
            """);
        session.Edit(
            "Tests/src/RadPerfWatchTests.Codeunit.al",
            "Assert.AreEqualText('kept', Header.\"Extension A\", 'Extension A round trip');",
            """
            Assert.AreEqualText('kept', Header."Extension A", 'Extension A round trip');
                    Assert.AreEqualText('noted', Header."Extension A Note", 'Extension A Note round trip');
            """);
        var extFieldUsed = await session.NextCycleAsync();
        AssertOneObjectDelta(extFieldUsed, TestApp, unchanged: App);
        AssertOutcomes(extFieldUsed, failures: 0);

        // 2. A table grows a field. Field id 3 from the AL ID Manager for table 71001.
        session.Edit(
            "App/src/RadPerfLine.Table.al",
            "field(2; \"Header No.\"; Code[20]) { DataClassification = SystemMetadata; }",
            """
            field(2; "Header No."; Code[20]) { DataClassification = SystemMetadata; }
                    field(3; Note; Text[30]) { DataClassification = SystemMetadata; }
            """);
        var tableField = await session.NextCycleAsync();
        AssertOneObjectDelta(tableField, App, unchanged: TestApp);
        AssertOutcomes(tableField, failures: 0);

        session.Edit(
            "Tests/src/RadPerfWatchTests.Codeunit.al",
            "        Line.Insert();",
            """
                    Line.Note := 'noted';
                    Line.Insert();
            """);
        session.Edit(
            "Tests/src/RadPerfWatchTests.Codeunit.al",
            "Assert.AreEqualText('H1', Line.\"Header No.\", 'Line header no.');",
            """
            Assert.AreEqualText('H1', Line."Header No.", 'Line header no.');
                    Assert.AreEqualText('noted', Line.Note, 'Line note');
            """);
        var tableFieldUsed = await session.NextCycleAsync();
        AssertOneObjectDelta(tableFieldUsed, TestApp, unchanged: App);
        AssertOutcomes(tableFieldUsed, failures: 0);

        // 3. An enumextension grows a value. Value 71001 from the AL ID Manager.
        session.Edit(
            "App/src/RadPerfStatusExt.EnumExt.al",
            "value(71000; Archived) { Caption = 'Archived'; }",
            """
            value(71000; Archived) { Caption = 'Archived'; }
                value(71001; Retired) { Caption = 'Retired'; }
            """);
        var enumValue = await session.NextCycleAsync();
        AssertOneObjectDelta(enumValue, App, unchanged: TestApp);
        AssertOutcomes(enumValue, failures: 0);

        session.Edit(
            "Tests/src/RadPerfWatchTests.Codeunit.al",
            """
            Status := Enum::"RAD Perf Status"::Archived;
                    Assert.AreEqualInt(71000, Status.AsInteger(), 'Archived ordinal');
            """,
            """
            Status := Enum::"RAD Perf Status"::Retired;
                    Assert.AreEqualInt(71001, Status.AsInteger(), 'Retired ordinal');
            """);
        var enumValueUsed = await session.NextCycleAsync();
        AssertOneObjectDelta(enumValueUsed, TestApp, unchanged: App);
        AssertOutcomes(enumValueUsed, failures: 0);

        // 4. Delete an object nothing references: no emit, no C#, no overlay — and the
        //    surviving tests still run.
        session.Delete("App/src/RadPerfUnrelatedD.Codeunit.al");
        var deletion = await session.NextCycleAsync();
        Assert.Contains($"[rad] {App}: delta +0 ~0 -1 over 0 changed file(s) → 0 object(s) re-emitted", deletion);
        Assert.DoesNotContain($"[rad] {App}: overlay", deletion);
        Assert.DoesNotContain($"[rad] {App}: baseline built", deletion);
        Assert.Contains($"[rad] {TestApp}: unchanged", deletion);
        AssertOutcomes(deletion, failures: 0);
    }

    /// <summary>Cycle 1 compiles both apps in full and records their RAD baselines.</summary>
    private static void AssertColdBaseline(string cycle)
    {
        Assert.Contains($"[rad] {App}: baseline built — 20 object(s)", cycle);
        Assert.Contains($"[rad] {TestApp}: baseline built — 2 object(s)", cycle);
        AssertOutcomes(cycle, failures: 0);
    }

    /// <summary>
    /// One object re-emitted in <paramref name="edited"/>, nothing at all in
    /// <paramref name="unchanged"/>, and no app back on the full-compile path.
    /// </summary>
    private static void AssertOneObjectDelta(string cycle, string edited, string unchanged)
    {
        Assert.Contains($"[rad] {edited}: delta +0 ~1 -0 over 1 changed file(s) → 1 object(s) re-emitted", cycle);
        // Read the object count off the edited app's OWN overlay line: a generic
        // "1 object(s)" match anywhere in the cycle would also accept the other app's.
        var overlay = Assert.Single(cycle.Split(Environment.NewLine)
            .Where(line => line.Contains($"[rad] {edited}: overlay", StringComparison.Ordinal)));
        Assert.Contains("— 1 object(s)", overlay);
        Assert.Contains($"[rad] {unchanged}: unchanged — reusing the loaded module", cycle);
        // Any of these would mean the delta bailed out and the whole module was rebuilt.
        Assert.DoesNotContain($"[rad] {edited}: baseline built", cycle);
        Assert.DoesNotContain($"[rad] {unchanged}: baseline built", cycle);
        Assert.DoesNotContain("full rebuild —", cycle);
    }

    /// <summary>
    /// Every test in the suite ran, and exactly <paramref name="failures"/> of them failed.
    /// A cycle that silently ran nothing — the deletion-only regression — fails here.
    /// </summary>
    private static void AssertOutcomes(string cycle, int failures)
    {
        var passes = Count(cycle, $"PASS  {TestCodeunit}.");
        var fails = Count(cycle, $"FAIL  {TestCodeunit}.");
        Assert.Equal(failures, fails);
        Assert.Equal(TestCount - failures, passes);
        Assert.DoesNotContain("COMPILE-FAIL", cycle);
        Assert.DoesNotContain("EMIT-ZERO", cycle);
        Assert.DoesNotContain("RAD-LOAD-FAIL", cycle);
        Assert.DoesNotContain("EXEC-FAIL", cycle);
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int i = 0; (i = text.IndexOf(value, i, StringComparison.Ordinal)) >= 0; i += value.Length)
            count++;
        return count;
    }

    /// <summary>
    /// One resident `--watch --rad` runner over a private copy of the 20-object app and its
    /// test app, with the output split into cycles at the watcher's own idle marker.
    ///
    /// Edits are real file writes, so the FileSystemWatcher drives the cycle exactly as it
    /// does for a developer saving in an editor — and because every edit waits for the
    /// previous cycle's marker first, no two edits can be coalesced into one cycle.
    /// </summary>
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

        internal static WatchSession Start(string scenarioDir)
        {
            var bundle = Path.Combine(Path.GetTempPath(), scenarioDir, Guid.NewGuid().ToString("N"));
            CopyTree(Path.Combine(FixtureRoot, "RadTwentyObject"), Path.Combine(bundle, "App"));
            CopyTree(Path.Combine(FixtureRoot, "RadTwentyObjectTests"), Path.Combine(bundle, "Tests"));

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                // --no-cache: a cached whole-module DLL carries no compiler symbol baseline,
                // so RAD would spend cycle 2 establishing one and the assertions below would
                // be off by a cycle.
                Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                    + $" \"{bundle}\" --watch --rad --no-cache",
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
                            var cycle = string.Join(Environment.NewLine, _lines.GetRange(_cursor, i - _cursor));
                            _cursor = i + 1;
                            return cycle;
                        }
                await Task.Delay(200);
            }
            string tail;
            lock (_lines) tail = string.Join(Environment.NewLine, _lines.TakeLast(60));
            throw new TimeoutException($"watch cycle did not finish.{Environment.NewLine}--- last output ---{Environment.NewLine}{tail}");
        }

        internal void Edit(string relativePath, string before, string after)
        {
            var path = Path.Combine(_bundle, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var source = File.ReadAllText(path);
            Assert.Equal(1, source.Split(before, StringSplitOptions.None).Length - 1);
            File.WriteAllText(path, source.Replace(before, after, StringComparison.Ordinal));
        }

        /// <summary>Rewrite a file with its own bytes — a save that changed nothing.</summary>
        internal void Touch(string relativePath)
        {
            var path = Path.Combine(_bundle, relativePath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(path, File.ReadAllText(path));
        }

        internal void Delete(string relativePath) =>
            File.Delete(Path.Combine(_bundle, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        public void Dispose()
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
            _process.Dispose();
            try { Directory.Delete(_bundle, recursive: true); } catch { }
        }

        private static void CopyTree(string from, string to)
        {
            foreach (var source in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(to, Path.GetRelativePath(from, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
        }
    }
}
