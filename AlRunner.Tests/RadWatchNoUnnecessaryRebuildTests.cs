using System.Diagnostics;
using AlRunner.Infrastructure;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// Two ways a `--watch` session used to buy a whole-module compile it did not need. Both are
/// end-to-end on purpose: neither decision lives in <c>BcCompiler.EmitIncremental</c>, so an
/// in-process cycle over <c>RadFixture</c> cannot see either of them.
///
/// <list type="number">
/// <item><b>The overlay chain reset.</b> <c>Program.RunEmit</c> invalidated the workspace once
/// it held 12 generations, so every 11th code-producing save rebuilt the module — minutes on a
/// large app, for memory hygiene rather than correctness, at a moment the developer could not
/// predict. The cap is gone; this pins that a long editing session stays on the delta path AND
/// that a deep generation chain still resolves to the newest copy of each object.</item>
/// <item><b>A duplicated declaration.</b> Copying an existing <c>.al</c> file to start a new
/// object from it — the ordinary way a developer begins one — produced a key the workspace
/// already owned in an untouched file, and the delta handed the whole module to the compiler
/// "because only the compiler can say which of the two is the duplicate". The compiler's answer
/// is always an error: two objects in one app cannot share an id or a name. So the cycle
/// reports it, costs the parse of the changed file, and leaves the workspace untouched — the
/// save that renumbers the copy deltas straight away.</item>
/// </list>
///
/// Spawns the real runner; needs the BC artifact cache. Skips (no-op) when absent.
/// </summary>
[Collection("server-serial")]
public class RadWatchNoUnnecessaryRebuildTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");
    private static readonly string FixtureSrc = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "DeltaTwoApp"));

    /// <summary>
    /// Fourteen successive code-producing saves, which is two past the old cap: every one of
    /// them must be a one-object delta, none may rebuild the module, and the suite must still
    /// pass at the end — the chain is 15 generations deep by then, and an object has to resolve
    /// to the newest one that declared it.
    ///
    /// <para>The edit is a comment appended to the file that declares codeunit 60921. It moves
    /// the file hash and therefore the object, so each cycle emits an overlay, while
    /// <c>Answer()</c> keeps returning 42 — so a green suite on the last cycle is evidence the
    /// deep chain still executes, not evidence that nothing happened.</para>
    /// </summary>
    [SkippableFact]
    public async Task Watch_FourteenSuccessiveDeltas_NeverRebuildTheModule()
    {
        TestArtifacts.SkipIfMissing();

        var bundle = NewBundle();
        var libSource = Path.Combine(bundle, "Lib", "src", "DeltaLib.Codeunit.al");

        await using var watch = await WatchSession.StartAsync(bundle);

        var cold = await watch.NextCycleAsync();
        Assert.Contains("[watch] Delta Lib: baseline built", cold);
        Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", cold);

        for (int save = 1; save <= 14; save++)
        {
            await File.AppendAllTextAsync(libSource, $"\n// overlay chain save {save}\n");
            var cycle = await watch.NextCycleAsync();

            // One object re-emitted, one overlay loaded — the delta path, every time.
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle);
            Assert.Contains("[watch] Delta Lib: overlay", cycle);
            // …and never the whole module. Under the old cap save 12 printed exactly this.
            Assert.DoesNotContain("[watch] Delta Lib: baseline built", cycle);
            Assert.DoesNotContain("[watch] Delta Lib: full rebuild", cycle);
            Assert.DoesNotContain("[watch] Delta Lib: full compile", cycle);
            // The other two apps are untouched by a body-only edit and must stay that way,
            // so a cycle cannot pass this loop by rebuilding the bundle around Delta Lib.
            Assert.Contains("[watch] Delta Bridge: unchanged", cycle);
            Assert.Contains("[watch] Delta Lib Tests: unchanged", cycle);
            // Fifteen generations deep on the last pass, and 60921 still answers 42.
            Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", cycle);
            Assert.DoesNotContain("FAIL  Codeunit60941.AnswerIsFortyTwo", cycle);
        }
    }

    /// <summary>
    /// A copied <c>.al</c> file is reported as the duplicate it is, and renumbering + renaming
    /// the copy is a plain one-object delta — no whole-module compile at either end.
    ///
    /// <para>Both halves matter. Reporting without the second half would be a cheaper way to be
    /// stuck; deltaing without the first would mean the copy silently replaced the original's
    /// object in the module, which is what the old <c>ws.Declares</c> check actually did.</para>
    /// </summary>
    [SkippableFact]
    public async Task Watch_CopyingAnAlFile_ReportsTheDuplicate_ThenDeltasOnceRenumbered()
    {
        TestArtifacts.SkipIfMissing();

        var bundle = NewBundle();
        var libSource = Path.Combine(bundle, "Lib", "src", "DeltaLib.Codeunit.al");
        var copyPath = Path.Combine(bundle, "Lib", "src", "DeltaLibCopy.Codeunit.al");

        await using var watch = await WatchSession.StartAsync(bundle);

        var cold = await watch.NextCycleAsync();
        Assert.Contains("[watch] Delta Lib: baseline built", cold);
        Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", cold);

        // The copy-paste: byte-identical, id and name not yet changed.
        File.Copy(libSource, copyPath);
        var duplicate = await watch.NextCycleAsync();

        // Named, with both ends and the AL code a cold compile reports for a colliding id.
        Assert.Contains("error AL0264", duplicate);
        Assert.Contains("Delta Lib Answer", duplicate);
        Assert.Contains("DeltaLibCopy.Codeunit.al", duplicate);
        Assert.Contains("DeltaLib.Codeunit.al", duplicate);
        Assert.Contains("give this one a unique id and name", duplicate);
        // Reported, not compiled around: no module rebuild, and no module either — a bundle
        // whose app failed to emit does not run its siblings' tests against a stale generation.
        Assert.DoesNotContain("[watch] Delta Lib: baseline built", duplicate);
        Assert.DoesNotContain("[watch] Delta Lib: full rebuild", duplicate);
        Assert.DoesNotContain("[watch] Delta Lib: full compile", duplicate);
        Assert.DoesNotContain("PASS  Codeunit60941.AnswerIsFortyTwo", duplicate);

        // Give the copy the unique id and name the developer was always going to give it.
        await File.WriteAllTextAsync(copyPath, """
            codeunit 60923 "Delta Lib Answer Copy"
            {
                Access = Internal;

                procedure Answer(): Integer
                begin
                    exit(42);
                end;
            }
            """);
        var repaired = await watch.NextCycleAsync();

        // One object ADDED, nothing else touched — straight back on the fast watch track.
        Assert.Contains("[watch] Delta Lib: delta +1 ~0 -0", repaired);
        Assert.Contains("[watch] Delta Lib: overlay", repaired);
        Assert.DoesNotContain("[watch] Delta Lib: baseline built", repaired);
        Assert.DoesNotContain("[watch] Delta Lib: full rebuild", repaired);
        Assert.DoesNotContain("[watch] Delta Lib: full compile", repaired);
        Assert.DoesNotContain("error AL0264", repaired);
        Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", repaired);
    }

    /// <summary>
    /// Rewriting <c>app.json</c> with the bytes it already held keeps every app warm; changing
    /// what it says still rebuilds them. Both directions, because the useful half of this is
    /// only safe if the other half still fires.
    ///
    /// <para>A byte-identical manifest write is not a contrived input: a branch switch, a
    /// checkout, an editor autosave and a formatter all produce one, and on macOS/APFS even
    /// <see cref="File.Copy(string,string)"/> of the tree touches the source inode enough for
    /// FSEvents to report it. <c>PrepareBundleReload</c> used to read any non-<c>.al</c> path in
    /// the event queue as a blocker, so every one of those cost the whole bundle a
    /// whole-module compile with nothing edited.</para>
    /// </summary>
    [SkippableFact]
    public async Task Watch_RewritingAppJsonWithIdenticalBytes_KeepsTheModuleWarm_ButAnEditRebuildsIt()
    {
        TestArtifacts.SkipIfMissing();

        var bundle = NewBundle();
        var libSource = Path.Combine(bundle, "Lib", "src", "DeltaLib.Codeunit.al");
        var testManifest = Path.Combine(bundle, "LibTests", "app.json");

        await using var watch = await WatchSession.StartAsync(bundle);

        var cold = await watch.NextCycleAsync();
        Assert.Contains("[watch] Delta Lib: baseline built", cold);
        Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", cold);

        // One real delta first, so "warm" means a live overlay chain and not just a cold module.
        await File.AppendAllTextAsync(libSource, "\n// warm the delta path\n");
        var warm = await watch.NextCycleAsync();
        Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", warm);

        // The rewrite: same bytes, new mtime.
        var manifest = await File.ReadAllBytesAsync(testManifest);
        await File.WriteAllBytesAsync(testManifest, manifest);
        var rewritten = await watch.NextCycleAsync();

        Assert.DoesNotContain("full rebuild", rewritten);
        Assert.DoesNotContain("baseline built", rewritten);
        // Nothing in the AL tree moved, so every app reports unchanged and the loaded
        // generations still run — a warm module, not merely a quiet one.
        Assert.Contains("[watch] Delta Lib: unchanged", rewritten);
        Assert.Contains("[watch] Delta Lib Tests: unchanged", rewritten);
        Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", rewritten);

        // The other direction: a manifest that actually says something different. The app's own
        // version line is the one with a trailing comma; the dependency entries spell theirs
        // inline, so this cannot silently rewrite a dependency pin instead.
        var before = await File.ReadAllTextAsync(testManifest);
        var after = before.Replace(
            "\"version\": \"1.0.0.0\",", "\"version\": \"1.0.0.1\",", StringComparison.Ordinal);
        Assert.Equal(1, before.Split("\"version\": \"1.0.0.0\",").Length - 1);
        await File.WriteAllTextAsync(testManifest, after);
        var edited = await watch.NextCycleAsync();

        Assert.Contains("full rebuild", edited);
        Assert.Contains("app.json changed", edited);
        Assert.Contains("[watch] Delta Lib Tests: baseline built", edited);
        Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", edited);
    }

    private static string NewBundle()
    {
        var bundle = Path.Combine(
            Path.GetTempPath(), "al-runner-watch-no-rebuild", Guid.NewGuid().ToString("N"));
        CopyTree(FixtureSrc, bundle);
        return bundle;
    }

    /// <summary>
    /// Stream-copy, never <see cref="File.Copy(string,string,bool)"/>: on macOS/APFS that clones
    /// the file and touches the SOURCE inode, which FSEvents reports as a change to the
    /// checked-in fixture. See RadDeltaWatchTests.CopyTree for the measurement.
    /// </summary>
    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sink = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(sink);
        }
    }

    /// <summary>
    /// A resident `--watch` process, exposing one cycle's output at a time. Each call to
    /// <see cref="NextCycleAsync"/> returns everything printed since the previous cycle's
    /// "waiting for AL source changes" marker, which is how the runner announces it has
    /// finished a cycle and re-armed.
    /// </summary>
    private sealed class WatchSession : IAsyncDisposable
    {
        private const string CycleMarker = "[watch] waiting for AL source";

        private readonly Process _process;
        private readonly List<string> _lines = new();
        private int _consumed;

        private WatchSession(Process process) => _process = process;

        internal static async Task<WatchSession> StartAsync(string bundle)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                    + $" \"{bundle}\" --watch --no-cache",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = RepoRoot,
            };
            var session = new WatchSession(Process.Start(psi)!);
            session.Pump(session._process.StandardOutput);
            session.Pump(session._process.StandardError);
            await Task.Yield();
            return session;
        }

        private void Pump(StreamReader reader) => _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
                lock (_lines) _lines.Add(line);
        });

        /// <summary>
        /// Block until the next cycle finishes and return its output. The budget asserts
        /// "the cycle completed", never "it was fast" — the delta claims are made by the
        /// caller's assertions on the log, which fail loudly if the module was rebuilt.
        /// </summary>
        internal async Task<string> NextCycleAsync()
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(240);
            while (DateTime.UtcNow < deadline)
            {
                lock (_lines)
                    for (int i = _consumed; i < _lines.Count; i++)
                        if (_lines[i].Contains(CycleMarker, StringComparison.Ordinal))
                        {
                            var cycle = string.Join(Environment.NewLine, _lines.GetRange(_consumed, i - _consumed));
                            _consumed = i + 1;
                            return cycle;
                        }
                if (_process.HasExited)
                {
                    string died;
                    lock (_lines) died = string.Join(Environment.NewLine, _lines.TakeLast(40));
                    throw new InvalidOperationException(
                        $"the watch process exited with {_process.ExitCode} mid-session."
                        + $"{Environment.NewLine}--- last output ---{Environment.NewLine}{died}");
                }
                await Task.Delay(200);
            }
            string dump;
            lock (_lines) dump = string.Join(Environment.NewLine, _lines.TakeLast(40));
            throw new TimeoutException(
                $"no cycle completed within the budget.{Environment.NewLine}--- last output ---{Environment.NewLine}{dump}");
        }

        public ValueTask DisposeAsync()
        {
            if (!_process.HasExited) try { _process.Kill(true); } catch { }
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
