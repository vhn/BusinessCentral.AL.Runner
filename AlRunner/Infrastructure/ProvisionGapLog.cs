// ProvisionGapLog — carries a provisioning-gap message from the dependency load that detects it
// up to the run summary, without making it any quieter on the way.
//
// DependencyLoader reports a known Microsoft platform runtime app resolved to a symbol-only
// package (procedure bodies external/native — it cannot execute) several layers below the bundle
// loop that builds the run's BucketResults, so it has nothing to hand the message to. It used to
// only write it to stderr. Measured on npcore: four such blocks at ~20 s, then 212 s of
// emit+compile, then `The object with ID 0 does not have a member with that ID` — precisely what
// those blocks predicted — with the summary never mentioning it. A caller reading the bottom of
// the run concludes their AL is broken rather than their package cache is unprovisioned.
//
// Deliberately a collector, not a replacement: Report still writes to stderr exactly as before
// (.claude/rules/loud-failures.md — this may not get quieter), and only ALSO records.
namespace AlRunner.Infrastructure;

internal static class ProvisionGapLog
{
    private static readonly object _lock = new();
    private static List<string> _gaps = new();

    /// <summary>
    /// Forget the previous bundle's gaps. Called once per bundle: a run walks bundles in
    /// sequence and a watch session re-runs them forever, so without this the first bundle's
    /// missing package is attributed to every later bundle and every later cycle.
    /// </summary>
    internal static void Reset()
    {
        lock (_lock) _gaps = new List<string>();
    }

    /// <summary>
    /// Report one gap: loud on stderr (unchanged), and recorded for the summary.
    /// </summary>
    internal static void Report(string message)
    {
        Console.Error.WriteLine(message);
        lock (_lock) _gaps.Add(message);
    }

    /// <summary>
    /// Snapshot of what has been reported since the last <see cref="Reset"/>. A copy, so a
    /// caller that has already read it keeps what it read when the next bundle resets.
    /// </summary>
    internal static IReadOnlyList<string> Collected
    {
        get { lock (_lock) return _gaps.ToList(); }
    }
}
