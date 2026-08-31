using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public sealed class MediaPngImportIntegrationTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");
    private static readonly string FixturePath = Path.Combine(
        RepoRoot, "AlRunner.Tests", "Fixtures", "MediaPngImport");

    [SkippableFact]
    public void ImportStream_UsesMicrosoftMediaContainerAndPreservesPngBytes()
    {
        TestArtifacts.SkipIfMissing();
        var platformApps = TestArtifacts.PlatformAppsDir();
        TestArtifacts.SkipIfDirectoryMissing(platformApps, "BC platform apps");

        var args = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
        args.Append(TestBuildConfig.BcVersionArg);
        args.Append(" --package-cache \"").Append(platformApps).Append('"');
        args.Append(" --no-auto-provision --test 72491");
        args.Append($" \"{FixturePath}\"");

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
        if (!process.WaitForExit(240_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("runner hung while exercising PNG Media.ImportStream");
        }
        process.WaitForExit();

        var text = output.ToString();
        Assert.True(process.ExitCode == 0, text);
        Assert.Contains(
            "PASS  Codeunit72491.ImportPng_PreservesTheNstSaveStreamBytes",
            text,
            StringComparison.Ordinal);
    }
}
