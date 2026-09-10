using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

/// BC stamps the synthesized FlowField sub-query column with the QUERY AUTHOR's `Method` and
/// `ReverseSign` — not the CalcFormula's own method — XOR-ing in `NegateResult` and swapping
/// Min/Max when the formula is negated
/// (<c>NCLMetaQuery.CreateSubQueryForFlowFieldCalculation</c>). Discarding that metadata drops
/// an aggregation the author asked for, silently: a `Method = Sum` over several owners returns
/// one owner's value instead of the total, and a `ReverseSign` column comes back with the wrong
/// sign. npcore queries 6014429 and ItemSalesPostings declare exactly this shape.
///
/// Spawns the real runner; needs the BC artifact cache. Skips (no-op) when absent.
/// </summary>
public class QueryFlowFieldAuthorMethodTests
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
        var root = Path.Combine(Path.GetTempPath(), "al-runner-query-flowfield-authormethod", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "app.json"), """
        {
          "id": "c7d1e4f2-2302-4a1b-9c3d-000000002302",
          "name": "QFA Author Method",
          "publisher": "Repro2302",
          "version": "1.0.0.0",
          "dependencies": [],
          "idRanges": [ { "from": 62440, "to": 62449 } ],
          "runtime": "17.0",
          "platform": "28.0.0.0",
          "application": "28.0.0.0"
        }
        """);

        File.WriteAllText(Path.Combine(root, "QfaLine.al"), """
        table 62440 "QFA Line"
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

        File.WriteAllText(Path.Combine(root, "QfaHeader.al"), """
        table 62441 "QFA Header"
        {
            DataClassification = SystemMetadata;
            fields
            {
                field(1; "No."; Code[20]) { }
                field(2; Grp; Code[10]) { }
                field(3; Total; Decimal)
                {
                    FieldClass = FlowField;
                    CalcFormula = sum("QFA Line".Amount where("Header No." = field("No.")));
                }
                field(4; NegTotal; Decimal)
                {
                    FieldClass = FlowField;
                    CalcFormula = -sum("QFA Line".Amount where("Header No." = field("No.")));
                }
            }
            keys { key(PK; "No.") { Clustered = true; } }
        }
        """);

        File.WriteAllText(Path.Combine(root, "QfaQueries.al"), """
        query 62442 "QFA Sum Of FlowField"
        {
            QueryType = Normal;
            elements
            {
                dataitem(H; "QFA Header")
                {
                    column(Grp; Grp) { }
                    column(TotalSum; Total) { Method = Sum; }
                }
            }
        }

        query 62443 "QFA Reversed FlowField"
        {
            QueryType = Normal;
            elements
            {
                dataitem(H; "QFA Header")
                {
                    column(No; "No.") { }
                    column(Rev; Total) { ReverseSign = true; }
                }
            }
        }

        query 62446 "QFA Min Of Negated FlowField"
        {
            QueryType = Normal;
            elements
            {
                dataitem(H; "QFA Header")
                {
                    column(Grp; Grp) { }
                    column(NegMin; NegTotal) { Method = Min; }
                }
            }
        }

        query 62447 "QFA Max Of Negated FlowField"
        {
            QueryType = Normal;
            elements
            {
                dataitem(H; "QFA Header")
                {
                    column(Grp; Grp) { }
                    column(NegMax; NegTotal) { Method = Max; }
                }
            }
        }

        query 62444 "QFA Reversed Negated FlowField"
        {
            QueryType = Normal;
            elements
            {
                dataitem(H; "QFA Header")
                {
                    column(No; "No.") { }
                    column(RevNeg; NegTotal) { ReverseSign = true; }
                }
            }
        }
        """);

        File.WriteAllText(Path.Combine(root, "QfaTests.al"), """
        codeunit 62445 "QFA Author Method Tests"
        {
            Subtype = Test;

            local procedure Seed()
            var
                H: Record "QFA Header";
                L: Record "QFA Line";
            begin
                if not H.IsEmpty() then exit;
                H.Init(); H."No." := 'A1'; H.Grp := 'G'; H.Insert();
                H.Init(); H."No." := 'A2'; H.Grp := 'G'; H.Insert();
                L.Init(); L."Entry No." := 1; L."Header No." := 'A1'; L.Amount := 15; L.Insert();
                L.Init(); L."Entry No." := 2; L."Header No." := 'A2'; L.Amount := 20; L.Insert();
            end;

            // The author asked the QUERY to Sum the FlowField across both owners.
            [Test]
            procedure AuthorMethodSum_OnFlowFieldColumn_SumsAcrossOwners()
            var
                Q: Query "QFA Sum Of FlowField";
                Rows: Integer;
                Got: Decimal;
            begin
                Seed();
                Q.SetRange(Grp, 'G');
                Q.Open();
                while Q.Read() do begin
                    Rows += 1;
                    Got := Q.TotalSum;
                end;
                Q.Close();
                if Rows <> 1 then
                    Error('Method = Sum should group both owners into one row, got %1 rows', Rows);
                if Got <> 35 then
                    Error('Expected 15 + 20 = 35, got %1 — the author''s Method was dropped', Got);
            end;

            // ReverseSign declared by the author on a (non-negated) FlowField column.
            [Test]
            procedure AuthorReverseSign_OnFlowFieldColumn_IsHonoured()
            var
                Q: Query "QFA Reversed FlowField";
                Got: Decimal;
            begin
                Seed();
                Q.SetRange(No, 'A1');
                Q.Open();
                if not Q.Read() then Error('expected a row');
                Got := Q.Rev;
                Q.Close();
                if Got <> -15 then
                    Error('Expected -15 (ReverseSign on a +15 FlowField), got %1', Got);
            end;

            // BC swaps Min<->Max on the synthesized column when the formula is negated, so the
            // runner must swap them BACK: the calc core already returned the negated values
            // (-15 and -20), and Min of those is -20. Reading the stamped "Max" literally would
            // return -15 — right shape, wrong row, and no crash to notice it by.
            [Test]
            procedure AuthorMethodMin_OnNegatedFlowField_IsTheTrueMinimum()
            var
                Q: Query "QFA Min Of Negated FlowField";
                Got: Decimal;
            begin
                Seed();
                Q.SetRange(Grp, 'G');
                Q.Open();
                if not Q.Read() then Error('expected a row');
                Got := Q.NegMin;
                Q.Close();
                if Got <> -20 then
                    Error('Expected -20 (min of -15 and -20), got %1 — the Min/Max swap was not undone', Got);
            end;

            [Test]
            procedure AuthorMethodMax_OnNegatedFlowField_IsTheTrueMaximum()
            var
                Q: Query "QFA Max Of Negated FlowField";
                Got: Decimal;
            begin
                Seed();
                Q.SetRange(Grp, 'G');
                Q.Open();
                if not Q.Read() then Error('expected a row');
                Got := Q.NegMax;
                Q.Close();
                if Got <> -15 then
                    Error('Expected -15 (max of -15 and -20), got %1 — the Min/Max swap was not undone', Got);
            end;

            // Author ReverseSign XOR CalcFormula NegateResult: both set cancel out.
            [Test]
            procedure AuthorReverseSign_OnNegatedFlowField_CancelsOut()
            var
                Q: Query "QFA Reversed Negated FlowField";
                Got: Decimal;
            begin
                Seed();
                Q.SetRange(No, 'A1');
                Q.Open();
                if not Q.Read() then Error('expected a row');
                Got := Q.RevNeg;
                Q.Close();
                if Got <> 15 then
                    Error('Expected +15 (ReverseSign XOR NegateResult cancel), got %1', Got);
            end;
        }
        """);

        return root;
    }

    [SkippableFact]
    public void AuthorDeclaredMethodAndReverseSign_OnAFlowFieldColumn_AreHonoured()
    {
        TestArtifacts.SkipIfMissing();

        var bundle = WriteBundle();
        var (output, _) = RunRunner(bundle);

        Assert.DoesNotContain("EMIT-EXCLUDED", output);
        Assert.DoesNotContain("COMPILE FAIL", output);
        Assert.Contains("5P/0F/0E", output);
    }
}
