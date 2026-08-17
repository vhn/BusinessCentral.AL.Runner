using System.Diagnostics;
using System.Text;
using AlRunner.Infrastructure;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// Proves the `--watch` delta path across a three-app dependency chain, editing only the
/// leaf library while its tableextension bridge and test app stay warm.
///
/// Four claims, each of which has failed in practice:
///
/// 1. <b>The edited app's new code actually runs.</b> Before the explicit ownership chain
///    (AlRunner.Rad.AlObjectResolution), a two-app bundle resolved a cross-app call by
///    scanning loaded assemblies in unspecified order, and the PREVIOUS cycle's still-loaded
///    types won as often as not — so this exact edit left the test GREEN against code the
///    developer had just changed.
/// 2. <b>Only the changed object is recompiled.</b> The `[watch] … delta +0 ~1 -0` line is the
///    difference between a proportional inner loop and re-emitting the whole module.
/// 3. <b>The untouched app is not recompiled at all.</b>
/// 4. <b>A rejected C# generation never advances or executes the workspace.</b>
///
/// Spawns the real runner; needs the BC artifact cache. Skips (no-op) when absent.
/// </summary>
[Collection("server-serial")]
public class RadDeltaWatchTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");
    private static readonly string FixtureSrc = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "DeltaTwoApp"));
    private static readonly string TableExtDepSrc = Path.Combine(
        RepoRoot, "tests", "runner-extras", "dep-tableext-platform-base-dep");
    private static readonly string TableExtMainSrc = Path.Combine(
        RepoRoot, "tests", "runner-extras", "dep-tableext-platform-base-main");

    [SkippableFact]
    public async Task Watch_EditingTheLibraryApp_RecompilesOnlyThatObject_AndRunsTheNewCode()
    {
        TestArtifacts.SkipIfMissing();

        var bundle = Path.Combine(Path.GetTempPath(), "al-runner-rad-delta", Guid.NewGuid().ToString("N"));
        CopyTree(FixtureSrc, bundle);
        var libSource = Path.Combine(bundle, "Lib", "src", "DeltaLib.Codeunit.al");
        var testSource = Path.Combine(bundle, "LibTests", "src", "DeltaLibTests.Codeunit.al");

        // The non-RAD path publishes the same sibling symbols before compiling C in the
        // A <- B <- C chain. Pin that wiring separately; the watch process below uses the
        // resident RAD publisher even for its first, full compile.
        var once = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                + $" \"{bundle}\" --no-cache",
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        once.Environment["AL_RUNNER_RAD"] = "0";
        using (var full = Process.Start(once)!)
        {
            try
            {
                var stdout = full.StandardOutput.ReadToEndAsync();
                var stderr = full.StandardError.ReadToEndAsync();
                await full.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(240));
                var output = (await stdout) + (await stderr);
                Assert.True(full.ExitCode == 0, output);
                Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", output);
            }
            finally
            {
                if (!full.HasExited) try { full.Kill(true); } catch { }
            }
        }

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
            string dump; lock (lines) dump = string.Join("\n", lines.TakeLast(40));
            throw new TimeoutException($"watch marker not seen.\n--- last output ---\n{dump}");
        }

        string Segment(int from, int to)
        {
            lock (lines) return string.Join("\n", lines.GetRange(from, Math.Max(0, to - from)));
        }

        try
        {
            // Cycle 1 (cold): the test passes, and all three apps get a delta baseline.
            int m1 = await WaitForMarkerAfter(0, TimeSpan.FromSeconds(240));
            var cycle1 = Segment(0, m1);
            Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", cycle1);
            Assert.DoesNotContain("FAIL  Codeunit", cycle1);
            Assert.Contains("[watch] Delta Lib: baseline built", cycle1);
            Assert.Contains("[watch] Delta Bridge: baseline built", cycle1);
            Assert.Contains("[watch] Delta Lib Tests: baseline built", cycle1);

            // First edit the test codeunit itself. Its unchanged Install codeunit remains
            // in the baseline generation, but must still seed data before the overlay's
            // test runs.
            await File.AppendAllTextAsync(testSource, "\n// exercise test-app overlay\n");
            int m2 = await WaitForMarkerAfter(m1 + 1, TimeSpan.FromSeconds(240));
            var cycle2 = Segment(m1 + 1, m2);
            Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", cycle2);
            Assert.Contains("[watch] Delta Lib Tests: delta +0 ~1 -0", cycle2);
            Assert.Contains("[watch] Delta Lib Tests: overlay", cycle2);

            // Edit ONLY the library app. The test app is now untouched and still asserts 42.
            var lib = await File.ReadAllTextAsync(libSource);
            var edited = lib.Replace("exit(42);", "exit(43);");
            Assert.NotEqual(lib, edited);
            await File.WriteAllTextAsync(libSource, edited);

            // Cycle 3. Generous budget — this asserts "the cycle finished", not "it was
            // fast"; the delta claim is made by the log assertions below, which fail
            // loudly if the whole module was re-emitted instead.
            int m3 = await WaitForMarkerAfter(m2 + 1, TimeSpan.FromSeconds(240));
            var cycle3 = Segment(m2 + 1, m3);

            // The new body ran: a stale generation would still return 42 and PASS.
            Assert.Contains("Delta Lib Answer returned 43, expected 42", cycle3);

            // Exactly one object recompiled in the edited app…
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle3);
            Assert.Contains("[watch] Delta Lib: overlay", cycle3);
            // …and nothing at all in the app that did not change.
            Assert.Contains("[watch] Delta Bridge: unchanged", cycle3);
            Assert.Contains("[watch] Delta Lib Tests: unchanged", cycle3);
            // The full path is what the delta path replaces here; seeing it means the
            // delta bailed out and the speed claim is not being met.
            Assert.DoesNotContain("[watch] Delta Lib: baseline built", cycle3);

            // Edit the same codeunit again while its first overlay is already loaded.
            // The next overlay must bind against the generation chain without duplicate
            // type ambiguity, and the newest owner must win at runtime.
            var editedAgain = edited.Replace("exit(43);", "exit(44);");
            Assert.NotEqual(edited, editedAgain);
            await File.WriteAllTextAsync(libSource, editedAgain);
            int m4 = await WaitForMarkerAfter(m3 + 1, TimeSpan.FromSeconds(240));
            var cycle4 = Segment(m3 + 1, m4);
            Assert.Contains("FAIL  Codeunit60941.AnswerIsFortyTwo", cycle4);
            Assert.Contains("Delta Lib Answer returned 44, expected 42", cycle4);
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle4);
            Assert.Contains("[watch] Delta Lib: overlay", cycle4);
            Assert.DoesNotContain("[watch] Delta Lib: baseline built", cycle4);

            // A callable-surface change is still an object delta. This app has no
            // same-module callers, so only the changed codeunit is replaced.
            const string surfaceChanged = """
                codeunit 60921 "Delta Lib Answer"
                {
                    Access = Internal;

                    procedure Answer(): Integer
                    begin
                        exit(45);
                    end;

                    procedure Marker(): Integer
                    begin
                        exit(1);
                    end;
                }
                """;
            await File.WriteAllTextAsync(libSource, surfaceChanged);
            int m5 = await WaitForMarkerAfter(m4 + 1, TimeSpan.FromSeconds(240));
            var cycle5 = Segment(m4 + 1, m5);
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle5);
            Assert.Contains("[watch] Delta Lib: overlay", cycle5);
            Assert.DoesNotContain("[watch] Delta Lib: baseline built", cycle5);
            Assert.Contains("Delta Lib Answer returned 45, expected 42", cycle5);

            // Change the callable surface and introduce an AL-valid call whose generated
            // C# is rejected by Roslyn. A failed backend must not commit or run the
            // untouched test app against the last-good generation.
            const string broken = """
                codeunit 60921 "Delta Lib Answer"
                {
                    Access = Internal;

                    procedure Answer(): Integer
                    var
                        FileName: Text;
                    begin
                        Database.ExportData(false, FileName);
                        exit(44);
                    end;

                    procedure Marker(): Integer
                    begin
                        exit(1);
                    end;

                    procedure BrokenMarker(): Integer
                    begin
                        exit(2);
                    end;
                }
                """;
            await File.WriteAllTextAsync(libSource, broken);
            int m6 = await WaitForMarkerAfter(m5 + 1, TimeSpan.FromSeconds(240));
            var cycle6 = Segment(m5 + 1, m6);
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle6);
            Assert.Contains("COMPILE-FAIL", cycle6);
            Assert.DoesNotContain("[watch] Delta Lib: overlay", cycle6);
            Assert.DoesNotContain("PASS  Codeunit60941.", cycle6);
            Assert.DoesNotContain("FAIL  Codeunit60941.", cycle6);

            // Saving the same broken bytes again must retry the delta, not accept
            // hashes recorded before the failed backend and report the app unchanged.
            await File.WriteAllTextAsync(libSource, broken);
            int m7 = await WaitForMarkerAfter(m6 + 1, TimeSpan.FromSeconds(240));
            var cycle7 = Segment(m6 + 1, m7);
            Assert.Contains("COMPILE-FAIL", cycle7);
            Assert.DoesNotContain("[watch] Delta Lib: unchanged", cycle7);
            Assert.DoesNotContain("PASS  Codeunit60941.", cycle7);
            Assert.DoesNotContain("FAIL  Codeunit60941.", cycle7);

            // Repair is another one-object delta from the last committed baseline.
            await File.WriteAllTextAsync(libSource, editedAgain);
            int m8 = await WaitForMarkerAfter(m7 + 1, TimeSpan.FromSeconds(240));
            var cycle8 = Segment(m7 + 1, m8);
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle8);
            Assert.Contains("[watch] Delta Lib: overlay", cycle8);
            // Unlike cycle 3's body-only edit, the repair restores the ORIGINAL file — so it
            // also removes Marker() and BrokenMarker() from codeunit 60921. Removing a member
            // moves that object's callable surface, and Delta Bridge calls into it from another
            // app with its generated calls baking member ids taken from the surface this cycle
            // just replaced. So Bridge must rebind. Before cross-app edges existed the graph
            // could not say so and this line read `Delta Bridge: unchanged`; that expectation
            // was the bug, not the behaviour.
            //
            // Asserted with the count and the reason rather than just "something was rebound":
            // the widening has to come from the per-key rule over the one surface that moved.
            // A rebind logged as `which rebuilt in full` would mean the producer had broadcast
            // "assume everything moved", which on a one-object delta is the cascade this work
            // exists to remove. Cycle 3 is the other half of that guarantee — a body-only edit
            // publishes nothing, so Bridge stays unchanged there.
            Assert.Contains(
                "[watch] Delta Bridge: rebinding 1 cross-app caller file(s) — 1 that call Delta Lib",
                cycle8);
            Assert.DoesNotContain("rebuilt in full", cycle8);
            // …and the widening does not cascade. Bridge is re-emitted, but its own surface did
            // not move, so it publishes nothing and the app that calls BRIDGE is left alone.
            Assert.Contains("[watch] Delta Lib Tests: unchanged", cycle8);
            Assert.Contains("FAIL  Codeunit60941.AnswerIsFortyTwo", cycle8);
            Assert.Contains("Delta Lib Answer returned 44, expected 42", cycle8);
        }
        finally
        {
            try { p.Kill(true); } catch { }
            try { Directory.Delete(bundle, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// A member-id move in one app rebinds the caller that lives in ANOTHER app.
    ///
    /// <para>Generated calls bake Microsoft's member id, and
    /// <c>MethodSymbol.CalculateMethodId</c> hashes each parameter's <c>NavTypeKind</c> — so
    /// retyping <c>Scaled(Factor: Integer)</c> to <c>Scaled(Factor: Decimal)</c> moves the id
    /// while leaving <c>Delta Bridge</c>'s own source valid, because an Integer argument
    /// widens to a Decimal parameter. Bridge is therefore never in <c>changedFiles</c>, and
    /// only the reference graph can say it must be rebound.</para>
    ///
    /// <para>It could not say so: <c>BcCompiler.ReferenceTargetKey</c> returns null for every
    /// cross-app target, so the Bridge→Lib edge is discarded when the graph is built. Bridge
    /// takes the <c>NoChange</c> short-circuit and keeps executing IL that dispatches the
    /// previous id.</para>
    ///
    /// <para><b>Observed before the fix — loud, not silent.</b> The retired id is absent from
    /// the re-emitted callee, so dispatch fails with
    /// <c>NavNCLCompilationException: Function ID 1446680415 was called. The object with ID
    /// 60921 does not have a member with that ID.</c> rather than answering wrongly. That
    /// satisfies <c>.claude/rules/loud-failures.md</c> as far as it goes, but it is still the
    /// wrong answer: a cold compile of the same tree reports the developer's actual assertion
    /// failure (<c>returned 42, expected 84</c>), and the delta reports an internal dispatch
    /// error naming a function id no AL author has ever seen. Loudness is not correctness
    /// here, and it is not general either — when the old id SURVIVES on the new object, the
    /// same staleness is silent. Adding an overload is exactly that case.</para>
    ///
    /// <para>So the oracle is the cold compile of the identical tree, not "some error".</para>
    /// </summary>
    [SkippableFact]
    public async Task Watch_MovingAMemberIdInOneApp_RebindsItsCrossAppCaller()
    {
        TestArtifacts.SkipIfMissing();

        var bundle = Path.Combine(Path.GetTempPath(), "al-runner-rad-xapp", Guid.NewGuid().ToString("N"));
        CopyTree(FixtureSrc, bundle);
        var scaleSource = Path.Combine(bundle, "Lib", "src", "DeltaLibScale.Codeunit.al");

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
            string dump; lock (lines) dump = string.Join("\n", lines.TakeLast(40));
            throw new TimeoutException($"watch marker not seen.\n--- last output ---\n{dump}");
        }

        string Segment(int from, int to)
        {
            lock (lines) return string.Join("\n", lines.GetRange(from, Math.Max(0, to - from)));
        }

        try
        {
            // Cycle 1 (cold): the cross-app call answers 42 * 2.
            int m1 = await WaitForMarkerAfter(0, TimeSpan.FromSeconds(240));
            var cycle1 = Segment(0, m1);
            Assert.Contains("PASS  Codeunit60941.ScaledIsEightyFour", cycle1);
            Assert.DoesNotContain("FAIL  Codeunit", cycle1);

            // The id-moving edit. The body changes too, so that RUNNING the new code is
            // observable: a correctly rebound caller answers 21 * 2 = 42, and the AL test
            // (which expects 84) then fails for the developer's own reason.
            var scale = await File.ReadAllTextAsync(scaleSource);
            var retyped = scale
                .Replace("procedure Scaled(Factor: Integer): Integer",
                         "procedure Scaled(Factor: Decimal): Integer")
                .Replace("exit(42 * Factor);", "exit(21 * Factor);");
            Assert.NotEqual(scale, retyped);
            await File.WriteAllTextAsync(scaleSource, retyped);

            int m2 = await WaitForMarkerAfter(m1 + 1, TimeSpan.FromSeconds(240));
            var cycle2 = Segment(m1 + 1, m2);

            // The edited app deltas its one object…
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle2);
            // …and the app that CALLS it is rebound rather than reused wholesale.
            Assert.DoesNotContain("[watch] Delta Bridge: unchanged", cycle2);
            Assert.Contains("[watch] Delta Bridge: delta", cycle2);

            // The answer is the one a cold compile of this exact tree gives.
            var cold = await ColdCompileAsync(bundle);
            Assert.Contains("Delta Lib Scaled returned 42, expected 84", cold);
            Assert.Contains("Delta Lib Scaled returned 42, expected 84", cycle2);
            Assert.DoesNotContain("does not have a member with that ID", cycle2);

            // Control — precision, not "rebind everything". A body-only edit leaves every
            // member id where it was, so the cross-app caller must stay warm.
            await File.WriteAllTextAsync(
                scaleSource, retyped.Replace("exit(21 * Factor);", "exit(42 * Factor);"));
            int m3 = await WaitForMarkerAfter(m2 + 1, TimeSpan.FromSeconds(240));
            var cycle3 = Segment(m2 + 1, m3);
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle3);
            Assert.Contains("[watch] Delta Bridge: unchanged", cycle3);
            Assert.Contains("PASS  Codeunit60941.ScaledIsEightyFour", cycle3);
        }
        finally
        {
            try { p.Kill(true); } catch { }
            try { Directory.Delete(bundle, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// The SILENT half of cross-app member-id staleness: adding an overload.
    ///
    /// <para>Watch_MovingAMemberIdInOneApp_RebindsItsCrossAppCaller retypes a parameter, which
    /// RETIRES the old member id — so the un-rebound caller dispatches an id the callee no
    /// longer has and BC throws. Loud, and therefore survivable.</para>
    ///
    /// <para>Adding an overload is the same staleness with the safety net removed.
    /// <c>CalculateMethodId</c> is method-local, so <c>Pick(Decimal)</c> keeps its id and its
    /// <c>case</c> label; what moves is which id the CALLER bakes, because an Integer argument
    /// now binds to the new <c>Pick(Integer)</c> instead of widening to the Decimal one. A
    /// caller that is never rebound therefore dispatches a member that still exists and gets a
    /// perfectly ordinary answer — the PREVIOUS one. No exception, no diagnostic, no log line.
    /// The only thing that notices is an assertion that happens to check the value.</para>
    ///
    /// <para>This is what <c>.claude/rules/loud-failures.md</c> calls a green test that lies,
    /// and it is why "the cross-app bug fails loudly" is not a reason to downgrade it: loudness
    /// was an accident of which edit was measured first.</para>
    ///
    /// <para>The test asserts the wrong answer is not merely absent but that the RIGHT one is
    /// present, and separately that the failure mode really is silent — no dispatch exception
    /// anywhere in the cycle — so the claim in the paragraph above is pinned rather than
    /// asserted in prose.</para>
    /// </summary>
    [SkippableFact]
    public async Task Watch_AddingAnOverloadInOneApp_RebindsItsCrossAppCaller()
    {
        TestArtifacts.SkipIfMissing();

        var bundle = Path.Combine(Path.GetTempPath(), "al-runner-rad-xovl", Guid.NewGuid().ToString("N"));
        CopyTree(FixtureSrc, bundle);
        var scaleSource = Path.Combine(bundle, "Lib", "src", "DeltaLibScale.Codeunit.al");
        var testSource = Path.Combine(bundle, "LibTests", "src", "DeltaLibTests.Codeunit.al");

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
            string dump; lock (lines) dump = string.Join("\n", lines.TakeLast(40));
            throw new TimeoutException($"watch marker not seen.\n--- last output ---\n{dump}");
        }

        string Segment(int from, int to)
        {
            lock (lines) return string.Join("\n", lines.GetRange(from, Math.Max(0, to - from)));
        }

        try
        {
            // Cycle 1 (cold): one overload, and the Integer argument widens to it.
            int m1 = await WaitForMarkerAfter(0, TimeSpan.FromSeconds(240));
            var cycle1 = Segment(0, m1);
            Assert.Contains("PASS  Codeunit60941.PickBindsTheOnlyOverload", cycle1);
            Assert.DoesNotContain("FAIL  Codeunit", cycle1);

            // Add the Integer overload in the library app, and move the test app's expectation
            // with it. Delta Bridge — which is what actually chooses between the two — is not
            // touched, and is the whole subject of the test.
            var scale = await File.ReadAllTextAsync(scaleSource);
            var overloaded = scale.Replace(
                """
                    procedure Pick(Seed: Decimal): Integer
                    begin
                        exit(1);
                    end;
                """,
                """
                    procedure Pick(Seed: Decimal): Integer
                    begin
                        exit(1);
                    end;

                    procedure Pick(Seed: Integer): Integer
                    begin
                        exit(2);
                    end;
                """);
            Assert.NotEqual(scale, overloaded);
            await File.WriteAllTextAsync(scaleSource, overloaded);

            var tests = await File.ReadAllTextAsync(testSource);
            var expectTwo = tests.Replace(
                "Assert.AreEqual(1, Bridge.Pick(), 'Delta Lib Pick');",
                "Assert.AreEqual(2, Bridge.Pick(), 'Delta Lib Pick');");
            Assert.NotEqual(tests, expectTwo);
            await File.WriteAllTextAsync(testSource, expectTwo);

            int m2 = await WaitForMarkerAfter(m1 + 1, TimeSpan.FromSeconds(240));
            var cycle2 = Segment(m1 + 1, m2);

            // The app that chooses the overload must be rebound…
            Assert.DoesNotContain("[watch] Delta Bridge: unchanged", cycle2);
            // …and the answer must be the one a cold compile of this tree gives.
            var cold = await ColdCompileAsync(bundle);
            Assert.Contains("PASS  Codeunit60941.PickBindsTheOnlyOverload", cold);
            Assert.Contains("PASS  Codeunit60941.PickBindsTheOnlyOverload", cycle2);
            Assert.DoesNotContain("Delta Lib Pick returned 1, expected 2", cycle2);

            // The failure this test exists for is SILENT. Pin that: the retired-id bug announces
            // itself with a dispatch exception, and this one has nothing to announce it with, so
            // an assertion on the value is the only thing standing between it and a green run.
            Assert.DoesNotContain("does not have a member with that ID", cycle2);
            Assert.DoesNotContain("NavNCLCompilationException", cycle2);
        }
        finally
        {
            try { p.Kill(true); } catch { }
            try { Directory.Delete(bundle, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// The path a developer actually takes to <c>--watch</c>: run the suite once, then start
    /// watching the same tree. The one-shot run is what writes the cache entry the watch hits —
    /// and it has <b>no <c>RadWorkspace</c> at all</b>, because the store is only enabled under
    /// <c>--watch</c>. So every cross-app edge in the envelope it leaves behind can only have
    /// come from the app graph (<c>RadAppCohort</c>, built in <c>Program.cs</c> before the app
    /// loop); a cohort derived from live workspaces would write an envelope with zero of them,
    /// the first watch would hydrate nothing, and the developer's first edit would be exactly as
    /// stale as before the cross-app work existed — with a schema bump and nothing to show for
    /// it.
    ///
    /// <para>Two claims, and only the second one is worth having. The envelope carries edges
    /// into the sibling app and none into the precompiled dependencies — that is the cheap half,
    /// and on its own it proves only that a file on disk has the right shape. The half that
    /// matters is that a watch process which never compiled these apps rebinds
    /// <c>Delta Bridge</c> on the FIRST edit, purely out of what it hydrated, and answers what a
    /// cold compile of the identical tree answers.</para>
    ///
    /// <para>The oracle is that cold run, not "some error". The edit retypes
    /// <c>Delta Lib Scale.Scaled</c>'s parameter, which moves its member id while leaving
    /// <c>Delta Bridge</c>'s source valid (an Integer argument widens to a Decimal parameter) —
    /// so Bridge never enters <c>changedFiles</c> and only a hydrated cross-app edge can say it
    /// must be re-emitted.</para>
    ///
    /// <para><b>What this measured that is not about cross-app edges at all:</b> the PRODUCER's
    /// hydration does not survive the first edit, because a one-shot run and a watch run do not
    /// compute the same reference signature for an app that is a dependency target. The
    /// assertion below names it in full. It is asserted rather than worked around so that it
    /// cannot be mistaken for intended behaviour, and so that fixing it fails this test rather
    /// than passing it silently.</para>
    /// </summary>
    [SkippableFact]
    public async Task OneShotSidecar_ThenWatch_HydratesCrossAppEdges_AndRebindsTheSiblingCaller()
    {
        TestArtifacts.SkipIfMissing();

        var root = Path.Combine(
            Path.GetTempPath(), "al-runner-rad-oneshot-xapp", Guid.NewGuid().ToString("N"));
        var bundle = Path.Combine(root, "bundle");
        var cache = Path.Combine(root, "cache");
        CopyTree(FixtureSrc, bundle);
        Directory.CreateDirectory(cache);
        var scaleSource = Path.Combine(bundle, "Lib", "src", "DeltaLibScale.Codeunit.al");

        // ── phase 1: the one-shot run that writes the cache entry ─────────────────────
        var oneShot = await RunOnceAsync($" \"{bundle}\" --cache \"{cache}\"");
        Assert.Contains("PASS  Codeunit60941.ScaledIsEightyFour", oneShot);
        Assert.DoesNotContain("FAIL  Codeunit", oneShot);
        Assert.Contains("[cache] MISS", oneShot);

        // Delta Lib's workspace identity is its app.json id — RadWorkspaceStore.IdentityOf's
        // first case. Spelled out rather than read back from the envelope, so a writer that
        // stored some other string could not satisfy the assertion by agreeing with itself.
        const string libIdentity = "7c4f6c1e9a2b4f0d8f3a1b2c3d4e5f60";

        var bridgeEnvelope = ReadEnvelope(cache, "Delta Bridge");
        Assert.Equal(2, bridgeEnvelope["schema"]!.GetValue<int>());
        var edges = bridgeEnvelope["crossAppReferences"]!.AsArray();
        Assert.NotEmpty(edges);
        // Codeunit 60961 "Delta Bridge" calls Codeunit 60922 "Delta Lib Scale" — the exact
        // edge the rebind below has to travel.
        var callsFromBridgeCodeunit = edges
            .Where(edge => Kind(edge!["from"]!) == "Codeunit" && Id(edge["from"]!) == 60961)
            .SelectMany(edge => edge!["to"]!.AsArray())
            .ToList();
        Assert.Contains(
            callsFromBridgeCodeunit,
            target => target!["app"]!.GetValue<string>() == libIdentity
                && Kind(target["key"]!) == "Codeunit" && Id(target["key"]!) == 60922);
        // …and nothing points OUT of the bundle. Every AL object also binds against the
        // platform's precompiled symbols; retaining those edges would cost a multiple of the
        // envelope for targets that can never change between two watch cycles. Delta Bridge
        // depends on exactly one sibling, so every cross-app target must name it.
        foreach (var edge in edges)
            foreach (var target in edge!["to"]!.AsArray())
                Assert.Equal(libIdentity, target!["app"]!.GetValue<string>());

        // ── phase 2: a fresh watch process over the same tree and the same cache ──────
        var lines = new List<string>();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                + $" \"{bundle}\" --watch --cache \"{cache}\"",
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
            string dump; lock (lines) dump = string.Join("\n", lines.TakeLast(40));
            throw new TimeoutException($"watch marker not seen.\n--- last output ---\n{dump}");
        }

        string Segment(int from, int to)
        {
            lock (lines) return string.Join("\n", lines.GetRange(from, Math.Max(0, to - from)));
        }

        try
        {
            // Cycle 1: nothing compiles at all — the module arrives out of the cache and the
            // delta state arrives out of the sidecar beside it.
            int m1 = await WaitForMarkerAfter(0, TimeSpan.FromSeconds(240));
            var cycle1 = Segment(0, m1);
            Assert.Contains("PASS  Codeunit60941.ScaledIsEightyFour", cycle1);
            Assert.DoesNotContain("FAIL  Codeunit", cycle1);
            Assert.Contains("[cache] rad baseline hydrated for Delta Bridge", cycle1);
            Assert.Contains("[cache] rad baseline hydrated for Delta Lib", cycle1);
            Assert.DoesNotContain("[watch] Delta Bridge: baseline built", cycle1);
            Assert.DoesNotContain("[watch] Delta Lib: baseline built", cycle1);

            // The id-moving edit, in the app the watch process has never compiled.
            var scale = await File.ReadAllTextAsync(scaleSource);
            var retyped = scale
                .Replace("procedure Scaled(Factor: Integer): Integer",
                         "procedure Scaled(Factor: Decimal): Integer")
                .Replace("exit(42 * Factor);", "exit(21 * Factor);");
            Assert.NotEqual(scale, retyped);
            await File.WriteAllTextAsync(scaleSource, retyped);

            int m2 = await WaitForMarkerAfter(m1 + 1, TimeSpan.FromSeconds(240));
            var cycle2 = Segment(m1 + 1, m2);

            // The subject of the test: the consumer deltas off a baseline it never compiled,
            // and is re-emitted although its own source did not move — which can only have come
            // from the cross-app edges the one-shot envelope carried.
            Assert.DoesNotContain("[watch] Delta Bridge: baseline built", cycle2);
            Assert.DoesNotContain("[watch] Delta Bridge: unchanged", cycle2);
            Assert.Contains("[watch] Delta Bridge: rebinding ", cycle2);
            Assert.Contains(" that call Delta Lib", cycle2);
            Assert.Contains("[watch] Delta Bridge: delta ", cycle2);

            // MEASURED SIDE-FINDING, asserted so it cannot quietly change: the PRODUCER's own
            // hydration does not survive this cycle. A one-shot run publishes each
            // dependency-target app's symbols in a pre-pass (Program.cs, `EmitSiblingSymbols` —
            // skipped under RAD), so by the time Delta Lib itself compiles, the sibling-symbols
            // directory already holds Delta BRIDGE's symbols and they enter Delta Lib's resolved
            // reference set. The signature persisted for Delta Lib therefore carries a
            // `ref|…|Delta Bridge|…` line that the watch path — which publishes incrementally
            // and never puts a dependent's symbols in front of its dependency — cannot
            // reproduce, so ArmFor invalidates on the first edit.
            //
            // Consequence: for an app that is a dependency TARGET, one-shot-then-watch still
            // pays a whole-module compile on the first edit, which is the exact cost the sidecar
            // exists to remove. It is a defect in the signature, not in the cross-app work, and
            // it is why the rebind below reads `which rebuilt in full` rather than naming one
            // moved key. When the signature is made mode-independent, this becomes
            // `Delta Lib: delta +0 ~1 -0` and the rebind message loses its suffix — update both
            // lines together.
            Assert.Contains(
                "[watch] Delta Lib: full rebuild — the resolved dependency set changed (1 → 0)",
                cycle2);

            // Whichever of the two the producer broadcast, the consumer only hears it because
            // its hydrated graph names the producer at all: PendingCrossAppRebinds returns
            // immediately for a workspace with no cross-app producers, which is precisely what a
            // schema-1 envelope, or a cohort derived from live workspaces, would have restored.
            var cold = await ColdCompileAsync(bundle);
            Assert.Contains("Delta Lib Scaled returned 42, expected 84", cold);
            Assert.Contains("Delta Lib Scaled returned 42, expected 84", cycle2);
            Assert.DoesNotContain("does not have a member with that ID", cycle2);
        }
        finally
        {
            try { p.Kill(true); } catch { }
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// The <c>&lt;key&gt;.rad-baseline.json</c> one app group left in <paramref name="cacheDir"/>.
    /// Found by the module name it records rather than by the cache key, because the key is a
    /// hash of the runner binary and the tree and reproducing it here would be a second
    /// implementation of <c>ComputeAlCacheKey</c> for the test to be wrong in.
    /// </summary>
    private static System.Text.Json.Nodes.JsonObject ReadEnvelope(string cacheDir, string module)
    {
        var envelopes = Directory
            .EnumerateFiles(cacheDir, "*" + AlCacheSidecars.RadBaselineSuffix)
            .Select(path => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject())
            .ToList();
        Assert.NotEmpty(envelopes);
        var found = envelopes
            .Where(envelope => envelope["module"]!.GetValue<string>() == module)
            .ToList();
        Assert.True(
            found.Count == 1,
            $"expected exactly one envelope for '{module}', found {found.Count} among "
            + string.Join(", ", envelopes.Select(e => e["module"]!.GetValue<string>())));
        return found[0];
    }

    // The envelope's writer omits members left at their default (JsonIgnoreCondition
    // .WhenWritingDefault), so an id-less key carries no `id` at all and a bare `["id"]!` would
    // NRE on the first interface or permission-set edge in the array.
    private static string Kind(System.Text.Json.Nodes.JsonNode key) =>
        key["kind"]?.GetValue<string>() ?? string.Empty;

    private static int Id(System.Text.Json.Nodes.JsonNode key) =>
        key["id"]?.GetValue<int>() ?? 0;

    /// <summary>Run the real runner to completion and return everything it printed.</summary>
    private static async Task<string> RunOnceAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg + arguments,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        using var run = Process.Start(psi)!;
        var stdout = run.StandardOutput.ReadToEndAsync();
        var stderr = run.StandardError.ReadToEndAsync();
        await run.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(240));
        return (await stdout) + (await stderr);
    }

    /// <summary>
    /// Compile and run a copy of <paramref name="tree"/> from scratch — no watch, no cache,
    /// no baseline. Whatever a cold build says about the tree is what the delta has to say
    /// about it. Copied first so the live watcher cannot observe the run.
    /// </summary>
    private static async Task<string> ColdCompileAsync(string tree)
    {
        var copy = Path.Combine(Path.GetTempPath(), "al-runner-rad-xapp-cold", Guid.NewGuid().ToString("N"));
        CopyTree(tree, copy);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                    + $" \"{copy}\" --no-cache",
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
            };
            using var cold = Process.Start(psi)!;
            var stdout = cold.StandardOutput.ReadToEndAsync();
            var stderr = cold.StandardError.ReadToEndAsync();
            await cold.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(240));
            return (await stdout) + (await stderr);
        }
        finally
        {
            try { Directory.Delete(copy, recursive: true); } catch { }
        }
    }

    [SkippableFact]
    public async Task Watch_PrecompiledTableExtensionDependency_RehydratesFieldsAfterReload()
    {
        TestArtifacts.SkipIfMissing();
        var platformApps = CompatiblePlatformApps();
        TestArtifacts.SkipIf(platformApps == null,
            $"compatible platform-apps not provisioned under '{TestArtifacts.PlatformAppsDir()}'.");

        var root = Path.Combine(Path.GetTempPath(), "al-runner-rad-tableext", Guid.NewGuid().ToString("N"));
        var dep = Path.Combine(root, "dep");
        var main = Path.Combine(root, "main");
        var packages = Path.Combine(root, "packages");
        CopyTree(TableExtDepSrc, dep);
        CopyTree(TableExtMainSrc, main);
        Directory.CreateDirectory(packages);

        // A real precompiled-dependency shape: source lets DependencyLoader build the
        // runtime DLL, while the embedded SymbolReference is what RecordPatches retains
        // across watch cycles. Keeping the dependency outside the watched source tree is
        // essential — a sibling source app would be reparsed and mask the reload bug.
        var identity = InProcessAppPackager.ReadIdentity(Path.Combine(dep, "app.json"));
        Assert.NotNull(identity);
        var depApp = Path.Combine(packages, "AL_Runner_DTB_Platform_Base_Dep_1_0_0_0.app");
        InProcessAppPackager.EmitAppPackageToFile(
            dep, identity!, depApp, Encoding.UTF8.GetBytes(TableExtSymbolReference));
        var symbolsPath = Path.Combine(packages, "AL_Runner_DTB_Platform_Base_Dep_1_0_0_0.symbols.json");
        File.WriteAllText(symbolsPath, TableExtSymbolReference);
        var platformClosure = Directory.EnumerateFiles(platformApps, "*.app", SearchOption.AllDirectories)
            .Select(AppLoader.ReadManifest)
            .OfType<AppManifest>()
            .Select(m => new DepsSidecarWriter.DepEntry(m.Publisher, m.Name, m.Version, m.AppId));
        DepsSidecarWriter.Write(
            Path.ChangeExtension(symbolsPath, ".deps.json"),
            identity.Publisher, identity.Name, identity.Version, identity.AppId, platformClosure);

        var lines = new List<string>();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                + $" \"{main}\" --watch --no-cache --verbose"
                + $" --package-cache \"{packages}\" --package-cache \"{platformApps}\"",
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

        try
        {
            int m1 = await WaitForMarkerAfter(0, TimeSpan.FromSeconds(240));
            var cycle1 = Segment(0, m1);
            Assert.True(CountOccurrences(cycle1, "PASS  Codeunit63411.") == 4, cycle1);
            Assert.DoesNotContain("extension field 61881", cycle1);
            Assert.Contains("[watch] DTB Platform Base Main: baseline built", cycle1);
            Assert.Contains("precompiled tableextension(s) into _parsedExtensionFields", cycle1);

            // Change one real codeunit file. It recompiles as a one-object overlay, while
            // the dependency remains warm and is not re-registered — the exact lifecycle
            // that used to lose its extension fields after ResetForReload.
            var testSource = Path.Combine(main, "DtbTests.Codeunit.al");
            await File.AppendAllTextAsync(testSource, "\n// trigger warm tableextension reload\n");

            int m2 = await WaitForMarkerAfter(m1 + 1, TimeSpan.FromSeconds(240));
            var cycle2 = Segment(m1 + 1, m2);
            Assert.True(CountOccurrences(cycle2, "PASS  Codeunit63411.") == 4, cycle2);
            Assert.DoesNotContain("extension field 61881", cycle2);
            Assert.DoesNotContain("EXEC-FAIL", cycle2);
            Assert.Contains("[watch] DTB Platform Base Main: delta +0 ~1 -0", cycle2);

            // Pin the cache invariant: reload must re-merge extension metadata without
            // throwing away and rebuilding the already-warm base-table symbol index.
            Assert.Contains("precompiled tableextension(s) into _parsedExtensionFields", cycle2);
            Assert.DoesNotContain("BcAppFallback: indexed", cycle2);
        }
        finally
        {
            try { p.Kill(true); } catch { }
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int i = 0; (i = text.IndexOf(value, i, StringComparison.Ordinal)) >= 0; i += value.Length)
            count++;
        return count;
    }

    private static string? CompatiblePlatformApps()
    {
        var built = BcArtifacts.EngineBuiltVersion();
        if (built == null || !Directory.Exists(BcArtifacts.ArtifactsRootDir)) return null;
        return Directory.EnumerateDirectories(BcArtifacts.ArtifactsRootDir)
            .Select(dir => (dir, parsed: Version.TryParse(Path.GetFileName(dir), out var v) ? v : null))
            .Where(x => x.parsed?.Major == built.Major && x.parsed?.Minor == built.Minor)
            .OrderByDescending(x => x.parsed)
            .Select(x => Path.Combine(x.dir, "platform-apps"))
            .FirstOrDefault(dir => Directory.Exists(dir)
                && Directory.EnumerateFiles(dir, "*.app", SearchOption.AllDirectories).Any());
    }

    private const string TableExtSymbolReference = """
        {
          "RuntimeVersion": "17.0",
          "Codeunits": [],
          "TableExtensions": [
            {
              "TargetObject": "#437dbf0e84ff417a965ded2bb9650972#Item",
              "Fields": [
                {
                  "TypeDefinition": { "Name": "Boolean" },
                  "Properties": [{ "Name": "DataClassification", "Value": "CustomerContent" }],
                  "Id": 61881,
                  "Name": "DTB Repro Flag"
                },
                {
                  "TypeDefinition": { "Name": "Integer" },
                  "Properties": [{ "Name": "DataClassification", "Value": "CustomerContent" }],
                  "Id": 61882,
                  "Name": "DTB Repro Counter"
                }
              ],
              "Id": 61881,
              "Name": "DTB Item Ext"
            }
          ],
          "Reports": [],
          "XmlPorts": [],
          "Queries": [],
          "ControlAddIns": [],
          "EnumTypes": [],
          "DotNetPackages": [],
          "Interfaces": [],
          "PermissionSets": [],
          "PermissionSetExtensions": [],
          "ReportExtensions": [],
          "InternalsVisibleToModules": [],
          "AppId": "5d3c2b1a-6f4e-4a2d-9c1b-8e7f6a5d4c31",
          "Name": "DTB Platform Base Dep",
          "Publisher": "AL Runner",
          "Version": "1.0.0.0"
        }
        """;

    /// <summary>
    /// Copy a tree without the live watcher noticing.
    /// </summary>
    /// <remarks>
    /// The stream copy is not a style choice. <see cref="File.Copy(string,string,bool)"/> on
    /// macOS/APFS clones the source file, which touches the SOURCE inode's metadata — and
    /// FSEvents reports that as a change, so <c>FileSystemWatcher</c> raises <c>Changed</c> for
    /// every file of a tree that was only READ. Measured against the real runner: copying the
    /// watched bundle with <c>File.Copy</c> starts a whole watch cycle (a full rebuild of every
    /// app, since a byte-identical <c>app.json</c> counts as "not AL source" to
    /// <c>RadWorkspaceStore.PrepareBundleReload</c>), while the same copy done with
    /// <c>ReadAllBytes</c> or a <c>FileStream</c> raises nothing at all.
    ///
    /// <para>That matters here because <see cref="ColdCompileAsync"/> copies the LIVE bundle
    /// mid-test and its whole premise is that the watcher cannot observe it. With
    /// <c>File.Copy</c> the spurious cycle lands between the cycle under test and the next
    /// edit, so the next <c>WaitForMarkerAfter</c> returns the WRONG cycle and the assertions
    /// are read against a segment that compiled the pre-edit tree.</para>
    ///
    /// <para>The runner is not blameless — a read-only event costing every app in the bundle a
    /// whole-module rebuild is a real defect — but it is a separate one from anything these
    /// tests assert, and it is not what this helper should be measuring.</para>
    /// </remarks>
    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var target = new FileStream(
                file.Replace(from, to), FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }
    }
}
