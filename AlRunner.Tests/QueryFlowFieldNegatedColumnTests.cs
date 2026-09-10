using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

/// A negated FlowField (`-sum(...)`) read through a query column. BC carries the negation
/// twice — the calc core negates from `NegateResult`, and BC also stamps it as the synthesized
/// column's ReverseSign — so honouring both flips the sign back, silently.
///
/// Spawns the real runner; needs the BC artifact cache. Skips (no-op) when absent.
/// </summary>
public class QueryFlowFieldNegatedColumnTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private static (string output, int exit) RunRunner(string bundle)
    {
        var args = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
        args.Append(TestBuildConfig.BcVersionArg);
        args.Append(" \"").Append(bundle).Append('"');
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet", Arguments = args.ToString(),
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        var sb = new StringBuilder();
        var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(180_000)) { try { p.Kill(true); } catch { } throw new TimeoutException("runner hung"); }
        p.WaitForExit();
        lock (sb) return (sb.ToString(), p.ExitCode);
    }

    private static string WriteBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "al-runner-query-flowfield-negated", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "app.json"), """
        {
          "id": "c7d1e4f2-2301-4a1b-9c3d-000000002301",
          "name": "QFN Negated Repro",
          "publisher": "Repro2301",
          "version": "1.0.0.0",
          "dependencies": [],
          "idRanges": [ { "from": 62450, "to": 62459 } ],
          "runtime": "17.0",
          "platform": "28.0.0.0",
          "application": "28.0.0.0"
        }
        """);

        File.WriteAllText(Path.Combine(root, "QfnLine.al"), """
        table 62450 "QFN Line"
        {
            DataClassification = SystemMetadata;
            fields
            {
                field(1; "Entry No."; Integer) { }
                field(2; "Header No."; Code[20]) { }
                field(3; Amount; Decimal) { }
            }
            keys { key(PK; "Entry No.") { Clustered = true; } }
        }
        """);

        File.WriteAllText(Path.Combine(root, "QfnHeader.al"), """
        table 62451 "QFN Header"
        {
            DataClassification = SystemMetadata;
            fields
            {
                field(1; "No."; Code[20]) { }
                field(2; "Neg Total"; Decimal)
                {
                    FieldClass = FlowField;
                    CalcFormula = -sum("QFN Line".Amount where("Header No." = field("No.")));
                }
                field(3; HasLines; Boolean)
                {
                    FieldClass = FlowField;
                    CalcFormula = exist("QFN Line" where("Header No." = field("No.")));
                }
                field(4; NoLines; Boolean)
                {
                    FieldClass = FlowField;
                    CalcFormula = -exist("QFN Line" where("Header No." = field("No.")));
                }
            }
            keys { key(PK; "No.") { Clustered = true; } }
        }
        """);

        File.WriteAllText(Path.Combine(root, "QfnQuery.al"), """
        query 62452 "QFN Header Negated"
        {
            QueryType = Normal;
            elements
            {
                dataitem(QfnHeader; "QFN Header")
                {
                    column(No; "No.") { }
                    column(NegTotal; "Neg Total") { }
                    column(HasLines; HasLines) { }
                    column(NoLines; NoLines) { }
                }
            }
        }
        """);

        File.WriteAllText(Path.Combine(root, "QfnTests.al"), """
        codeunit 62453 "QFN Negated Tests"
        {
            Subtype = Test;

            [Test]
            procedure NegatedFlowFieldColumn_MatchesRecordCalcFields()
            var
                QfnHeader: Record "QFN Header";
                QfnLine: Record "QFN Line";
                Q: Query "QFN Header Negated";
                FromQuery: Decimal;
                FromRecord: Decimal;
            begin
                QfnHeader.Init(); QfnHeader."No." := 'N1'; QfnHeader.Insert();
                QfnLine.Init(); QfnLine."Entry No." := 1; QfnLine."Header No." := 'N1'; QfnLine.Amount := 10; QfnLine.Insert();
                QfnLine.Init(); QfnLine."Entry No." := 2; QfnLine."Header No." := 'N1'; QfnLine.Amount := 5; QfnLine.Insert();

                // The record path is the oracle: -sum(10 + 5) = -15.
                QfnHeader.CalcFields("Neg Total");
                FromRecord := QfnHeader."Neg Total";
                if FromRecord <> -15 then
                    Error('oracle broken: Record.CalcFields gave %1, expected -15', FromRecord);

                Q.SetRange(No, 'N1');
                Q.Open();
                if not Q.Read() then
                    Error('expected one row');
                FromQuery := Q.NegTotal;
                Q.Close();

                if FromQuery <> FromRecord then
                    Error('query column gave %1, record gave %2 — the sign was applied a different number of times', FromQuery, FromRecord);
            end;

            // Asserted against LITERALS, not Record.CalcFields: the record path shared the same
            // bug, so an oracle comparison passed with both sides wrong.
            [Test]
            procedure NegatedExistFlowFieldColumn_IsInverted()
            var
                QfnHeader: Record "QFN Header";
                QfnLine: Record "QFN Line";
                Q: Query "QFN Header Negated";
            begin
                QfnHeader.Init(); QfnHeader."No." := 'E1'; QfnHeader.Insert();
                QfnLine.Init(); QfnLine."Entry No." := 11; QfnLine."Header No." := 'E1'; QfnLine.Amount := 1; QfnLine.Insert();
                QfnHeader.Init(); QfnHeader."No." := 'E2'; QfnHeader.Insert();

                // E1 HAS lines: exist = true, so -exist must be FALSE.
                Q.SetRange(No, 'E1');
                Q.Open();
                if not Q.Read() then Error('expected a row for E1');
                if not Q.HasLines then
                    Error('E1 has a line, so exist() must be true');
                if Q.NoLines then
                    Error('E1 has a line, so -exist() must be FALSE — the negation was not applied to the Boolean');
                Q.Close();

                // E2 has NO lines: exist = false, so -exist must be TRUE.
                Q.SetRange(No, 'E2');
                Q.Open();
                if not Q.Read() then Error('expected a row for E2');
                if Q.HasLines then
                    Error('E2 has no lines, so exist() must be false');
                if not Q.NoLines then
                    Error('E2 has no lines, so -exist() must be TRUE — the negation was not applied to the Boolean');
                Q.Close();
            end;
        }
        """);

        return root;
    }

    [SkippableFact]
    public void NegatedFlowFieldColumn_IsNotNegatedTwice()
    {
        TestArtifacts.SkipIfMissing();

        var bundle = WriteBundle();
        var (output, _) = RunRunner(bundle);

        Assert.DoesNotContain("EMIT-EXCLUDED", output);
        Assert.DoesNotContain("COMPILE FAIL", output);
        Assert.Contains("2P/0F/0E", output);
    }
}
