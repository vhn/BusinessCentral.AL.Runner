// CacheRootsNoCacheIsolationTests — `--no-cache` really does start cold.
//
// What was wrong
// --------------
// `--no-cache` disabled the AL-output cache and nothing else. Every other on-disk cache —
// compiled-deps, workspace-deps, ncl-cecil, bc-symbols, app-manifests, r2r-chunks,
// install-baseline — kept reading and writing the shared `~/.cache/al-runner/<name>`. So the
// one situation the flag exists for, measuring or reproducing a cold compile, was exactly the
// situation it lied about: the run was handed the compiled dependency DLLs, the Cecil-rewritten
// Ncl, the parsed .app symbol tables and the extracted R2R chunks, worth tens of seconds, and
// reported itself as uncached.
//
// Why a path-string test is not enough
// ------------------------------------
// Same argument as CacheRootsIsolationTests, which this file mirrors deliberately: a test that
// only checks the resolved PATH would pass against code that computes a throwaway directory and
// then performs its I/O against the old shared location anyway. The proof has to be
// behavioural, and it has to be observed from OUTSIDE the process, because the throwaway root
// is removed when that process exits.
//
// The decisive shape: run the real runner TWICE with `--no-cache`, over IDENTICAL fixture
// content, so the workspace-deps cache KEY is identical both times. Both runs must report a
// MISS ("WROTE", never "cache HIT"). Before the fix run 1 populated the shared real cache and
// run 2 was served by it — a HIT here is precisely the bug. The dependency's identity is a
// fresh GUID, so run 1 cannot be anything but a MISS on any machine, CI or local.
//
// Spawns the real runner; needs the BC artifact cache. Skips (no-op) when absent.

using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public class CacheRootsNoCacheIsolationTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private static (string output, int exit) RunRunnerNoCache(string bundleDir, string absentPackageCache)
    {
        var args = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
        args.Append(TestBuildConfig.BcVersionArg);
        args.Append($" \"{bundleDir}\"");
        args.Append(" --no-cache");
        // --verbose because Log.FilteredWriter drops any line starting with a bracketed
        // alphanumeric tag unless it is set, and `[Cecil]` is one of those (`[source-dep]` and
        // the two-space-indented `[cache]` lines are not — a hyphen and a leading space
        // respectively put them outside the pattern). The Cecil MISS/HIT pair is half the claim
        // this test makes, so the run has to be told to emit it.
        args.Append(" --verbose");
        // Same zero-package-cache pin as CacheRootsIsolationTests — forces the real Tier-3
        // source-compile path for the dependency on every leg, CI included.
        args.Append($" --package-cache \"{absentPackageCache}\"");
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet", Arguments = args.ToString(),
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        var sb = new StringBuilder();
        using var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(300_000)) { try { p.Kill(true); } catch { } throw new TimeoutException("runner hung"); }
        p.WaitForExit();
        lock (sb) return (sb.ToString(), p.ExitCode);
    }

    [SkippableFact]
    public void TwoNoCacheRuns_SameContent_BothMissWorkspaceDeps()
    {
        TestArtifacts.SkipIfMissing();

        var scratchRoot = Path.Combine(Path.GetTempPath(), "al-runner-nocache-isolation", Guid.NewGuid().ToString("N"));
        var depDir = Path.Combine(scratchRoot, "dep-app");
        var testsDir = Path.Combine(scratchRoot, "tests-app");
        var absentPackageCache = Path.Combine(scratchRoot, "no-such-package-cache");
        Directory.CreateDirectory(depDir);
        Directory.CreateDirectory(testsDir);

        // Fresh random identity: this dependency's workspace-deps key has never been seen by
        // any prior run on this machine, so run 1 is unconditionally a MISS. Content is byte-
        // identical between the two runs, so the KEY is identical too — which is what makes
        // run 2's verdict mean something.
        var depId = Guid.NewGuid();
        var testsId = Guid.NewGuid();

        File.WriteAllText(Path.Combine(depDir, "app.json"), $$"""
        {
          "id": "{{depId}}",
          "name": "NoCache Dep App",
          "publisher": "NoCacheRepro",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 61940, "to": 61949 } ],
          "runtime": "14.0"
        }
        """);
        File.WriteAllText(Path.Combine(depDir, "NoCacheDep.al"), """
        codeunit 61940 "NoCache Greeter"
        {
            procedure Greet(): Text
            begin
                exit('hello');
            end;
        }
        """);

        File.WriteAllText(Path.Combine(testsDir, "app.json"), $$"""
        {
          "id": "{{testsId}}",
          "name": "NoCache Tests",
          "publisher": "NoCacheRepro",
          "version": "1.0.0.0",
          "dependencies": [
            { "id": "{{depId}}", "name": "NoCache Dep App", "publisher": "NoCacheRepro", "version": "1.0.0.0" }
          ],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 61950, "to": 61959 } ],
          "runtime": "14.0"
        }
        """);
        File.WriteAllText(Path.Combine(testsDir, "NoCacheTests.al"), """
        codeunit 61950 "NoCache Dep Test"
        {
            Subtype = Test;

            [Test]
            procedure GreeterWorks()
            var
                Greeter: Codeunit "NoCache Greeter";
                Result: Text;
            begin
                Result := Greeter.Greet();
                if Result <> 'hello' then
                    Error('Expected hello, got ''%1''', Result);
            end;
        }
        """);

        var (outputA, exitA) = RunRunnerNoCache(testsDir, absentPackageCache);
        Assert.True(exitA == 0 && outputA.Contains("1P/0F/0E"), $"run A must pass:\n{outputA}");
        Assert.Contains("[source-dep] WROTE NoCache Dep App", outputA);
        Assert.Contains("[cache] --no-cache: every on-disk cache redirected to", outputA);

        // ncl-cecil is bypassed too, and the re-exec it triggers still lands on a HIT.
        //
        // A fresh rewrite makes the runner re-exec itself: a process that rewrites Ncl and then
        // loads the byte-identical result in-process intermittently dies with
        // BadImageFormatException 0x80131124, so the child exists to load it from cache. Give
        // the child its own throwaway root and it MISSes, rewrites a second time, and then
        // takes exactly that fatal path — which is why the root is inherited across the
        // re-exec. MISS then re-exec then HIT, in one run's output, is what proves both halves:
        // the cache really was cold, and the child really did adopt the parent's root.
        Assert.Contains("[Cecil] Cecil cache MISS", outputA);
        Assert.Contains("[Cecil] Fresh rewrite done — re-execing", outputA);
        Assert.Contains("[Cecil] Cecil cache HIT", outputA);

        var (outputB, exitB) = RunRunnerNoCache(testsDir, absentPackageCache);
        Assert.True(exitB == 0 && outputB.Contains("1P/0F/0E"), $"run B must pass:\n{outputB}");

        // The decisive assertion. Identical content, identical key, a second --no-cache run:
        // it must STILL recompute. A HIT means workspace-deps was served from the shared real
        // cache that run A wrote — which is what "--no-cache disables the AL-output cache and
        // nothing else" did.
        Assert.True(!outputB.Contains("[source-dep] cache HIT NoCache Dep App"),
            $"a second --no-cache run over identical content must still MISS workspace-deps. A HIT " +
            $"means the cache is still being read from ~/.cache/al-runner despite --no-cache:\n{outputB}");
        Assert.Contains("[source-dep] WROTE NoCache Dep App", outputB);

        // Each run gets its OWN throwaway root. A shared "no-cache" directory would just be a
        // cache under another name — run B would be served everything run A computed.
        Assert.NotEqual(ThrowawayRootFrom(outputA), ThrowawayRootFrom(outputB));
    }

    private static string ThrowawayRootFrom(string output)
    {
        const string marker = "[cache] --no-cache: every on-disk cache redirected to ";
        var at = output.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the --no-cache announcement is missing from the run output:\n{output}");
        var rest = output[(at + marker.Length)..];
        return rest[..rest.IndexOf(" for this run", StringComparison.Ordinal)];
    }
}
