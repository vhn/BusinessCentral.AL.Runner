using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The CLI's self-documentation is the ONLY thing an agent handed a bare
/// al-runner binary can read. If a flag is implemented but undocumented, or the
/// help contradicts itself, the agent invents an explanation for the resulting
/// failure — which is how "al-runner cannot run the suite (a rewriter
/// limitation)" got asserted about a runner that runs ~1000 Pageworks tests.
///
/// These tests pin the help/guide surface against drift. They exercise the real
/// CLI (both flags are handled before any BC type loads, so this is fast).
/// </summary>
// See DefineFlagIntegrationTests for why this used to be [Collection("server-serial")]
// and no longer is — #1809.
public sealed class CliDocumentationTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string CurrentFramework()
    {
        var v = Environment.Version;
        return $"net{v.Major}.{v.Minor}";
    }

    private static (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var argLine = TestBuildConfig.RunArgs(Path.Combine(RepoRoot, "AlRunner")) + $" {string.Join(' ', args)}";
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = argLine,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot,
        };
        // Drain BOTH pipes concurrently. Reading stdout to the end first and stderr only
        // afterwards deadlocks: the runner writes heavily to stderr ([Cecil], [cache],
        // [deps], [emit-timing]), so once that 64K pipe buffer fills the child blocks on
        // its stderr write, never exits, and never closes stdout — so ReadToEnd() on
        // stdout never returns and the WaitForExit timeout below is never even reached.
        //
        // The window is widest right after AlRunner is rebuilt: a new runner assembly
        // changes the Ncl Cecil cache key, so the next invocation does a fresh rewrite and
        // re-execs itself, and that re-exec child inherits these same pipes. That is the
        // multi-hour "suite hangs after Starting test execution" with al-runner children
        // parked in Process.WaitForExit().
        //
        // The other runner-subprocess tests already use this async pattern; this one was
        // the last holdout.
        using var proc = Process.Start(psi)!;
        var outSb = new StringBuilder();
        var errSb = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outSb) outSb.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errSb) errSb.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        if (!proc.WaitForExit(240_000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(
                $"al-runner did not exit within 240s for args: {string.Join(' ', args)}");
        }
        proc.WaitForExit(); // flush the async readers
        lock (outSb) lock (errSb) return (proc.ExitCode, outSb.ToString(), errSb.ToString());
    }

    /// <summary>
    /// Every long flag the argument parser recognises must appear in --help.
    ///
    /// Scraping Program.cs rather than reflecting is deliberate: the parser lives
    /// in top-level statements, so there is no symbol to reflect over. The scrape
    /// is what caught --auto-provision and --emit-app being implemented-but-
    /// undocumented, and it is what will catch the next one.
    /// </summary>
    [Fact]
    public void Help_DocumentsEveryRecognizedFlag()
    {
        var programSource = File.ReadAllText(Path.Combine(RepoRoot, "AlRunner", "Program.cs"));

        // Flags the parser compares against, minus the bare "--" argument separator.
        var recognized = Regex.Matches(programSource, "\"(--[a-z][a-z-]*)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Where(f => f != "--")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(recognized);

        var (exit, help, stderr) = RunCli("--help");
        Assert.True(exit == 0, $"--help must exit 0. exit={exit}\n{stderr}");

        var undocumented = recognized.Where(f => !help.Contains(f, StringComparison.Ordinal)).ToList();

        Assert.True(undocumented.Count == 0,
            "These flags are recognized by the parser but absent from --help. An agent "
            + "reading --help cannot discover them:\n  " + string.Join("\n  ", undocumented));
    }

    /// <summary>
    /// The `provision` subcommand is the only supported way to obtain BC artifacts
    /// (the runner never auto-downloads). If --help omits it, an agent on a machine
    /// without artifacts has no path forward.
    /// </summary>
    [Fact]
    public void Help_DocumentsProvisionSubcommand()
    {
        var (_, help, _) = RunCli("--help");
        Assert.Contains("provision", help, StringComparison.Ordinal);
    }

    /// <summary>
    /// --server is fully implemented (docs/server-mode.md, ServerTests.cs) but was
    /// also listed under "NOT YET IMPLEMENTED". A reader who hits the second list
    /// concludes the runner is crippled. Self-contradiction is worse than silence.
    /// </summary>
    [Fact]
    public void Help_DoesNotListImplementedFlagsAsUnimplemented()
    {
        var (_, help, _) = RunCli("--help");

        var idx = help.IndexOf("NOT YET IMPLEMENTED", StringComparison.Ordinal);
        Assert.True(idx >= 0, "--help should keep a 'NOT YET IMPLEMENTED' section.");
        var notYet = help[idx..];

        foreach (var implemented in new[] { "--server", "--watch", "--define", "--auto-provision", "--output-json", "--output-junit" })
            Assert.False(notYet.Contains(implemented, StringComparison.Ordinal),
                $"{implemented} is implemented but listed under 'NOT YET IMPLEMENTED'.");
    }

    /// <summary>
    /// --guide is advertised in CLAUDE.md and the al-runner-workflow skill. It must
    /// actually exist, and it must answer the questions an agent gets wrong when it
    /// only has the binary: how to invoke against a real app + test app, where deps
    /// come from, and what the common failure signatures mean.
    /// </summary>
    [Fact]
    public void Guide_ExistsAndCoversInvocationEssentials()
    {
        var (exit, guide, stderr) = RunCli("--guide");
        Assert.True(exit == 0, $"--guide must exit 0. exit={exit}\n{stderr}");

        foreach (var required in new[]
        {
            "INVOCATION",          // the minimal correct command line
            "DEPENDENCIES",        // where .app deps are resolved from
            "TROUBLESHOOTING",     // failure signature -> meaning
            "does not have a member with that ID",  // the ID-0 signature specifically
            "symbols-only",        // the distinction that makes "it compiled" not mean "it can run"
            "HIGHEST VERSION",     // how the winning package is actually chosen
            "[dep]",               // the mechanical check that replaces hand-auditing .app files
            "Do NOT infer it from the app under test",  // --bc-version is not the app's version
        })
            Assert.True(guide.Contains(required, StringComparison.Ordinal),
                $"--guide must cover '{required}'. Got:\n{guide}");
    }

    /// <summary>
    /// The guide must state plainly that the runner executes full suites. Absent a
    /// positive claim, an agent hitting any failure is free to conclude the runner
    /// "can't run tests" — which is the exact misdiagnosis this suite exists to stop.
    /// </summary>
    [Fact]
    public void Guide_StatesThatFullSuitesExecute()
    {
        var (_, guide, _) = RunCli("--guide");
        Assert.Contains("CAPABILITY", guide, StringComparison.Ordinal);
    }

    /// <summary>Negative: an unknown documentation flag must not be silently accepted.</summary>
    [Fact]
    public void UnknownFlag_IsRejected()
    {
        var (exit, _, stderr) = RunCli("--guied");
        Assert.True(exit != 0, "A misspelled flag must not exit 0.");
        Assert.Contains("--guied", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Delta compilation is what `--watch` does, so the help entry has to say so —
    /// otherwise the only description of the behaviour a developer sees is "re-run
    /// in-process", which undersells it and gives no name to search for when a cycle
    /// looks wrong.
    /// </summary>
    [Fact]
    public void Help_DescribesWatchAsObjectGranular()
    {
        var entry = FlagEntry(RunCli("--help").StdOut, "--watch");

        Assert.Contains("only the AL objects", entry, StringComparison.Ordinal);
    }

    /// <summary>
    /// `--print-cache-key` is sold as a cheap probe, and the help said it exits "before
    /// Emit+Compile starts". It does not: the short-circuit is inside the per-app-group
    /// loop, and the layered pre-pass that builds every dependency impl package FROM SOURCE
    /// has already run by then. Measured: a "cheap probe" of npcore spent 16.3 s compiling
    /// NP Retail into a .app before printing a key.
    ///
    /// The wording is the whole contract here — a caller who believes it and times the flag
    /// concludes the runner is pathologically slow at hashing files. Don't fix it by moving
    /// the short-circuit earlier: the key includes the resolved dependency set, so skipping
    /// the pre-pass would change the answer the flag exists to give.
    /// </summary>
    [Fact]
    public void Help_PrintCacheKey_DoesNotClaimItSkipsAllCompilation()
    {
        var entry = FlagEntry(RunCli("--help").StdOut, "--print-cache-key");

        Assert.DoesNotContain("before Emit+Compile starts", entry, StringComparison.Ordinal);
        Assert.Contains("dependenc", entry, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The invariant that makes the wording above correct, asserted against the source
    /// rather than trusted: the `--print-cache-key` short-circuit really does sit after the
    /// layered pre-pass call. If someone moves it before the pre-pass, this fails and the
    /// help text is the thing to change back.
    /// </summary>
    [Fact]
    public void PrintCacheKey_ShortCircuitsAfterTheLayeredPrePassHasAlreadyRun()
    {
        var programSource = File.ReadAllText(Path.Combine(RepoRoot, "AlRunner", "Program.cs"));

        var prePassCall = programSource.IndexOf("packageCacheDirs = RunLayeredPrePass(bundles", StringComparison.Ordinal);
        var shortCircuit = programSource.IndexOf("if (printCacheKeyOnly)", StringComparison.Ordinal);

        Assert.True(prePassCall > 0, "could not find the RunLayeredPrePass call site in Program.cs.");
        Assert.True(shortCircuit > 0, "could not find the --print-cache-key short-circuit in Program.cs.");
        Assert.True(prePassCall < shortCircuit,
            "the --print-cache-key short-circuit must come after the layered pre-pass; if that "
            + "changed, Help_PrintCacheKey_DoesNotClaimItSkipsAllCompilation needs updating too.");
    }

    /// <summary>
    /// `--version` is how a developer identifies which build they were handed, and the
    /// identifying part is the prerelease suffix (2.0.0-preview.1, 2.1.2-performance).
    /// AssemblyVersion is a numeric quad that cannot carry it, so reading that prints
    /// "2.0.0.0" for every build of the same numeric version — see RunnerVersionTests.
    /// </summary>
    [Fact]
    public void Version_PrintsTheFullInformationalVersion()
    {
        var runner = typeof(AlRunner.AppLoader).Assembly;
        var informational = runner
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        var (exit, stdout, stderr) = RunCli("--version");

        Assert.True(exit == 0, $"--version must exit 0. exit={exit}\n{stderr}");
        Assert.Contains($"al-runner v{informational}", stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// The `  --flag  description…` block for <paramref name="flag"/>, up to the next flag.
    /// </summary>
    private static string FlagEntry(string help, string flag)
    {
        var idx = help.IndexOf($"  {flag} ", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"--help must document {flag}.");
        var entry = help[idx..];
        var next = entry.IndexOf("\n  --", StringComparison.Ordinal);
        return next > 0 ? entry[..next] : entry;
    }
}
