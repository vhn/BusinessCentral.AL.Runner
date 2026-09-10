using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

/// A FlowField whose CalcFormula is NEGATED (`-sum(...)`) must read the SAME value through a
/// query column that Record.CalcFields gives.
///
/// BC carries the negation twice, and a runner that honours both applies it twice. BC's
/// CreateSubqueryForFlowField passes `CalculationFormula.NegateResult` as the synthesized
/// outer column's REVERSE-SIGN constructor argument, while the calculation core itself already
/// negates the computed value from the same `NegateResult` flag. On the real service tier the
/// sub-query returns the UN-negated sum and the outer ReverseSign supplies the only negation;
/// in this runner the value arrives already negated from CalcOneFlowFieldForQueryRow, so
/// re-applying ReverseSign in ProjectFinalRows/AggregateColumn flips it back.
///
/// The failure is silent: a wrong SIGN, not a crash, on a query that opens and returns the
/// expected number of rows. The parent commit throws the #2300 metadata NRE here instead, so
/// this is the shape where a fix can turn a loud failure into a wrong answer.
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
        Assert.Contains("1P/0F/0E", output);
    }
}
