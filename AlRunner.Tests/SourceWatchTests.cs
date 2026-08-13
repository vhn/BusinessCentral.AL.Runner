// SourceWatchTests — the two decisions `--watch` makes before it compiles anything.
//
// RadBulkSwitchWatchTests proves the end-to-end behaviour by driving the real runner, which
// costs a BC engine boot and a compile. These are the same two claims stated directly
// against the state machine, so a regression names itself instead of surfacing as a slow
// watch test failing on an assertion about log output.

using System.Diagnostics;
using AlRunner;
using Xunit;

namespace AlRunner.Tests;

public sealed class SourceWatchTests
{
    /// <summary>
    /// The debounce has to be quiescence, not a fixed interval. A writer that keeps going —
    /// git checking out a branch, a formatter walking the tree — must not have a compile
    /// started underneath it, because the tree is a mixture of two versions until it stops.
    ///
    /// The assertion is a LOWER bound on purpose: a slow machine stretches the writer and
    /// the wait together, so the test cannot flake in the "took longer than expected"
    /// direction. It fails only against an implementation that returns while events are
    /// still arriving — which the fixed 250 ms sleep this replaced did every time.
    /// </summary>
    [Fact]
    public async Task AwaitQuiet_BlocksWhileEventsAreStillArriving()
    {
        var watch = new SourceWatch();
        const int events = 8, gapMs = 60;

        watch.Record("/tmp/first.al");
        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < events; i++)
            {
                await Task.Delay(gapMs);
                watch.Record($"/tmp/burst{i}.al");
            }
        });

        var elapsed = Stopwatch.StartNew();
        watch.AwaitQuiet();
        elapsed.Stop();
        await writer;

        Assert.True(elapsed.ElapsedMilliseconds >= events * gapMs,
            $"AwaitQuiet returned after {elapsed.ElapsedMilliseconds}ms, while the writer was " +
            $"still emitting events for {events * gapMs}ms — a cycle starting there compiles a " +
            "half-written tree.");
    }

    /// <summary>
    /// A single event must not pay the burst treatment beyond the quiet window itself: the
    /// inner dev loop is one save, and quiescence is reached as soon as nothing follows it.
    /// Paired with the test above, this is what pins the wait to "until it stops" rather
    /// than either "immediately" or "always the maximum".
    /// </summary>
    [Fact]
    public void AwaitQuiet_ReturnsPromptlyAfterASingleSave()
    {
        var watch = new SourceWatch();
        watch.Record("/tmp/one.al");

        var elapsed = Stopwatch.StartNew();
        watch.AwaitQuiet();
        elapsed.Stop();

        Assert.True(elapsed.ElapsedMilliseconds < 3_000,
            $"a single save waited {elapsed.ElapsedMilliseconds}ms before its cycle could start.");
    }

    /// <summary>
    /// A notification overflow means the runner knows something changed but not what. That
    /// has to be reported as "unknown", never as "nothing" — the consumer
    /// (<c>RadWorkspaceStore.PrepareBundleReload</c>) decides whether warm compiler metadata
    /// may survive the reload by checking that EVERY changed path is a <c>.al</c> file under
    /// a known app, and every path of an empty list satisfies that vacuously. Handing it an
    /// empty list after an overflow preserves metadata across a change that may well have
    /// been an <c>app.json</c> edit.
    /// </summary>
    [Fact]
    public void DrainChangedPaths_ReportsAnOverflowAsUnknown_NotAsNoChange()
    {
        var watch = new SourceWatch();

        // Positive: an ordinary burst drains as the exact set of paths seen.
        watch.Record("/tmp/a.al");
        watch.Record("/tmp/b.al");
        Assert.Equal(["/tmp/a.al", "/tmp/b.al"], watch.DrainChangedPaths());

        // Negative: once events have been dropped, the list is null rather than short.
        watch.Record("/tmp/c.al");
        watch.Record(null);
        Assert.Null(watch.DrainChangedPaths());

        // And the overflow does not stick — the next cycle reports normally again.
        watch.Record("/tmp/d.al");
        Assert.Equal(["/tmp/d.al"], watch.DrainChangedPaths());
    }

    /// <summary>
    /// Draining twice with nothing in between is an empty list, not null: "nothing changed"
    /// and "I do not know what changed" are different answers and only the second one may
    /// force a clean metadata refresh.
    /// </summary>
    [Fact]
    public void DrainChangedPaths_WithNothingRecorded_IsEmptyRatherThanUnknown()
    {
        var watch = new SourceWatch();
        Assert.Equal([], watch.DrainChangedPaths());
    }
}
