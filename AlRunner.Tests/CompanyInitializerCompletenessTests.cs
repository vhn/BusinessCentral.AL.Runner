using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public class CompanyInitializerCompletenessTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    [SkippableFact]
    public void CompanyInitializeCreatesSourceCodeSetup()
    {
        TestArtifacts.SkipIfMissing();

        var root = Path.Combine(Path.GetTempPath(), "al-runner-company-initialize", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "app.json"), """
            {
              "id": "89b73cf4-23e1-4ccb-b53d-785af6d61745",
              "name": "Company Initialize Completeness",
              "publisher": "AlRunnerTests",
              "version": "1.0.0.0",
              "dependencies": [],
              "platform": "1.0.0.0",
              "application": "1.0.0.0",
              "idRanges": [ { "from": 62700, "to": 62709 } ],
              "runtime": "14.0"
            }
            """);
            File.WriteAllText(Path.Combine(root, "Tests.al"), """
            codeunit 62700 "Company Init Completeness"
            {
                Subtype = Test;

                [Test]
                procedure SourceCodeSetupExists()
                var
                    SourceCodeSetup: Record "Source Code Setup";
                begin
                    SourceCodeSetup.Get();
                end;

                [Test]
                procedure TrappedCodeunitRunExposesLastError()
                begin
                    ClearLastError();
                    if Codeunit.Run(Codeunit::"Company Init Failure Probe") then
                        Error('The failure probe unexpectedly succeeded.');
                    if StrPos(GetLastErrorText(), 'expected trapped failure') = 0 then
                        Error('Codeunit.Run did not preserve the trapped error: %1', GetLastErrorText());
                end;

                [Test]
                procedure TrappedCodeunitRunRestoresTableAndRecordState()
                var
                    Probe: Record "Codeunit Run Tx Probe";
                begin
                    Probe."No." := 'TRAP';
                    Probe.Value := 'before';
                    Probe.Insert();
                    Commit();

                    if Codeunit.Run(Codeunit::"Codeunit Run Rollback Probe", Probe) then
                        Error('The rollback probe unexpectedly succeeded.');
                    if Probe."No." <> 'TRAP' then
                        Error('The trapped run did not restore the passed record handle.');
                    Probe.Get('TRAP');
                    if Probe.Value <> 'before' then
                        Error('The trapped run left its table write behind: %1', Probe.Value);
                end;

                [Test]
                procedure SuccessfulCodeunitRunCommitsItsWrites()
                var
                    Probe: Record "Codeunit Run Tx Probe";
                begin
                    Probe."No." := 'SUCCESS';
                    Probe.Value := 'before';
                    Probe.Insert();
                    Commit();

                    if not Codeunit.Run(Codeunit::"Codeunit Run Commit Probe", Probe) then
                        Error('The commit probe unexpectedly failed.');
                    asserterror Error('later unrelated failure');

                    Probe.Get('SUCCESS');
                    if Probe.Value <> 'committed' then
                        Error('The successful run was not committed: %1', Probe.Value);
                end;

                [Test]
                procedure FailedOuterCodeunitRunRollsBackNestedSuccess()
                var
                    Probe: Record "Codeunit Run Tx Probe";
                begin
                    Probe."No." := 'NESTED';
                    Probe.Value := 'before';
                    Probe.Insert();
                    Commit();

                    if Codeunit.Run(Codeunit::"Codeunit Run Outer Rollback Probe", Probe) then
                        Error('The outer rollback probe unexpectedly succeeded.');
                    Probe.Get('NESTED');
                    if Probe.Value <> 'before' then
                        Error('A nested successful run escaped the outer rollback: %1', Probe.Value);
                end;

                [Test]
                procedure FailedOuterCodeunitRunRollsBackNestedXmlPortSuccess()
                var
                    Probe: Record "Codeunit Run Tx Probe";
                begin
                    if Codeunit.Run(Codeunit::"Codeunit Run XmlPort Probe") then
                        Error('The outer XMLPort rollback probe unexpectedly succeeded.');
                    if Probe.Get('XMLPORT') then
                        Error('A nested successful XMLPort import escaped the outer rollback.');
                end;
            }

            codeunit 62701 "Company Init Failure Probe"
            {
                trigger OnRun()
                begin
                    Error('expected trapped failure');
                end;
            }

            table 62702 "Codeunit Run Tx Probe"
            {
                fields
                {
                    field(1; "No."; Code[20]) { }
                    field(2; Value; Text[50]) { }
                }

                keys
                {
                    key(PK; "No.") { Clustered = true; }
                }
            }

            codeunit 62703 "Codeunit Run Rollback Probe"
            {
                TableNo = "Codeunit Run Tx Probe";

                trigger OnRun()
                begin
                    Rec.Value := 'during';
                    Rec.Modify();
                    Rec."No." := 'MUTATED';
                    Error('expected rollback');
                end;
            }

            codeunit 62704 "Codeunit Run Commit Probe"
            {
                TableNo = "Codeunit Run Tx Probe";

                trigger OnRun()
                begin
                    Rec.Value := 'committed';
                    Rec.Modify();
                end;
            }

            codeunit 62705 "Codeunit Run Outer Rollback Probe"
            {
                TableNo = "Codeunit Run Tx Probe";

                trigger OnRun()
                begin
                    Codeunit.Run(Codeunit::"Codeunit Run Nested Commit Probe", Rec);
                    Error('expected outer rollback');
                end;
            }

            codeunit 62706 "Codeunit Run Nested Commit Probe"
            {
                TableNo = "Codeunit Run Tx Probe";

                trigger OnRun()
                begin
                    Rec.Value := 'nested';
                    Rec.Modify();
                end;
            }

            xmlport 62707 "Codeunit Run Tx XmlPort"
            {
                Direction = Import;
                UseRequestPage = false;

                schema
                {
                    textelement(root)
                    {
                        tableelement(Probe; "Codeunit Run Tx Probe")
                        {
                            XmlName = 'Probe';
                            fieldelement(No; Probe."No.") { }
                            fieldelement(Value; Probe.Value) { }
                        }
                    }
                }
            }

            codeunit 62708 "Codeunit Run XmlPort Probe"
            {
                trigger OnRun()
                var
                    TempBlob: Codeunit "Temp Blob";
                    DocumentOutStream: OutStream;
                    DocumentInStream: InStream;
                begin
                    TempBlob.CreateOutStream(DocumentOutStream);
                    DocumentOutStream.WriteText('<?xml version="1.0" encoding="utf-8"?><root><Probe><No>XMLPORT</No><Value>nested</Value></Probe></root>');
                    TempBlob.CreateInStream(DocumentInStream);
                    if not XmlPort.Import(XmlPort::"Codeunit Run Tx XmlPort", DocumentInStream) then
                        Error('The nested XMLPort import unexpectedly failed: %1', GetLastErrorText());
                    Error('expected outer rollback');
                end;
            }
            """);

            var args = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
            args.Append(TestBuildConfig.BcVersionArg);
            args.Append(" --package-cache \"").Append(TestArtifacts.PlatformAppsDir()).Append('"');
            args.Append(" \"").Append(root).Append('"');

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = args.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = RepoRoot,
                Environment = { ["AL_RUNNER_NO_DEP_COMPANY_CACHE"] = "1" },
            };
            var output = new StringBuilder();
            using var process = Process.Start(psi)!;
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(180_000))
            {
                try { process.Kill(true); } catch { }
                throw new TimeoutException("runner hung");
            }
            process.WaitForExit();

            string captured;
            lock (output) captured = output.ToString();
            Assert.True(process.ExitCode == 0,
                $"Company-Initialize did not seed Source Code Setup:\n{captured}");
            Assert.Contains("6P/0F/0E", captured);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
