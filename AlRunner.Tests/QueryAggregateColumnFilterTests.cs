using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public sealed class QueryAggregateColumnFilterTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "al-runner-query-aggregate-column-filter", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private void WriteBundle()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.json"), """
        {
          "id": "eb3c4a76-f76d-4bdc-b199-1144a89b7985",
          "name": "Runner Mechanism - Query Aggregate ColumnFilter",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 62450, "to": 62459 } ],
          "runtime": "14.0"
        }
        """);

        File.WriteAllText(Path.Combine(_root, "QacGroup.Table.al"), """
        table 62450 "Qac Group"
        {
            DataClassification = CustomerContent;

            fields
            {
                field(1; Code; Code[10]) { }
            }

            keys
            {
                key(PK; Code) { Clustered = true; }
            }
        }
        """);

        File.WriteAllText(Path.Combine(_root, "QacEntry.Table.al"), """
        table 62451 "Qac Entry"
        {
            DataClassification = CustomerContent;

            fields
            {
                field(1; "Entry No."; Integer) { }
                field(2; "Group Code"; Code[10]) { }
                field(3; Amount; Decimal) { }
                field(4; Included; Boolean) { }
            }

            keys
            {
                key(PK; "Entry No.") { Clustered = true; }
            }
        }
        """);

        File.WriteAllText(Path.Combine(_root, "QacTotals.Query.al"), """
        query 62452 "Qac Totals"
        {
            QueryType = Normal;

            elements
            {
                dataitem(Group_Row; "Qac Group")
                {
                    column(GroupCode; Code) { }

                    dataitem(Entry_Row; "Qac Entry")
                    {
                        DataItemLink = "Group Code" = Group_Row.Code;
                        DataItemTableFilter = Included = const(true);
                        SqlJoinType = InnerJoin;

                        column(TotalAmount; Amount)
                        {
                            ColumnFilter = TotalAmount = filter(> 0);
                            Method = Sum;
                        }
                    }
                }
            }
        }
        """);

        File.WriteAllText(Path.Combine(_root, "QacSingleTotals.Query.al"), """
        query 62454 "Qac Single Totals"
        {
            QueryType = Normal;

            elements
            {
                dataitem(Entry_Row; "Qac Entry")
                {
                    DataItemTableFilter = Included = const(true);

                    column(GroupCode; "Group Code") { }
                    column(TotalAmount; Amount)
                    {
                        ColumnFilter = TotalAmount = filter(> 0);
                        Method = Sum;
                    }
                }
            }
        }
        """);

        File.WriteAllText(Path.Combine(_root, "QacAggregateMethods.Query.al"), """
        query 62455 "Qac Aggregate Methods"
        {
            QueryType = Normal;

            elements
            {
                dataitem(Entry_Row; "Qac Entry")
                {
                    DataItemTableFilter = Included = const(true);

                    column(RowCount)
                    {
                        Method = Count;
                    }
                    column(TotalAmount; Amount)
                    {
                        Method = Sum;
                    }
                    column(ReversedTotal; Amount)
                    {
                        Method = Sum;
                        ReverseSign = true;
                    }
                    column(AverageAmount; Amount)
                    {
                        Method = Average;
                    }
                    column(MinimumAmount; Amount)
                    {
                        Method = Min;
                    }
                    column(MaximumAmount; Amount)
                    {
                        Method = Max;
                    }
                }
            }
        }
        """);

        File.WriteAllText(Path.Combine(_root, "QacTests.Codeunit.al"), """
        codeunit 62453 "Qac Tests"
        {
            Subtype = Test;
            TestPermissions = Disabled;

            [Test]
            procedure AggregateColumnFilterRunsAfterGrouping()
            var
                Totals: Query "Qac Totals";
                RowCount: Integer;
            begin
                Seed();

                Totals.Open();
                while Totals.Read() do begin
                    RowCount += 1;
                    if Totals.GroupCode <> 'A' then
                        Error('Expected only group A, got %1 with total %2', Totals.GroupCode, Totals.TotalAmount);
                    if Totals.TotalAmount <> 15 then
                        Error('Expected grouped total 15, got %1', Totals.TotalAmount);
                end;
                Totals.Close();

                if RowCount <> 1 then
                    Error('Expected one positive aggregate group, got %1', RowCount);
            end;

            [Test]
            procedure SingleDataItemAggregateColumnFilterRunsAfterGrouping()
            var
                Totals: Query "Qac Single Totals";
                RowCount: Integer;
            begin
                Seed();

                Totals.Open();
                while Totals.Read() do begin
                    RowCount += 1;
                    if Totals.GroupCode <> 'A' then
                        Error('Expected only group A, got %1 with total %2', Totals.GroupCode, Totals.TotalAmount);
                    if Totals.TotalAmount <> 15 then
                        Error('Expected grouped total 15, got %1', Totals.TotalAmount);
                end;
                Totals.Close();

                if RowCount <> 1 then
                    Error('Expected one positive aggregate group, got %1', RowCount);
            end;

            [Test]
            procedure AggregateMethodsAndReverseSignReturnTypedResults()
            var
                Aggregates: Query "Qac Aggregate Methods";
            begin
                Seed();

                Aggregates.Open();
                if not Aggregates.Read() then
                    Error('Expected one global aggregate row');
                if Aggregates.RowCount <> 5 then
                    Error('Expected count 5, got %1', Aggregates.RowCount);
                if Aggregates.TotalAmount <> 12 then
                    Error('Expected sum 12, got %1', Aggregates.TotalAmount);
                if Aggregates.ReversedTotal <> -12 then
                    Error('Expected reversed sum -12, got %1', Aggregates.ReversedTotal);
                if Aggregates.AverageAmount <> 2.4 then
                    Error('Expected average 2.4, got %1', Aggregates.AverageAmount);
                if Aggregates.MinimumAmount <> -7 then
                    Error('Expected minimum -7, got %1', Aggregates.MinimumAmount);
                if Aggregates.MaximumAmount <> 10 then
                    Error('Expected maximum 10, got %1', Aggregates.MaximumAmount);
                if Aggregates.Read() then
                    Error('Expected only one global aggregate row');
                Aggregates.Close();
            end;

            local procedure Seed()
            var
                GroupRow: Record "Qac Group";
                EntryRow: Record "Qac Entry";
            begin
                GroupRow.DeleteAll();
                EntryRow.DeleteAll();

                InsertGroup(GroupRow, 'A');
                InsertGroup(GroupRow, 'B');
                InsertGroup(GroupRow, 'C');
                InsertEntry(EntryRow, 1, 'A', 10, true);
                InsertEntry(EntryRow, 2, 'A', 5, true);
                InsertEntry(EntryRow, 3, 'A', 1000, false);
                InsertEntry(EntryRow, 4, 'B', 7, true);
                InsertEntry(EntryRow, 5, 'B', -7, true);
                InsertEntry(EntryRow, 6, 'C', -3, true);
            end;

            local procedure InsertGroup(var GroupRow: Record "Qac Group"; Code: Code[10])
            begin
                GroupRow.Init();
                GroupRow.Code := Code;
                GroupRow.Insert();
            end;

            local procedure InsertEntry(var EntryRow: Record "Qac Entry"; EntryNo: Integer; GroupCode: Code[10]; Amount: Decimal; Included: Boolean)
            begin
                EntryRow.Init();
                EntryRow."Entry No." := EntryNo;
                EntryRow."Group Code" := GroupCode;
                EntryRow.Amount := Amount;
                EntryRow.Included := Included;
                EntryRow.Insert();
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
    public void AggregateColumnFilter_IsAppliedAsHavingAfterGrouping()
    {
        TestArtifacts.SkipIfMissing();
        WriteBundle();

        var (output, exitCode) = RunBundle();

        Assert.True(exitCode == 0, $"Expected the bundle to pass; exit={exitCode}\n{output}");
        Assert.Contains("PASS  Codeunit62453.AggregateColumnFilterRunsAfterGrouping", output);
        Assert.DoesNotContain("FAIL", output);
    }
}
