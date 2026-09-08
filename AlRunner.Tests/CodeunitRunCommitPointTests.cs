// CodeunitRunCommitPointTests — pins WHICH Codeunit.Run form is a commit point.
//
// BC branches on DataError: the GUARDED form (`Ok := Codeunit.Run(...)`) takes
// BeginTransactionWorldAndTransaction and is a real transaction boundary; the STATEMENT form
// takes the plain BeginTransaction branch, which inside a test method only bumps
// TransactionCount on the already-open transaction and commits nothing. The runner bracketed
// both, so a write made before a statement-form run wrongly survived a later asserterror.
//
// Both claims are adjudicated upstream in the al-language corpus (see
// docs/upstream-corpus-workflow.md); these fixtures are the runner-side pin for the gating in
// CodeunitPatches.RunCodeunitInTransaction, kept local because they must fail fast on the
// mechanism rather than wait for a corpus pin bump.
//
// Every scenario here is BC-legal: no test enters a guarded run with an uncommitted write,
// which BC refuses via ThrowIfWriteTransactionStarted and the runner does not yet model
// (docs/limitations.md). That keeps these fixtures green when the refusal is eventually ported.

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
        File.WriteAllText(Path.Combine(dir, "MarkerWriter.Codeunit.al"), """
        codeunit 62733 "Codeunit Run Marker Writer"
        {
            trigger OnRun()
            var
                Marker: Record "Codeunit Run Commit Marker";
            begin
                Marker.Id := 3;
                Marker.Insert();
            end;
        }
        """);
        File.WriteAllText(Path.Combine(dir, "MarkerWriter2.Codeunit.al"), """
        codeunit 62734 "Codeunit Run Marker Writer2"
        {
            trigger OnRun()
            var
                Marker: Record "Codeunit Run Commit Marker";
            begin
                Marker.Id := 5;
                Marker.Insert();
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
                if not Marker.Get(1) then
                    Error('SETUP: the row was never written, so "rolled back" below would prove nothing.');
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
                if not Marker.Get(2) then
                    Error('SETUP: the row was never written, so "rolled back" below would prove nothing.');
                Codeunit.Run(Codeunit::"Codeunit Run Empty OnRun");
                asserterror Error('intentional');
                if Marker.Get(2) then
                    Error('REGRESSION: a statement-form Codeunit.Run marked a commit point, so the earlier write survived asserterror. Real BC rolls it back.');
            end;

            // The instance spelling enters through NavCodeunit_DoRunAsync, the static one
            // through NavCodeunit_RunCodeunit. Both funnel into RunCodeunitInTransaction today;
            // pinning both stops a per-entry-point refactor from regressing one silently.
            [Test]
            procedure StatementFormInstanceRun_IsNotACommitPoint()
            var
                Marker: Record "Codeunit Run Commit Marker";
                EmptyOnRun: Codeunit "Codeunit Run Empty OnRun";
            begin
                Marker.Id := 4;
                Marker.Insert();
                if not Marker.Get(4) then
                    Error('SETUP: the row was never written, so "rolled back" below would prove nothing.');
                EmptyOnRun.Run();
                asserterror Error('intentional');
                if Marker.Get(4) then
                    Error('REGRESSION: a statement-form instance Codeunit.Run marked a commit point, so the earlier write survived asserterror. Real BC rolls it back.');
            end;

            // BC-legal: nothing is pending when the guarded run starts, so BC does not refuse
            // it. The callee writes INSIDE the world, so that write is committed and a later
            // asserterror must not undo it. Drop the bracketing entirely and the callee's row
            // is snapshotted against the test-start commit point and rolled back instead.
            // Pinned on both spellings for the same reason as the statement form above.
            [Test]
            procedure GuardedFormRun_IsACommitPoint()
            var
                Marker: Record "Codeunit Run Commit Marker";
                Ok: Boolean;
            begin
                Ok := Codeunit.Run(Codeunit::"Codeunit Run Marker Writer");
                if not Ok then
                    Error('REGRESSION: guarded Codeunit.Run returned false.');
                asserterror Error('intentional');
                if not Marker.Get(3) then
                    Error('REGRESSION: the guarded Codeunit.Run stopped being a commit point.');
            end;

            // BC's test framework commits between test methods, and a commit ends the write
            // transaction. These two run in declaration order: the first deliberately leaves an
            // uncommitted write behind, the second asserts the next method does not inherit it.
            [Test]
            procedure LeaveUncommittedWriteForTheNextTest()
            var
                Marker: Record "Codeunit Run Commit Marker";
            begin
                Marker.Id := 6;
                Marker.Insert();
            end;

            [Test]
            procedure NextTestStartsOutsideAWriteTransaction()
            begin
                if Database.IsInWriteTransaction() then
                    Error('REGRESSION: an uncommitted write in the previous test method left the session in a write transaction; the per-test commit must end it, as BC does.');
            end;

            [Test]
            procedure GuardedFormInstanceRun_IsACommitPoint()
            var
                Marker: Record "Codeunit Run Commit Marker";
                MarkerWriter: Codeunit "Codeunit Run Marker Writer2";
                Ok: Boolean;
            begin
                Ok := MarkerWriter.Run();
                if not Ok then
                    Error('REGRESSION: guarded instance Codeunit.Run returned false.');
                asserterror Error('intentional');
                if not Marker.Get(5) then
                    Error('REGRESSION: the guarded instance Codeunit.Run stopped being a commit point.');
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

        // Assert.True with the whole output as the message, not Assert.DoesNotContain /
        // Assert.Contains: xunit clips the matched string, which would hide both the REGRESSION
        // sentence naming the defect and any AL compiler diagnostic behind a compile failure.
        Assert.True(!output.Contains("REGRESSION:"), output);
        Assert.True(output.Contains("7P/0F/0E across 7 tests"), output);
        Assert.True(exitCode == 0, output);
    }
}
