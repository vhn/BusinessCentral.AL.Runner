using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public sealed class DateTimeVirtualTableTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "al-runner-date-time-zone-virtual-tables", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private void WriteBundle()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.json"), """
        {
          "id": "9905ece1-5e8d-463c-8e1f-675c2cbef26e",
          "name": "Runner Mechanism - Date and Time Zone Virtual Tables",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 62460, "to": 62469 } ],
          "runtime": "14.0"
        }
        """);

        File.WriteAllText(Path.Combine(_root, "DtvTests.Codeunit.al"), """
        codeunit 62460 "Dtv Tests"
        {
            Subtype = Test;
            TestPermissions = Disabled;

            [Test]
            procedure DateVirtualTableProvidesBoundedPeriodRows()
            var
                Calendar: Record Date;
            begin
                Calendar.SetRange("Period Type", Calendar."Period Type"::Date);
                Calendar.SetRange("Period Start", 20240229D);
                if not Calendar.FindFirst() then
                    Error('Expected a Date row for 2024-02-29');
                if Calendar."Period End" <> ClosingDate(20240229D) then
                    Error('Expected a closing date for the end of the day, got %1', Calendar."Period End");
                if Calendar."Period No." <> 4 then
                    Error('Expected Thursday to be weekday 4, got %1', Calendar."Period No.");

                Calendar.Reset();
                Calendar.SetRange("Period Type", Calendar."Period Type"::Month);
                Calendar.SetRange("Period Start", 20240101D, 20240301D);
                if Calendar.Count() <> 3 then
                    Error('Expected three month rows, got %1', Calendar.Count());
            end;

            [Test]
            procedure TimeZoneVirtualTableProvidesBusinessCentralIds()
            var
                TimeZone: Record "Time Zone";
            begin
                TimeZone.SetRange(ID, 'UTC+12');
                if not TimeZone.FindFirst() then
                    Error('Expected UTC+12 in the Time Zone virtual table');
                if TimeZone."Display Name" = '' then
                    Error('Expected UTC+12 to have a display name');
                if TimeZone."No." <> 134 then
                    Error('Expected UTC+12 to have Business Central sequence number 134, got %1', TimeZone."No.");

                TimeZone.SetRange(ID, 'W. Europe Standard Time');
                if not TimeZone.FindFirst() then
                    Error('Expected W. Europe Standard Time in the Time Zone virtual table');
                if TimeZone."No." <> 52 then
                    Error('Expected W. Europe Standard Time to have Business Central sequence number 52, got %1', TimeZone."No.");
            end;

            [Test]
            procedure WindowsLanguageVirtualTableProvidesEnglishUnitedStates()
            var
                WindowsLanguage: Record "Windows Language";
            begin
                if not WindowsLanguage.Get(1033) then
                    Error('Expected language ID 1033 in the Windows Language virtual table');
                if WindowsLanguage."Abbreviated Name" <> 'ENU' then
                    Error(
                        'Expected language ID 1033 to use abbreviated name ENU, got %1',
                        WindowsLanguage."Abbreviated Name");
                if WindowsLanguage."Language Tag" <> 'en-US' then
                    Error(
                        'Expected language ID 1033 to use language tag en-US, got %1',
                        WindowsLanguage."Language Tag");
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
    public void DateAndTimeZoneVirtualTables_ArePopulated()
    {
        Skip.If(OperatingSystem.IsWindows(), "The managed Time Zone compatibility provider is Unix-specific.");
        TestArtifacts.SkipIfMissing();
        WriteBundle();

        var (output, exitCode) = RunBundle();

        Assert.True(exitCode == 0, $"Expected the bundle to pass; exit={exitCode}\n{output}");
        Assert.Contains("PASS  Codeunit62460.DateVirtualTableProvidesBoundedPeriodRows", output);
        Assert.Contains("PASS  Codeunit62460.TimeZoneVirtualTableProvidesBusinessCentralIds", output);
        Assert.Contains("PASS  Codeunit62460.WindowsLanguageVirtualTableProvidesEnglishUnitedStates", output);
        Assert.DoesNotContain("FAIL", output);
    }
}
