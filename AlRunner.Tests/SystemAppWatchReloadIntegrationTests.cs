using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// Runs the real watch loop across the boundary that lost Microsoft SystemPackage table
/// metadata. Cycle 2 is load-bearing: cycle 1 has just registered the package, while cycle 2
/// has cleared all bundle metadata in the same resident process before executing the AL tests.
/// </summary>
[Collection("server-serial")]
public sealed class SystemAppWatchReloadIntegrationTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");
    private static readonly string FixtureSource = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "SystemAppWatchReload"));

    private const string PositiveTest =
        "PASS  Codeunit72501.TenantMediaHasMicrosoftSystemPackageField";
    private const string NegativeTest =
        "PASS  Codeunit72501.TenantMediaDoesNotInventMissingFields";

    [SkippableFact]
    public async Task Watch_CycleTwoRetainsMicrosoftSystemPackageTableShape()
    {
        TestArtifacts.SkipIfMissing();

        var bundle = Path.Combine(
            Path.GetTempPath(), "al-runner-systemapp-watch", Guid.NewGuid().ToString("N"));
        CopyTree(FixtureSource, bundle);
        var testSource = Path.Combine(bundle, "SystemAppWatchTests.Codeunit.al");

        var arguments = new StringBuilder(
            TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
            + $" \"{bundle}\" --watch --no-cache");
        var platformApps = TestArtifacts.PlatformAppsDir();
        if (Directory.Exists(platformApps))
            arguments.Append($" --package-cache \"{platformApps}\"");

        var lines = new List<string>();
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot,
        })!;

        void Pump(StreamReader reader) => Task.Run(async () =>
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
                lock (lines) lines.Add(line);
        });
        Pump(process.StandardOutput);
        Pump(process.StandardError);

        async Task<int> WaitForMarkerAfter(int fromIndex, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (lines)
                    for (var i = fromIndex; i < lines.Count; i++)
                        if (lines[i].Contains("[watch] waiting for AL source", StringComparison.Ordinal))
                            return i;
                if (process.HasExited)
                {
                    await Task.Delay(500);
                    throw new TimeoutException(
                        $"Watch process exited {process.ExitCode} before the idle marker.\n{Dump()}");
                }
                await Task.Delay(200);
            }
            throw new TimeoutException($"Watch process did not become idle.\n{Dump()}");
        }

        string Segment(int from, int to)
        {
            lock (lines)
                return string.Join('\n', lines.GetRange(from, Math.Max(0, to - from)));
        }

        string Dump()
        {
            lock (lines) return string.Join('\n', lines);
        }

        static void AssertCycle(string cycle, int number)
        {
            Assert.Contains(PositiveTest, cycle);
            Assert.Contains(NegativeTest, cycle);
            Assert.DoesNotContain("FAIL  Codeunit72501", cycle);
            Assert.DoesNotContain("ERROR Codeunit72501", cycle);
            Assert.False(
                cycle.Contains("BcAppFallback: indexed", StringComparison.Ordinal),
                $"Watch cycle {number} rebuilt the global BC table index while restoring "
                + $"SystemPackage metadata.\n--- cycle {number} ---\n{cycle}");
        }

        try
        {
            var cycleOneEnd = await WaitForMarkerAfter(0, TimeSpan.FromSeconds(240));
            AssertCycle(Segment(0, cycleOneEnd), 1);

            var source = await File.ReadAllTextAsync(testSource);
            var edited = source.Replace(
                "EDIT-MARKER-V1", $"EDIT-MARKER-V2 {Guid.NewGuid():N}", StringComparison.Ordinal);
            Assert.NotEqual(source, edited);
            await File.WriteAllTextAsync(testSource, edited);

            var cycleTwoEnd = await WaitForMarkerAfter(cycleOneEnd + 1, TimeSpan.FromSeconds(240));
            AssertCycle(Segment(cycleOneEnd + 1, cycleTwoEnd), 2);
        }
        finally
        {
            try { process.Kill(true); } catch { }
            try { Directory.Delete(bundle, recursive: true); } catch { }
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal));
    }
}
