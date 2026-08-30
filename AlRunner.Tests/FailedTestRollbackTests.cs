using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public sealed class FailedTestRollbackTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private readonly string _root;

    public FailedTestRollbackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "al-runner-failed-test-rollback", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        WriteFixture(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static void WriteFixture(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "app.json"), """
        {
          "id": "df49b293-cbaa-4b87-81b4-d4b3072737ec",
          "name": "Failed Test Rollback Fixture",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 62720, "to": 62729 } ],
          "runtime": "14.0"
        }
        """);
        File.WriteAllText(Path.Combine(dir, "Marker.Table.al"), """
        table 62720 "Failed Test Marker"
        {
            fields
            {
                field(1; Id; Integer) { }
            }
            keys
            {
                key(PK; Id) { Clustered = true; }
            }
        }
        """);
        File.WriteAllText(Path.Combine(dir, "RollbackTests.Codeunit.al"), """
        codeunit 62721 "Failed Test Rollback Tests"
        {
            Subtype = Test;

            [Test]
            procedure Step1_WritesThenFails()
            var
                Marker: Record "Failed Test Marker";
            begin
                Marker.Id := 1;
                Marker.Insert();
                Error('intentional first-test failure');
            end;

            [Test]
            procedure Step2_ReusesRolledBackKey()
            var
                Marker: Record "Failed Test Marker";
            begin
                Marker.Id := 1;
                Marker.Insert();
            end;
        }
        """);
    }

    private (string Output, int ExitCode) RunRunner()
    {
        var args = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
        args.Append(TestBuildConfig.BcVersionArg);
        args.Append(" --isolation codeunit");
        args.Append($" \"{_root}\"");
        var psi = new ProcessStartInfo
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
        using var process = Process.Start(psi)!;
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(240_000))
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException("runner hung while checking failed-test rollback");
        }
        process.WaitForExit();
        lock (output) return (output.ToString(), process.ExitCode);
    }

    [SkippableFact]
    public void CodeunitIsolation_RollsBackWritesFromFailedTestBeforeNextTest()
    {
        TestArtifacts.SkipIfMissing();

        var (output, exitCode) = RunRunner();

        Assert.Equal(1, exitCode);
        Assert.Contains("1P/1F/0E across 2 tests", output);
        Assert.Contains("intentional first-test failure", output);
        Assert.DoesNotContain("already exists", output);
    }
}
