using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// A manually-bound subscriber stored in an AL method-local variable is bound only for
/// that variable's lifetime. Under codeunit isolation the test-codeunit instance survives
/// between test methods, but a local subscriber from the first method must not.
/// </summary>
public sealed class ManualBindingScopeLifetimeTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private readonly string _root;

    public ManualBindingScopeLifetimeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "al-runner-manual-binding-scope", Guid.NewGuid().ToString("N"));
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
          "id": "982b5be7-c4b1-40ce-95ee-0f8378811348",
          "name": "Manual Binding Scope Lifetime Fixture",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 62230, "to": 62239 } ],
          "runtime": "14.0"
        }
        """);

        File.WriteAllText(Path.Combine(dir, "Publisher.Codeunit.al"), """
        codeunit 62230 "Binding Scope Publisher"
        {
            [IntegrationEvent(false, false)]
            procedure Raise(var InvocationCount: Integer)
            begin
            end;

            [IntegrationEvent(false, false)]
            procedure Raise_Nested(var InvocationCount: Integer)
            begin
            end;
        }
        """);

        File.WriteAllText(Path.Combine(dir, "Subscriber.Codeunit.al"), """
        codeunit 62231 "Binding Scope Subscriber"
        {
            EventSubscriberInstance = Manual;

            [EventSubscriber(ObjectType::Codeunit, Codeunit::"Binding Scope Publisher", 'Raise', '', false, false)]
            local procedure OnRaise(var InvocationCount: Integer)
            var
                Publisher: Codeunit "Binding Scope Publisher";
            begin
                InvocationCount += 1;
                Publisher.Raise_Nested(InvocationCount);
            end;

            [EventSubscriber(ObjectType::Codeunit, Codeunit::"Binding Scope Publisher", 'Raise_Nested', '', false, false)]
            local procedure OnRaiseNested(var InvocationCount: Integer)
            begin
                InvocationCount += 10;
            end;
        }
        """);

        File.WriteAllText(Path.Combine(dir, "Tests.Codeunit.al"), """
        codeunit 62232 "Manual Binding Scope Tests"
        {
            Subtype = Test;

            [Test]
            procedure Step1_LocalBindingIsLiveInsideDeclaringMethod()
            var
                Publisher: Codeunit "Binding Scope Publisher";
                Subscriber: Codeunit "Binding Scope Subscriber";
                InvocationCount: Integer;
            begin
                BindSubscription(Subscriber);
                Publisher.Raise(InvocationCount);
                if InvocationCount <> 11 then
                    Error('Expected the bound subscriber to survive nested event dispatch and produce 11, but got %1.', InvocationCount);
            end;

            [Test]
            procedure Step2_LocalBindingFromEarlierMethodHasExpired()
            var
                Publisher: Codeunit "Binding Scope Publisher";
                InvocationCount: Integer;
            begin
                Publisher.Raise(InvocationCount);
                if InvocationCount <> 0 then
                    Error('LOCAL-BINDING-LEAK: a subscriber local to the previous test method fired %1 times.', InvocationCount);
            end;
        }
        """);
    }

    private (string Output, int ExitCode) RunRunner()
    {
        var args = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
        args.Append(TestBuildConfig.BcVersionArg);
        args.Append(" --strict --no-cache");
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
            throw new TimeoutException("runner hung while checking manual binding scope lifetime");
        }
        process.WaitForExit();
        lock (output) return (output.ToString(), process.ExitCode);
    }

    [SkippableFact]
    public void CodeunitIsolation_ExpiresManualBindingsOwnedByMethodLocals()
    {
        TestArtifacts.SkipIfMissing();

        var (output, exitCode) = RunRunner();

        Assert.True(exitCode == 0, output);
        Assert.Contains("PASS  Codeunit62232.Step1_LocalBindingIsLiveInsideDeclaringMethod", output);
        Assert.Contains("PASS  Codeunit62232.Step2_LocalBindingFromEarlierMethodHasExpired", output);
        Assert.DoesNotContain("LOCAL-BINDING-LEAK", output);
    }
}
