using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlRunner;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// A dependency that resolves to a package NO loader tier can implement (no
/// publishedartifacts DLL, no src/*.al) is a certain failure: every call into it dies with
/// "The object with ID 0 does not have a member with that ID". DependencyResolver detects it
/// and Program.cs prints the block — once, per bundle, to stderr, at dependency-resolution
/// time.
///
/// On a real run that is ~20 seconds in, followed by minutes of emit and compile, and then a
/// summary that never mentions it again. Measured on npcore: three such blocks at ~20 s, then
/// 212 s of emit+compile, then a failure whose message was exactly the one the blocks
/// predicted — with 2,600 lines of verbose log in between. The summary is the part a scripted
/// caller (and a human scrolling to the bottom) actually reads, and it said nothing, so the
/// run reads as "my AL is broken" rather than "my package cache is not provisioned".
///
/// So the summary repeats them. These tests pin that, and pin that a healthy run's summary is
/// unchanged — a provisioning section that prints on every run would be noise nobody reads.
/// </summary>
public class ProvisionGapSummaryTests
{
    private const string Gap =
        "[dep] NaviPartner/NP Retail v9999.9999.9999.9999 resolved to a package with NO IMPLEMENTATION";

    private static BucketResult Bucket(string path, IReadOnlyList<string>? gaps = null) =>
        new(path, BucketStage.Ran,
            Array.Empty<string>(), null,
            new[] { new TestResult("Codeunit1", "T", TestOutcome.Pass, null, null, TimeSpan.Zero) },
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 1, gaps);

    private static string Summarize(params BucketResult[] buckets)
    {
        var w = new StringWriter();
        Reporter.PrintSummary(buckets, w);
        return w.ToString();
    }

    /// <summary>
    /// The block itself has to reappear, not a count: it is the thing that names the app, the
    /// winning path and the fix command, and re-reading it is what turns "my code is broken"
    /// into "run al-runner provision".
    /// </summary>
    [Fact]
    public void Summary_RepeatsTheProvisionGap_SoTheEndOfTheRunNamesIt()
    {
        var summary = Summarize(Bucket("/w/Application", new[] { Gap }));

        Assert.Contains(Gap, summary, StringComparison.Ordinal);
        Assert.Contains("Provisioning gaps", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The section header must not put a consequence on gaps that do not have it. Two different
    /// sources feed it: DependencyResolver's unservable dependencies, where every call really
    /// does die with "The object with ID 0 does not have a member with that ID", and
    /// DependencyLoader's symbol-only platform runtime apps, whose own block says "The runner
    /// will use service-tier DLL dispatch as a fallback". A header asserting the fatal outcome
    /// for both is wrong for the second — the same overstatement the --print-cache-key help text
    /// was just corrected for. Each block states its own consequence and its own fix; the header
    /// counts them and gets out of the way.
    /// </summary>
    [Fact]
    public void Summary_Header_DoesNotAssertOneConsequenceForEveryKindOfGap()
    {
        var platformAppGap =
            "[provision-gap] 'Microsoft Base Application' v28.3.52162.53416 is not available as an "
            + "R2R runtime package.\n  The runner will use service-tier DLL dispatch as a fallback.";
        var summary = Summarize(Bucket("/w/Application", new[] { platformAppGap }));

        var header = summary.Split('\n').First(l => l.Contains("Provisioning gaps", StringComparison.Ordinal));

        Assert.DoesNotContain("no loader tier can implement", header, StringComparison.Ordinal);
        Assert.DoesNotContain("object with ID 0", summary[..summary.IndexOf(platformAppGap, StringComparison.Ordinal)],
            StringComparison.Ordinal);
        // Still counts them, so the section is findable and quantified.
        Assert.Contains("Provisioning gaps: 1", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every bundle that declares the same broken dependency reports it, so a 3-bundle repo
    /// yields the same block three times. The summary states it once.
    /// </summary>
    [Fact]
    public void Summary_ReportsEachDistinctGapOnce_AcrossBundles()
    {
        var other = "[dep] Contoso/Other v1.0.0.0 resolved to a package with NO IMPLEMENTATION";
        var summary = Summarize(
            Bucket("/w/Application", new[] { Gap }),
            Bucket("/w/Test", new[] { Gap, other }));

        Assert.Equal(1, Occurrences(summary, Gap));
        Assert.Equal(1, Occurrences(summary, other));
        Assert.Contains("Provisioning gaps: 2", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// NEGATIVE — the healthy case. No gaps means the section does not exist at all: the
    /// summary a passing run prints must be byte-for-byte what it printed before, or every
    /// integration test that asserts on those markers is now asserting on noise.
    /// </summary>
    [Fact]
    public void Summary_HasNoProvisioningSection_WhenThereAreNoGaps()
    {
        foreach (var healthy in new[] { Bucket("/w/Application"), Bucket("/w/Application", Array.Empty<string>()) })
        {
            var summary = Summarize(healthy);

            Assert.DoesNotContain("Provisioning", summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[dep]", summary, StringComparison.Ordinal);
        }
    }

    private static int Occurrences(string haystack, string needle)
    {
        int n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
