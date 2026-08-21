using System;
using System.IO;
using AlRunner.Infrastructure;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The provisioning gap that actually bites is the one DependencyLoader reports: a known
/// Microsoft platform runtime app (System Application, Base Application, Business Foundation)
/// resolved to a symbol-only package, whose procedure bodies are external/native and so cannot
/// execute. Measured on npcore: four `[provision-gap]` blocks at ~20 s, then 212 s of
/// emit+compile, then `The object with ID 0 does not have a member with that ID` — exactly what
/// those blocks predicted, and never mentioned again in the summary the caller reads.
///
/// DependencyLoader writes that message from deep inside a dependency load, several layers below
/// the bundle loop that builds the run's BucketResults, so it needs somewhere to put it. This is
/// that place: the message stays LOUD on stderr where it always was (per
/// .claude/rules/loud-failures.md, this must not become quieter), and is additionally recorded so
/// Reporter.PrintSummary can name it at the end.
///
/// The reset is the interesting half. A run processes bundles in sequence and a watch session
/// re-runs them forever, so a collector that never forgets would attribute the first bundle's
/// gaps to every later bundle and every later cycle.
/// </summary>
public class ProvisionGapLogTests : IDisposable
{
    private readonly TextWriter _originalError = Console.Error;

    public ProvisionGapLogTests() => ProvisionGapLog.Reset();

    public void Dispose()
    {
        Console.SetError(_originalError);
        ProvisionGapLog.Reset();
    }

    [Fact]
    public void Collected_IsEmpty_BeforeAnythingIsReported()
    {
        Assert.Empty(ProvisionGapLog.Collected);
    }

    /// <summary>
    /// Both halves in one assertion pair: still printed (loud), and now also recorded. Dropping
    /// either would defeat the point — a recorded-but-unprinted gap is quieter than before, and a
    /// printed-but-unrecorded one is the defect being fixed.
    /// </summary>
    [Fact]
    public void Report_WritesToStderr_AndRecordsTheMessage()
    {
        var captured = new StringWriter();
        Console.SetError(captured);

        ProvisionGapLog.Report("[provision-gap] 'Microsoft Base Application' v28.3.52162.53416 is not available as an R2R runtime package.");

        Assert.Contains("[provision-gap] 'Microsoft Base Application'", captured.ToString(), StringComparison.Ordinal);
        var recorded = Assert.Single(ProvisionGapLog.Collected);
        Assert.Contains("Microsoft Base Application", recorded, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_KeepsEveryDistinctGap_InOrder()
    {
        Console.SetError(TextWriter.Null);

        ProvisionGapLog.Report("gap-system");
        ProvisionGapLog.Report("gap-base");

        Assert.Equal(new[] { "gap-system", "gap-base" }, ProvisionGapLog.Collected);
    }

    /// <summary>
    /// NEGATIVE — the cross-bundle and cross-watch-cycle leak. Without this, bundle 2's summary
    /// blames it for bundle 1's missing package, and a watch session accumulates gaps forever.
    /// </summary>
    [Fact]
    public void Reset_DropsThePreviousBundlesGaps()
    {
        Console.SetError(TextWriter.Null);
        ProvisionGapLog.Report("gap-from-the-previous-bundle");
        Assert.NotEmpty(ProvisionGapLog.Collected);

        ProvisionGapLog.Reset();

        Assert.Empty(ProvisionGapLog.Collected);
    }

    /// <summary>
    /// The returned list must be a snapshot, not a live view: Program.cs copies it into the
    /// bundle's own list and the next Reset must not empty what was already copied out.
    /// </summary>
    [Fact]
    public void Collected_IsASnapshot_NotInvalidatedByALaterReset()
    {
        Console.SetError(TextWriter.Null);
        ProvisionGapLog.Report("gap-a");

        var snapshot = ProvisionGapLog.Collected;
        ProvisionGapLog.Reset();

        Assert.Equal(new[] { "gap-a" }, snapshot);
    }
}
