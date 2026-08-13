using System.Collections.Concurrent;

namespace AlRunner;

/// <summary>
/// The armed file-watching state of one <c>--watch</c> session: the wake-up signal, the
/// paths that woke it, and the watchers themselves.
///
/// <para><b>Why this is not just a signal.</b> The interesting file change is often not a
/// single save — it is a BURST. A branch switch, a rebase, a bulk rename or a formatter run
/// rewrites dozens to thousands of <c>.al</c> files, and from a FileSystemWatcher's point of
/// view that is a stream of events spread over seconds with the tree in a mixed state
/// throughout. Waking on the first event and sleeping a fixed 250 ms — which is what this
/// used to do — starts a compile in the middle of that, against a tree that is part old
/// version and part new. Measured on the RadBulkSwitch fixture, a 12-file switch delivered
/// over 1.4 s produced two cycles, the first of which compiled 4 of the 12 files and
/// reported a FAILING test from source that is valid before and after the switch. A
/// spurious red on branch switch is worse than a slow one: it trains the developer to stop
/// believing the runner.</para>
///
/// <para>So the wait has two stages: block until something changed, then block until it has
/// STOPPED changing. <see cref="AwaitQuiet"/> is the second stage.</para>
///
/// <para><b>Known limit.</b> Quiescence is measured on the event stream, and event delivery
/// lags the writes it reports (FSEvents coalescing on macOS, the inotify queue on Linux).
/// A writer that pauses for longer than the quiet window mid-burst is therefore
/// indistinguishable from one that has finished, and the cycle runs early. Nothing observed
/// so far needs a longer window; <c>AL_RUNNER_WATCH_QUIET_MS</c> raises it if a very large
/// tree ever does.</para>
/// </summary>
internal sealed class SourceWatch
{
    /// <summary>
    /// How long the tree must be silent before a burst counts as finished. Long enough to
    /// bridge the gaps between a checkout's writes, short enough that a single save still
    /// feels immediate. Override with <c>AL_RUNNER_WATCH_QUIET_MS</c>; 0 disables the wait
    /// entirely, which is only sensible in a test that drives changes synchronously.
    /// </summary>
    private static readonly int QuietMs =
        int.TryParse(Environment.GetEnvironmentVariable("AL_RUNNER_WATCH_QUIET_MS"), out var ms)
        && ms >= 0 ? ms : 300;

    /// <summary>
    /// Ceiling on the wait. Something that writes into the tree continuously — a code
    /// generator, a build dropping artifacts — would otherwise hold the loop off forever,
    /// and never running is worse than running against a moving tree.
    /// </summary>
    private const int MaxQuietWaitMs = 10_000;

    private long _lastEventTicks;
    private int _overflowed;

    public System.Threading.ManualResetEventSlim Signal { get; } = new(false);

    /// <summary>
    /// Paths seen since the last drain. Advisory: it tells the reload whether the change was
    /// confined to AL sources of known apps, and nothing more. WHICH objects to recompile is
    /// decided by re-hashing the tree (<c>RadWorkspace.DiffFiles</c>), so a dropped event
    /// costs a wake-up, never a wrong delta.
    /// </summary>
    private ConcurrentQueue<string> ChangedPaths { get; } = new();

    /// <summary>
    /// Take the paths seen since the last call, or <c>null</c> if a notification overflow
    /// means the list is known to be INCOMPLETE.
    ///
    /// <para>The distinction matters because the consumer
    /// (<c>RadWorkspaceStore.PrepareBundleReload</c>) preserves warm compiler metadata when
    /// every changed path is a <c>.al</c> file under a known app — and an empty list
    /// satisfies "every" vacuously. Handing it an empty list after an overflow would
    /// therefore preserve metadata across a change that might have been an <c>app.json</c>
    /// edit or a file in an app the workspace has never seen. Null says "I do not know what
    /// changed", and the caller takes the clean-refresh path.</para>
    /// </summary>
    public List<string>? DrainChangedPaths()
    {
        var incomplete = Interlocked.Exchange(ref _overflowed, 0) == 1;
        var paths = new List<string>();
        while (ChangedPaths.TryDequeue(out var path)) paths.Add(path);
        return incomplete ? null : paths;
    }

    /// <summary>
    /// The live watchers. Nothing reads this list — it exists to hold the references, because
    /// a collected FileSystemWatcher silently stops raising events and the watch loop would
    /// then block forever on a tree that is changing.
    /// </summary>
    public List<FileSystemWatcher> Watchers { get; } = new();

    /// <summary>
    /// Note one file-change event. <paramref name="path"/> is null for a notification
    /// overflow, where the runner knows something changed but not what.
    /// </summary>
    public void Record(string? path)
    {
        if (path != null) ChangedPaths.Enqueue(path);
        else Interlocked.Exchange(ref _overflowed, 1);
        Interlocked.Exchange(ref _lastEventTicks, Environment.TickCount64);
        Signal.Set();
    }

    /// <summary>
    /// Block until no file-change event has arrived for <see cref="QuietMs"/>, so the cycle
    /// that follows compiles the settled tree rather than a half-applied one. Returns as soon
    /// as the tree is quiet; gives up after <see cref="MaxQuietWaitMs"/> and says so.
    /// </summary>
    public void AwaitQuiet()
    {
        if (QuietMs == 0) return;
        var deadline = Environment.TickCount64 + MaxQuietWaitMs;
        while (true)
        {
            var idle = Environment.TickCount64 - Interlocked.Read(ref _lastEventTicks);
            if (idle >= QuietMs) return;
            if (Environment.TickCount64 >= deadline)
            {
                Console.Error.WriteLine(
                    $"[watch] source tree still changing after {MaxQuietWaitMs / 1000}s — " +
                    "running against it as it stands.");
                return;
            }
            System.Threading.Thread.Sleep((int)Math.Min(QuietMs - idle, 50));
        }
    }
}
