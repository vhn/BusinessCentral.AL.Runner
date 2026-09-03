// CodeunitRunCommitPointTests — pins WHICH Codeunit.Run form is a commit point.
//
// The regression this guards: RunCodeunitInTransaction bracketed EVERY Codeunit.Run with
// ALDatabasePatches.Begin/EndCodeunitRunTransaction. Those reach
// RecordPatches.NoteTransactionBegin/End, which call MarkCommitPoint() at the outermost
// frame, and MarkCommitPoint() clears _txCommitPoint — so every write made BEFORE the call
// stopped being restorable and wrongly survived a later, unrelated asserterror.
//
// BC-observable claim, verified against a live BC 28.2 service tier (Test Runner -
// Isol. Codeunit 130450): a row inserted before a STATEMENT-FORM Codeunit.Run whose OnRun is
// empty is rolled back by a later asserterror, identically to the control with no Run at all;
// a row written by the callee is rolled back too. BC's DoRunAsync branches on DataError —
// the statement form (ThrowError) takes the plain BeginTransaction branch, where a nested
// begin is only a TransactionCount bump and EndTransaction at depth > 0 merely pops it, so
// nothing is committed.
//
// The GUARDED form is the contrast, and the second test is deliberately asymmetric: on real
// BC `Ok := Codeunit.Run(...)` inside an open write transaction THROWS
// (TransactionManager.ThrowIfWriteTransactionStarted) rather than committing. The runner does
// not model that refusal — it is lenient and makes the caller's pending writes durable
// instead. That leniency is pre-existing and out of scope here; what matters is that the
// guarded form remains a commit boundary. The two tests together pin the gate from both
// sides: revert the gating to unconditional and the first fails; drop the bracketing
// altogether and the second fails.
//
// Runs as a self-contained fixture (no Base App, no dependencies) so it costs a couple of
// seconds and cannot be perturbed by corpus or artifact drift.

using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public sealed class CodeunitRunCommitPointTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private readonly string _root;

    public CodeunitRunCommitPointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "al-runner-codeunit-run-commit-point", Guid.NewGuid().ToString("N"));
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
          "id": "b1f4c0d2-9e77-4a51-8c3e-2d6a5f0b7c31",
          "name": "Codeunit Run Commit Point Fixture",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 62730, "to": 62739 } ],
          "runtime": "14.0"
        }
        """);
        File.WriteAllText(Path.Combine(dir, "Marker.Table.al"), """
        table 62730 "Codeunit Run Commit Marker"
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
        File.WriteAllText(Path.Combine(dir, "EmptyOnRun.Codeunit.al"), """
        codeunit 62731 "Codeunit Run Empty OnRun"
        {
            trigger OnRun()
            begin
            end;
        }
        """);
        // Each [Test] PASSES when the runner matches BC, and fails with a distinctive
        // REGRESSION message otherwise, so a failure names the defect rather than a diff.
        File.WriteAllText(Path.Combine(dir, "CommitPointTests.Codeunit.al"), """
        codeunit 62732 "Codeunit Run Commit Tests"
        {
            Subtype = Test;

            [Test]
            procedure Control_NoRun_WriteRollsBack()
            var
                Marker: Record "Codeunit Run Commit Marker";
            begin
                Marker.Id := 1;
                Marker.Insert();
                asserterror Error('intentional');
                if Marker.Get(1) then
                    Error('REGRESSION: asserterror did not roll back a plain write.');
            end;

            [Test]
            procedure StatementFormRun_IsNotACommitPoint()
            var
                Marker: Record "Codeunit Run Commit Marker";
            begin
                Marker.Id := 2;
                Marker.Insert();
                Codeunit.Run(Codeunit::"Codeunit Run Empty OnRun");
                asserterror Error('intentional');
                if Marker.Get(2) then
                    Error('REGRESSION: a statement-form Codeunit.Run marked a commit point, so the earlier write survived asserterror. Real BC rolls it back.');
            end;

            [Test]
            procedure GuardedFormRun_IsACommitPoint()
            var
                Marker: Record "Codeunit Run Commit Marker";
                Ok: Boolean;
            begin
                Marker.Id := 3;
                Marker.Insert();
                Ok := Codeunit.Run(Codeunit::"Codeunit Run Empty OnRun");
                if not Ok then
                    Error('REGRESSION: guarded Codeunit.Run on an empty OnRun returned false.');
                asserterror Error('intentional');
                if not Marker.Get(3) then
                    Error('REGRESSION: the guarded Codeunit.Run stopped being a commit point.');
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
            throw new TimeoutException("runner hung while checking Codeunit.Run commit-point behavior");
        }
        process.WaitForExit();
        lock (output) return (output.ToString(), process.ExitCode);
    }

    [SkippableFact]
    public void OnlyTheGuardedCodeunitRunIsACommitPoint()
    {
        TestArtifacts.SkipIfMissing();

        var (output, exitCode) = RunRunner();

        // Assert.True with the whole output as the message, not Assert.DoesNotContain: xunit
        // clips the matched substring, which would hide the REGRESSION sentence naming the
        // defect. On failure CI should print the diagnosis, not a re-run instruction.
        Assert.True(!output.Contains("REGRESSION:"), output);
        Assert.Contains("3P/0F/0E across 3 tests", output);
        Assert.Equal(0, exitCode);
    }
}
