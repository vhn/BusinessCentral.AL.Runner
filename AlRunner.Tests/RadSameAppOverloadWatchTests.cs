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

    private static readonly string NsFreeFixtureSrc = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "RadNsFreeOverload"));

    private const string NsFreeApp = "RAD NsFree Overload";
    private const string NsFreeTestCodeunit = "Codeunit72323";
    private const string NsFreeLibFile = "src/NsFreeOverloadLib.Codeunit.al";

    /// <summary>
    /// Body-only, and that is load-bearing rather than incidental. A body change leaves the
    /// library's serialized surface identical, so no direct user is rebound and the bystander
    /// stays on the packaged baseline holding a reference to the codeunit the delta strips —
    /// which is the only way to reach the repair. An edit that moved the surface would rebind
    /// every direct user, bystander included, and there would be nothing left dangling.
    /// </summary>
    private const string NsFreeBodyBefore = "        exit('DECIMAL');";
    private const string NsFreeBodyAfter = "        exit('DECIMAL-V2');";

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
    /// The same family of claim — un-rebound callers dispatching a re-emitted callee by baked
    /// member id — on a fixture that declares NO namespace, where the cycle can only get there
    /// through the delta's stripped-surface replacement. This is the runtime witness for that
    /// repair; every other test of it asserts diagnostics and emitted-object sets.
    ///
    /// <para><b>How the repair is forced.</b> BC chooses the binder per compilation unit: with no
    /// `namespace` declaration a file is bound by `LegacyInContainerBinder`, which resolves an object
    /// name against the packaged module symbol's own copy of the surface — the copy that cannot see
    /// this app's source. `RAD NsFree Ovl Bystander` is never edited, and its
    /// `Hold(Lib: Codeunit "RAD NsFree Ovl Lib")` parameter is exactly such a reference. The
    /// body-only edit below strips the library, that parameter goes unresolvable, and the library's
    /// own `_Bystander.Hold(_This)` — the binding site, inside the edited file — fails `AL0133`
    /// unless the cycle puts the library's freshly compiled surface back. Before the repair this
    /// cycle was a whole-module compile; before THAT it was a COMPILE FAIL on a tree that builds
    /// clean.</para>
    ///
    /// <para><b>What only a runtime assertion can prove.</b> The repair deliberately hands the
    /// compilation a reference definition for an object it is also compiling from source, so name
    /// lookups may resolve the library to the reference copy. Generated calls bake Microsoft's
    /// member id, and neither the caller nor the bystander is re-emitted here — the surface did not
    /// move — so both keep dispatching the ids they baked at the cold compile. "It compiled" says
    /// nothing about whether the repaired pass produced a library those ids still reach. The value
    /// does: a repaired pass that emitted the stale body answers <c>GOT=DECIMAL</c>, and one that
    /// moved <c>Sibling</c>'s id answers <c>GOT=HOLD-WRONG</c> or throws on an unknown function
    /// id.</para>
    /// </summary>
    [SkippableFact]
    public async Task Watch_WithoutANamespace_ABodyEdit_LeavesUnreboundCallersDispatchingCorrectly()
    {
        TestArtifacts.SkipIfMissing();

        using var session = WatchSession.Start("al-runner-rad-nsfree-replacement", NsFreeFixtureSrc);

        // Cycle 1 (cold). The pre-edit runtime answer, measured rather than assumed.
        var cold = await session.NextCycleAsync();
        Assert.Contains($"[watch] {NsFreeApp}: baseline built — 4 object(s)", cold);
        Assert.Contains($"FAIL  {NsFreeTestCodeunit}.UnreboundCallersStillDispatchTheRepairedLibrary", cold);
        Assert.Contains("GOT=DECIMAL", cold);

        // Cycle 2. One body changes, in one file, and nothing else in the app moves.
        session.Edit(NsFreeLibFile, NsFreeBodyBefore, NsFreeBodyAfter);
        var repaired = await session.NextCycleAsync();

        // The repair ran — so this cycle is the one under test rather than one that never needed
        // a second pass. Asserted before the answer, because without it a green run could mean
        // the break simply stopped reproducing.
        Assert.Contains("namespace-free binder chose the packaged copy", repaired);
        Assert.Contains("RAD stripped the changed target it names", repaired);

        // The runtime answer, through the repaired pass: the new body, and the un-rebound
        // bystander's call into the re-emitted library still landing on `Sibling`.
        Assert.Contains($"PASS  {NsFreeTestCodeunit}.UnreboundCallersStillDispatchTheRepairedLibrary", repaired);
        Assert.DoesNotContain("GOT=", repaired);
        Assert.DoesNotContain($"FAIL  {NsFreeTestCodeunit}.", repaired);

        // …and it stayed a delta over the one edited object. This is the assertion the
        // pre-repair runner failed: it took the whole module here, which is a correct answer
        // reached the slow way.
        Assert.Contains($"[watch] {NsFreeApp}: delta +0 ~1 -0 over 1 changed file(s) → 1 object(s) re-emitted", repaired);
        Assert.DoesNotContain($"[watch] {NsFreeApp}: baseline built", repaired);
        Assert.DoesNotContain("full compile —", repaired);
        Assert.DoesNotContain("full rebuild —", repaired);
        Assert.DoesNotContain("COMPILE-FAIL", repaired);
        Assert.DoesNotContain("EMIT-ZERO", repaired);
        Assert.DoesNotContain("__MissingTypeSymbol__", repaired);

        // Cycle 3. Revert it: the answer must go back, still by a delta and still through the
        // repair. A pass that had quietly left the first generation loaded would stay green here.
        session.Edit(NsFreeLibFile, NsFreeBodyAfter, NsFreeBodyBefore);
        var reverted = await session.NextCycleAsync();
        Assert.Contains("namespace-free binder chose the packaged copy", reverted);
        Assert.Contains("RAD stripped the changed target it names", reverted);
        Assert.Contains($"FAIL  {NsFreeTestCodeunit}.UnreboundCallersStillDispatchTheRepairedLibrary", reverted);
        Assert.Contains("GOT=DECIMAL", reverted);
        Assert.DoesNotContain($"[watch] {NsFreeApp}: baseline built", reverted);
        Assert.DoesNotContain("__MissingTypeSymbol__", reverted);
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
