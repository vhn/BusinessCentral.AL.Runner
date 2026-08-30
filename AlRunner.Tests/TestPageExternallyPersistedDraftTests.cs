// A TestPage field validation can persist its draft through another record variable. The
// TestPage client must then adopt that saved row at its next save boundary; attempting to
// insert the stale client buffer again either raises a duplicate-key error or loses values
// that server-side AL wrote after the insert.

using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public sealed class TestPageExternallyPersistedDraftTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "al-runner-testpage-external-draft", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private void WriteBundle()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.json"), """
        {
          "id": "fb62936e-42d6-4b58-ae65-733c1749a480",
          "name": "Runner Mechanism - TestPage Externally Persisted Draft",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 62440, "to": 62449 } ],
          "runtime": "14.0"
        }
        """);

        File.WriteAllText(Path.Combine(_root, "EpdRow.Table.al"), """
        table 62440 "Epd Row"
        {
            DataClassification = CustomerContent;

            fields
            {
                field(1; Code; Code[10]) { }
                field(2; Input; Text[30])
                {
                    trigger OnValidate()
                    var
                        Persisted: Record "Epd Row";
                    begin
                        Persisted := Rec;
                        Persisted.Insert();
                        Persisted."Server Value" := 'server';
                        Persisted.Modify();
                    end;
                }
                field(3; "Server Value"; Text[30]) { }
            }

            keys
            {
                key(PK; Code) { Clustered = true; }
            }
        }
        """);

        File.WriteAllText(Path.Combine(_root, "EpdCard.Page.al"), """
        page 62441 "Epd Card"
        {
            PageType = Card;
            SourceTable = "Epd Row";
            ApplicationArea = All;

            layout
            {
                area(Content)
                {
                    field(RowCode; Rec.Code) { ApplicationArea = All; }
                    field(RowInput; Rec.Input) { ApplicationArea = All; }
                    field(ServerValue; Rec."Server Value") { ApplicationArea = All; }
                }
            }

            actions
            {
                area(Processing)
                {
                    action(Probe)
                    {
                        ApplicationArea = All;

                        trigger OnAction()
                        begin
                            if Rec."Server Value" <> 'server' then
                                Error('the page did not adopt the server-side value before OnAction');
                        end;
                    }
                }
            }
        }
        """);

        File.WriteAllText(Path.Combine(_root, "EpdTests.Codeunit.al"), """
        codeunit 62442 "Epd Tests"
        {
            Subtype = Test;
            TestPermissions = Disabled;

            [Test]
            procedure SaveBoundaryAdoptsRowPersistedDuringValidation()
            var
                Row: Record "Epd Row";
                Card: TestPage "Epd Card";
            begin
                Row.DeleteAll();

                Card.OpenEdit();
                Card.New();
                Card.RowCode.SetValue('R1');
                Card.RowInput.SetValue('client');
                Card.Probe.Invoke();
                Card.Close();

                Row.Get('R1');
                if Row.Input <> 'client' then
                    Error('the persisted row lost the client input');
                if Row."Server Value" <> 'server' then
                    Error('the persisted row lost the server-side value');
                if Row.Count() <> 1 then
                    Error('the stale TestPage draft created a duplicate row');
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
    public void SaveBoundary_AdoptsDraftPersistedAndModifiedByValidation()
    {
        TestArtifacts.SkipIfMissing();
        WriteBundle();

        var (output, exitCode) = RunBundle();

        Assert.True(exitCode == 0, $"Expected the bundle to pass; exit={exitCode}\n{output}");
        Assert.Contains(
            "PASS  Codeunit62442.SaveBoundaryAdoptsRowPersistedDuringValidation", output);
        Assert.DoesNotContain("FAIL", output);
    }
}
