using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public sealed class CompanyVirtualTableTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "al-runner-company-virtual-table", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private void WriteBundle()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.json"), """
        {
          "id": "f54501c1-2063-47c9-aa93-84aaf0f6e4c8",
          "name": "Runner Mechanism - Company Virtual Table",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 62470, "to": 62479 } ],
          "runtime": "14.0"
        }
        """);

        File.WriteAllText(Path.Combine(_root, "CvtTests.Codeunit.al"), """
        codeunit 62470 "Cvt Tests"
        {
            Subtype = Test;
            TestPermissions = Disabled;

            [Test]
            procedure CurrentCompanyExistsInCompanyVirtualTable()
            var
                Company: Record Company;
            begin
                Company.SetRange(Name, CompanyName());
                if not Company.FindFirst() then
                    Error('Expected current company %1 in the Company virtual table', CompanyName());
            end;
        }
        """);
    }

    private (string Output, int ExitCode) RunBundle()
    {
        var args = new StringBuilder(
            TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg + $" \"{_root}\"");
        var platformApps = TestArtifacts.PlatformAppsDir();
        if (Directory.Exists(platformApps))
            args.Append($" \"--package-cache\" \"{platformApps}\"");

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
        if (!process.WaitForExit(600_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("runner hung");
        }
        process.WaitForExit();
        lock (output) return (output.ToString(), process.ExitCode);
    }

    [SkippableFact]
    public void CompanyVirtualTable_ContainsCurrentCompany()
    {
        TestArtifacts.SkipIfMissing();
        WriteBundle();

        var (output, exitCode) = RunBundle();

        Assert.True(exitCode == 0, $"Expected the bundle to pass; exit={exitCode}\n{output}");
        Assert.Contains("PASS  Codeunit62470.CurrentCompanyExistsInCompanyVirtualTable", output);
        Assert.DoesNotContain("FAIL", output);
    }
}
