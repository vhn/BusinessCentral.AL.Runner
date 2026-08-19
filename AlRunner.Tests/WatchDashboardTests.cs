using System;
using System.Collections.Generic;
using Spectre.Console.Testing;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// Pure render-model tests for the --watch live dashboard. The interactive loop
/// itself can't be unit-tested, so the view (BucketResult[] + status → renderable)
/// is factored out into <see cref="WatchDashboard"/> and exercised here against
/// Spectre.Console's <see cref="TestConsole"/>. No BC artifacts, runs fast.
/// </summary>
public class WatchDashboardTests
{
    private static string Render(IReadOnlyList<BucketResult> results, WatchStatus status,
        DateTime ts, TimeSpan dur, IReadOnlyList<string>? fullCompileNotes = null,
        IReadOnlyList<string>? rebindNotes = null)
    {
        var console = new TestConsole();
        // Wide enough that the table columns aren't truncated away in the test.
        console.Profile.Width = 120;
        console.Write(WatchDashboard.Build(
            results, "my-bundle", status, ts, dur, fullCompileNotes, rebindNotes));
        return console.Output;
    }

    private static BucketResult Bucket(params TestResult[] tests) =>
        new BucketResult("/tmp/my-bundle", BucketStage.Ran,
            Array.Empty<string>(), null, tests,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

    [Fact]
    public void Render_ShowsPassFailErrorRows_WithNamesAndCounts()
    {
        var results = new List<BucketResult>
        {
            Bucket(
                new TestResult("Codeunit1", "Pays", TestOutcome.Pass, null, null, TimeSpan.FromMilliseconds(12)),
                new TestResult("Codeunit1", "Validates", TestOutcome.Fail, "Assert.AreEqual failed: expected 1 got 9", null, TimeSpan.FromMilliseconds(34)),
                new TestResult("Codeunit2", "Posts", TestOutcome.Error, "NullReferenceException", null, TimeSpan.FromMilliseconds(56)))
        };

        var output = Render(results, WatchStatus.Idle,
            new DateTime(2026, 5, 29, 13, 5, 7, DateTimeKind.Local), TimeSpan.FromSeconds(4.2));

        // Header: bundle name + idle/watching status.
        Assert.Contains("my-bundle", output);
        Assert.Contains("watching", output);

        // Tree: a codeunit parent node, then a child node per test method.
        Assert.Contains("Codeunit1", output);
        Assert.Contains("Codeunit2", output);
        Assert.Contains("Pays", output);
        Assert.Contains("Validates", output);
        Assert.Contains("Posts", output);

        // Status labels.
        Assert.Contains("PASS", output);
        Assert.Contains("FAIL", output);
        Assert.Contains("ERROR", output);

        // Durations rendered as ms.
        Assert.Contains("12ms", output);
        Assert.Contains("34ms", output);

        // The failing test surfaces its full message under the method node.
        Assert.Contains("expected 1 got 9", output);

        // Footer counts: 1 pass / 1 fail / 1 error, 3 total.
        Assert.Contains("1P", output);
        Assert.Contains("1F", output);
        Assert.Contains("1E", output);
        // Footer hints the scroll/quit keys.
        Assert.Contains("quit", output);
    }

    [Fact]
    public void Render_UsesCodeunitDisplayName_NotTypeName()
    {
        var results = new List<BucketResult>
        {
            Bucket(
                new TestResult("Codeunit60208", "DispatchesEvent", TestOutcome.Pass, null, null,
                    TimeSpan.FromMilliseconds(8), AlCallStack: null,
                    CodeunitDisplayName: "Test Table Event Dispatch"))
        };

        var output = Render(results, WatchStatus.Idle, DateTime.Now, TimeSpan.FromSeconds(1));

        // The AL object NAME is shown as the codeunit node label …
        Assert.Contains("Test Table Event Dispatch", output);
        // … not the raw .NET type id.
        Assert.DoesNotContain("Codeunit60208", output);
        // The method still shows under it.
        Assert.Contains("DispatchesEvent", output);
    }

    [Fact]
    public void Render_FailingTest_ShowsFullStack()
    {
        const string stack =
            "\"Cust\"(CodeUnit 1).Validates line 5 - App by Pub version 1.0\n" +
            "\"Helper\"(CodeUnit 2).Check line 9 - App by Pub version 1.0";
        var results = new List<BucketResult>
        {
            Bucket(
                new TestResult("Codeunit1", "Validates", TestOutcome.Fail,
                    "NavTestFieldException: TestField failed for Amount",
                    "System.Exception full .net trace here", TimeSpan.FromMilliseconds(20),
                    AlCallStack: stack))
        };

        var output = Render(results, WatchStatus.Idle, DateTime.Now, TimeSpan.FromSeconds(1));

        // Full (untruncated) message.
        Assert.Contains("TestField failed for Amount", output);
        // Both AL call-stack frames appear (no truncation).
        Assert.Contains("Validates line 5", output);
        Assert.Contains("Check line 9", output);
    }

    [Fact]
    public void Render_FailingTest_FallsBackToFullException_WhenNoAlStack()
    {
        var results = new List<BucketResult>
        {
            Bucket(
                new TestResult("Codeunit1", "Boom", TestOutcome.Error,
                    "NullReferenceException", "NRE at SomeMethod\n  at OtherMethod",
                    TimeSpan.FromMilliseconds(3), AlCallStack: null))
        };

        var output = Render(results, WatchStatus.Idle, DateTime.Now, TimeSpan.FromSeconds(1));
        Assert.Contains("at SomeMethod", output);
        Assert.Contains("at OtherMethod", output);
    }

    [Fact]
    public void Render_RunningStatus_ShowsBusyMarker()
    {
        var results = new List<BucketResult>(); // first cold cycle: nothing yet
        var output = Render(results, WatchStatus.Running, DateTime.Now, TimeSpan.Zero);
        Assert.Contains("running", output);
        // The cold first run must not look frozen — busy state is explicit.
        Assert.DoesNotContain("watching", output);
    }

    [Fact]
    public void Render_CompileFailure_ShowsErrorRow()
    {
        var results = new List<BucketResult>
        {
            new BucketResult("/tmp/my-bundle", BucketStage.CompileFailed,
                new[] { "AL0185: 'Foo' does not contain a definition for 'Bar'" }, null,
                Array.Empty<TestResult>(),
                TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero)
        };

        var output = Render(results, WatchStatus.Idle, DateTime.Now, TimeSpan.FromSeconds(1));
        Assert.Contains("COMPILE", output);
        Assert.Contains("AL0185", output);
        // No tests ran, so a compile failure counts as one E in the footer roll-up.
        Assert.Contains("1E", output);
    }

    [Fact]
    public void Render_AllGreen_ShowsZeroFailures()
    {
        var results = new List<BucketResult>
        {
            Bucket(
                new TestResult("A", "One", TestOutcome.Pass, null, null, TimeSpan.FromMilliseconds(5)),
                new TestResult("A", "Two", TestOutcome.Pass, null, null, TimeSpan.FromMilliseconds(7)))
        };
        var output = Render(results, WatchStatus.Idle, DateTime.Now, TimeSpan.FromSeconds(2));
        Assert.Contains("2P", output);
        Assert.Contains("0F", output);
        Assert.Contains("0E", output);
    }

    /// <summary>
    /// Why a cycle rebuilt whole modules has to be ON this screen. The bundle loop redirects
    /// both console streams to <c>TextWriter.Null</c> while it runs, so the `[watch]` lines
    /// carrying the reason are discarded in exactly the mode a developer watches — a cycle that
    /// cost four minutes looked identical to one that cost a second, with nothing to attribute
    /// it to. The reason is the whole payload, so it is asserted verbatim rather than by the
    /// panel's presence.
    /// </summary>
    [Fact]
    public void Render_FullCompileNotes_ShowTheReasonAndTheApp()
    {
        var results = new List<BucketResult>
        {
            Bucket(new TestResult("A", "One", TestOutcome.Pass, null, null, TimeSpan.FromMilliseconds(5)))
        };

        var output = Render(results, WatchStatus.Idle, DateTime.Now, TimeSpan.FromSeconds(2),
            ["NP Retail: app.json changed the app version: 1.0.0.0 → 1.0.1.0"]);

        Assert.Contains("full recompile", output);
        Assert.Contains("NP Retail", output);
        Assert.Contains("app.json changed the app version", output);
        // The results are still trustworthy — a full compile is slow, not wrong.
        Assert.Contains("1P", output);
    }

    /// <summary>
    /// And it is absent on an ordinary delta cycle. A panel that is always there stops carrying
    /// information: the developer has to be able to read "no full recompile happened" off the
    /// screen without parsing anything.
    /// </summary>
    [Fact]
    public void Render_NoFullCompileNotes_OmitsThePanelEntirely()
    {
        var results = new List<BucketResult>
        {
            Bucket(new TestResult("A", "One", TestOutcome.Pass, null, null, TimeSpan.FromMilliseconds(5)))
        };

        Assert.DoesNotContain("full recompile",
            Render(results, WatchStatus.Idle, DateTime.Now, TimeSpan.FromSeconds(2)));
        Assert.DoesNotContain("full recompile",
            Render(results, WatchStatus.Idle, DateTime.Now, TimeSpan.FromSeconds(2), []));
    }

    /// <summary>
    /// The same argument for extra binding work that a delta's changed-file count does not expose:
    /// either an app re-emits callers of a sibling app's moved surface, or a namespace-free file
    /// repeats its bind against a repaired packaged surface. Those decisions are made deep in the
    /// compile path and announced on stderr — which the bundle loop redirects to
    /// <c>TextWriter.Null</c> while it runs. Without this panel the work has no visible cause.
    ///
    /// <para>A SEPARATE panel, not an extra line in the full-recompile one, because the two say
    /// opposite things about the cycle. A full compile is the slow path and the note explains a
    /// cost; a delta rebind is the narrow path working correctly, and mislabelling it "full
    /// recompile" would claim a cascade that did not happen.</para>
    /// </summary>
    [Fact]
    public void Render_RebindNotes_ShowTheProducerAndTheCount_InTheirOwnPanel()
    {
        var results = new List<BucketResult>
        {
            Bucket(new TestResult("A", "One", TestOutcome.Pass, null, null, TimeSpan.FromMilliseconds(5)))
        };

        var output = Render(results, WatchStatus.Idle, DateTime.Now, TimeSpan.FromSeconds(2),
            fullCompileNotes: null,
            rebindNotes: ["NP Retail Test: 3 that call NP Retail"]);

        Assert.Contains("delta rebind", output);
        Assert.Contains("NP Retail Test", output);
        Assert.Contains("3 that call NP Retail", output);
        // Not the slow path, and must not be reported as it.
        Assert.DoesNotContain("full recompile", output);
    }

    /// <summary>
    /// And absent on a cycle that needed neither caller widening nor a repaired second bind. A
    /// panel that is always present stops carrying information.
    /// </summary>
    [Fact]
    public void Render_NoRebindNotes_OmitsThePanelEntirely()
    {
        var results = new List<BucketResult>
        {
            Bucket(new TestResult("A", "One", TestOutcome.Pass, null, null, TimeSpan.FromMilliseconds(5)))
        };

        Assert.DoesNotContain("delta rebind",
            Render(results, WatchStatus.Idle, DateTime.Now, TimeSpan.FromSeconds(2)));
        Assert.DoesNotContain("delta rebind",
            Render(results, WatchStatus.Idle, DateTime.Now, TimeSpan.FromSeconds(2), [], []));
    }
}
