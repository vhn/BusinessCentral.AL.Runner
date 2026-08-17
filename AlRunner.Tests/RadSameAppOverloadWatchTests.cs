// RadSameAppOverloadWatchTests — the runtime half of the silent overload hazard, same app.
//
// RadSameAppOverloadTests measures which objects the delta re-emits. That is a proxy: what
// the developer actually gets wrong is the ANSWER. So this suite runs the real runner over a
// real `--watch` session and reads the bound overload out of a running AL test, by value.
//
// Why the value and not "no exception": adding an overload leaves the previous overload's id
// and its `case` label intact in the re-emitted callee, so a caller that was not rebound
// dispatches a member that still exists and returns the PREVIOUS overload's answer. Nothing
// throws, nothing is logged, and the cycle reports success. The AL test's `BOUND-TO=<value>`
// is the only witness there is — which is exactly what
// .claude/rules/loud-failures.md calls a green test that lies, in the one shape where the
// runner has no way to be loud.
//
// The cross-app version of this is RadDeltaWatchTests.
// Watch_AddingAnOverloadInOneApp_RebindsItsCrossAppCaller. This one keeps caller and callee
// in ONE module, which is the path a member-level surface diff rewrites.

using System.Diagnostics;
using Xunit;

namespace AlRunner.Tests;

[Collection("server-serial")]
public class RadSameAppOverloadWatchTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");
    private static readonly string FixtureSrc = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "RadSameAppOverload"));

    private const string App = "RAD Same App Overload";
    private const string TestCodeunit = "Codeunit72302";
    private const string LibFile = "src/OverloadLib.Codeunit.al";

    private const string OverloadDeclaration = """
            procedure Which(Seed: Integer): Text
            begin
                exit('INTEGER');
            end;

            procedure Sibling(Value: Integer): Integer
        """;

    /// <summary>
    /// Three cycles, each asserting the overload the caller dispatched by its returned value:
    ///
    /// <list type="number">
    /// <item>cold — only <c>Which(Decimal)</c> exists, so the Integer argument widens to it
    ///   and the AL test reports <c>BOUND-TO=DECIMAL</c>. That is the pre-edit runtime answer
    ///   measured, not assumed;</item>
    /// <item>the library gains <c>Which(Integer)</c> — and NOTHING else in the app changes.
    ///   The caller's own file is byte-identical, so it is re-emitted only because the delta
    ///   decided its baked id had moved. The test then passes, which can only happen if the
    ///   caller really dispatches the new overload;</item>
    /// <item>the overload is removed again. The caller has to rebind back, and the answer
    ///   returns to <c>DECIMAL</c> — a delta that only ever widens forward would pass cycle 2
    ///   and fail here.</item>
    /// </list>
    ///
    /// <para>Cycle 2 also pins that the failure mode really is silent: no dispatch exception
    /// anywhere in the cycle. The cross-app member-id test can lean on
    /// <c>NavNCLCompilationException</c> announcing staleness because a RETYPED parameter
    /// retires the old id; here the old id survives, so there is nothing to announce it and
    /// the value assertion is the only thing standing between this bug and a green run.</para>
    ///
    /// <para><b>It really does discriminate.</b> Measured against a runner temporarily taught
    /// the naive rule ("the object's serialized surface only grew, so the addition is safe —
    /// skip the rebind"): cycle 2 reported
    /// <c>delta +0 ~1 -0 over 1 changed file(s) → 1 object(s) re-emitted</c>, no diagnostic and
    /// no exception of any kind — and the AL test answered <c>BOUND-TO=DECIMAL</c>. That is a
    /// green watch cycle executing the overload the developer just stopped calling, and this
    /// test is what turns it red.</para>
    /// </summary>
    [SkippableFact]
    public async Task Watch_AddingAnOverload_RebindsTheSameAppCaller_ToTheNewOverload()
    {
        TestArtifacts.SkipIfMissing();

        using var session = WatchSession.Start("al-runner-rad-sameapp-overload", FixtureSrc);

        // Cycle 1 (cold). The whole app compiles, and the Integer argument widens to the only
        // overload there is.
        var cold = await session.NextCycleAsync();
        Assert.Contains($"[watch] {App}: baseline built — 3 object(s)", cold);
        Assert.Contains($"FAIL  {TestCodeunit}.CallerBindsTheIntegerOverload", cold);
        Assert.Contains("BOUND-TO=DECIMAL", cold);

        // Cycle 2. One file changes — the library's — and the caller must come with it.
        session.Edit(LibFile, "    procedure Sibling(Value: Integer): Integer", OverloadDeclaration);
        var overloaded = await session.NextCycleAsync();

        // The runtime answer, which is the whole point: the caller dispatches the overload
        // that did not exist one cycle ago.
        Assert.Contains($"PASS  {TestCodeunit}.CallerBindsTheIntegerOverload", overloaded);
        Assert.DoesNotContain("BOUND-TO=DECIMAL", overloaded);
        Assert.DoesNotContain($"FAIL  {TestCodeunit}.", overloaded);

        // …reached by a delta, not by giving up and rebuilding the module.
        Assert.Contains($"[watch] {App}: rebinding 1 direct caller file(s)", overloaded);
        Assert.Contains("object(s) re-emitted", overloaded);
        Assert.DoesNotContain($"[watch] {App}: baseline built", overloaded);
        Assert.DoesNotContain("full compile —", overloaded);
        Assert.DoesNotContain("full rebuild —", overloaded);
        Assert.DoesNotContain("COMPILE-FAIL", overloaded);
        Assert.DoesNotContain("EMIT-ZERO", overloaded);

        // Nothing announced the staleness this test exists to catch — pinned, because the
        // absence is the reason an assertion on the VALUE is the only available oracle.
        Assert.DoesNotContain("does not have a member with that ID", overloaded);
        Assert.DoesNotContain("NavNCLCompilationException", overloaded);

        // Cycle 3. Remove it again: the caller must rebind back to the Decimal overload.
        session.Edit(LibFile, OverloadDeclaration, "    procedure Sibling(Value: Integer): Integer");
        var reverted = await session.NextCycleAsync();
        Assert.Contains($"FAIL  {TestCodeunit}.CallerBindsTheIntegerOverload", reverted);
        Assert.Contains("BOUND-TO=DECIMAL", reverted);
        Assert.DoesNotContain($"[watch] {App}: baseline built", reverted);
    }

    /// <summary>
    /// One resident `--watch` runner over a private copy of a single-app fixture, with the
    /// output split into cycles at the watcher's own idle marker. Edits are real file writes,
    /// and every edit waits for the previous cycle's marker first, so no two edits can be
    /// coalesced into one cycle.
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

        internal static WatchSession Start(string scenarioDir, string fixtureSrc)
        {
            var bundle = Path.Combine(Path.GetTempPath(), scenarioDir, Guid.NewGuid().ToString("N"));
            CopyTree(fixtureSrc, bundle);

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                // --no-cache: a cached whole-module DLL carries no compiler symbol baseline,
                // so RAD would spend cycle 2 establishing one and every assertion below would
                // be off by a cycle.
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
        /// Copy a tree without the live watcher noticing.
        /// </summary>
        /// <remarks>
        /// The stream copy is not a style choice. <see cref="File.Copy(string,string,bool)"/>
        /// on macOS/APFS clones the source file, which touches the SOURCE inode's metadata —
        /// FSEvents reports that as a change, so a <c>FileSystemWatcher</c> raises
        /// <c>Changed</c> for every file of a tree that was only READ. A spurious cycle then
        /// lands between the cycle under test and the next edit, and the assertions are read
        /// against a segment that compiled the pre-edit tree. Same reason
        /// <c>RadDeltaWatchTests.CopyTree</c> uses a stream.
        /// </remarks>
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
