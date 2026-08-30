using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public sealed class ApplicationAreaCacheInvalidationTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "al-runner-application-area-cache", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void WriteBundle()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.json"), """
        {
          "id": "65f060ff-dc4e-49a5-8e20-4f0574151344",
          "name": "Runner Mechanism - Application Area Cache",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [
            {
              "id": "5d86850b-0d76-4eca-bd7b-951ad998e997",
              "publisher": "Microsoft",
              "name": "Tests-TestLibraries",
              "version": "28.1.0.0"
            },
            {
              "id": "23de40a6-dfe8-4f80-80db-d70f83ce8caf",
              "publisher": "Microsoft",
              "name": "Test Runner",
              "version": "28.1.0.0"
            }
          ],
          "platform": "28.0.0.0",
          "application": "28.1.0.0",
          "runtime": "17.0",
          "target": "OnPrem",
          "idRanges": [ { "from": 62890, "to": 62890 } ]
        }
        """);

        File.WriteAllText(Path.Combine(_root, "ApplicationAreaCacheTests.Codeunit.al"), """
        codeunit 62890 "Application Area Cache Tests"
        {
            Subtype = Test;
            TestPermissions = Disabled;

            [Test]
            procedure TableEventsInvalidateApplicationAreaCache()
            var
                ApplicationAreaMgmt: Codeunit "Application Area Mgmt.";
                LibraryApplicationArea: Codeunit "Library - Application Area";
            begin
                LibraryApplicationArea.EnableVATSetup();
                if not ApplicationAreaMgmt.IsVATEnabled() then
                    Error('The inserted VAT setup did not invalidate the application-area cache.');

                LibraryApplicationArea.EnableSalesTaxSetup();
                if not ApplicationAreaMgmt.IsSalesTaxEnabled() then
                    Error('The inserted Sales Tax setup did not invalidate the application-area cache.');
            end;

        }
        """);

    }

    private (string Output, int ExitCode) RunBundle(string platformApps, string testApps)
    {
        var args = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
        args.Append(TestBuildConfig.BcVersionArg);
        args.Append(" --package-cache \"").Append(platformApps).Append('"');
        args.Append(" --package-cache \"").Append(testApps).Append('"');
        args.Append(" --cache \"").Append(Path.Combine(_root, "cache")).Append('"');
        args.Append(" --test Codeunit62890 \"").Append(_root).Append('"');

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot,
        };
        var output = new StringBuilder();
        using var process = Process.Start(startInfo)!;
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(300_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("runner hung while checking application-area cache invalidation");
        }
        process.WaitForExit();
        lock (output) return (output.ToString(), process.ExitCode);
    }

    [SkippableFact]
    public void PrecompiledTableSubscribers_AreInjectedWhenPublisherMetadataLoadsLazily()
    {
        TestArtifacts.SkipIfMissing();
        var platformApps = TestArtifacts.PlatformAppsDir();
        var testApps = Path.Combine(TestArtifacts.HomeDir() ?? string.Empty, ".al-runner", "test-apps");
        TestArtifacts.SkipIfDirectoryMissing(platformApps, "CI-style platform apps");
        TestArtifacts.SkipIfDirectoryMissing(testApps, "CI-style test apps");
        WriteBundle();

        var (output, exitCode) = RunBundle(platformApps, testApps);

        Assert.True(exitCode == 0,
            $"Expected lazily loaded precompiled table subscribers to invalidate the cache; exit={exitCode}\n{output}");
        Assert.Contains("1P/0F/0E across 1 tests", output);
    }
}
