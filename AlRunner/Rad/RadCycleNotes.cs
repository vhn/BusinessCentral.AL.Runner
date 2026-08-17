using System.Collections.Concurrent;

namespace AlRunner.Rad;

/// <summary>
/// Why a warm cycle rebuilt a whole module instead of deltaing it.
///
/// <para>Every decision to fall back already writes a <c>[watch]</c> line to stderr, and in the
/// interactive <c>--watch</c> dashboard nobody sees it: the bundle loop redirects BOTH streams
/// to <c>TextWriter.Null</c> while it runs, so the painted frame is not scrolled away (see
/// Program.cs, `stdoutSilenced`). The reason a cycle suddenly cost minutes instead of a second
/// was therefore visible only under <c>--verbose</c> or on a redirected stdout — which is to
/// say, not to the developer watching the dashboard.</para>
///
/// <para>So the reason is recorded here as well as logged, and the watch loop drains it after
/// the bundle loop and hands it to the dashboard. The collector is process-wide because the
/// decision is made deep inside the compile path, several layers below anything the watch loop
/// holds a reference to; threading a sink down through <c>BcCompiler</c> and
/// <c>RadWorkspace</c> would touch every call site to carry one string out.</para>
/// </summary>
/// <para>Deliberately <c>internal</c>: one process-wide queue with a single drainer is exactly
/// right for the CLI watch loop, which compiles bundles serially and drains once per cycle, and
/// exactly wrong for anything concurrent — two callers of the public
/// <see cref="BcCompiler.EmitIncremental"/> would steal each other's notes. Keeping it out of the
/// public surface keeps that constraint enforceable.</para>
internal static class RadCycleNotes
{
    /// <summary>
    /// Cap on retained notes, so a host that never drains cannot grow this without bound. One
    /// cycle produces at most one note per app; anything approaching this is already a wall of
    /// text nobody reads, so the OLDEST are dropped — the most recent cycle is the one on screen.
    /// </summary>
    private const int MaxRetained = 256;

    private static readonly ConcurrentQueue<string> _notes = new();
    private static readonly ConcurrentQueue<string> _rebinds = new();

    /// <summary>
    /// Record that <paramref name="moduleName"/> is being compiled in full, and why.
    /// <paramref name="reason"/> should read as a cause a developer recognises — "app.json
    /// changed the app version: 1.0.0.0 → 1.0.1.0", not "the reference surface changed".
    /// </summary>
    internal static void FullCompile(string moduleName, string reason)
    {
        _notes.Enqueue($"{moduleName}: {reason}");
        while (_notes.Count > MaxRetained) _notes.TryDequeue(out _);
    }

    /// <summary>
    /// Record that <paramref name="moduleName"/> re-emitted objects it did not itself change,
    /// because a SIBLING app in the bundle moved a callable surface this one's generated calls
    /// bake the member ids of.
    ///
    /// <para>Kept apart from <see cref="FullCompile"/> rather than sharing its queue, because the
    /// two say opposite things about the cycle. A full compile is the slow path and its note
    /// explains a cost; a cross-app rebind is the NARROW path working correctly — one file per
    /// caller instead of the whole module. Rendering it under a "full recompile" heading would
    /// send a developer hunting a cascade that did not happen.</para>
    /// </summary>
    internal static void Rebind(string moduleName, string reason)
    {
        _rebinds.Enqueue($"{moduleName}: {reason}");
        while (_rebinds.Count > MaxRetained) _rebinds.TryDequeue(out _);
    }

    /// <summary>
    /// Take and clear everything recorded since the last drain. Called once per watch cycle,
    /// after the bundle loop has restored the console streams.
    /// </summary>
    internal static IReadOnlyList<string> Drain()
    {
        var drained = new List<string>();
        while (_notes.TryDequeue(out var note)) drained.Add(note);
        return drained;
    }

    /// <summary>Same, for the cross-app rebind notes recorded by <see cref="Rebind"/>.</summary>
    internal static IReadOnlyList<string> DrainRebinds()
    {
        var drained = new List<string>();
        while (_rebinds.TryDequeue(out var note)) drained.Add(note);
        return drained;
    }
}
