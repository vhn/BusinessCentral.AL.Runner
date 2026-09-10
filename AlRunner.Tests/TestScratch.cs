// TestScratch — per-run scratch directories for tests.
//
// ADAPTED FOR THIS FORK, not carried verbatim from upstream. Upstream's version routes both
// helpers through AlRunner.Infrastructure.ScratchDirs (#2706), a 504-line directory-ownership
// subsystem this fork does not have: it writes a `.owner` sidecar naming the test host, deletes
// reserved directories at ProcessExit, and lets the next runner start reclaim what a killed host
// left behind. Porting that subsystem to make one test file compile would drag unrelated
// infrastructure into a query-engine change, so this fork keeps the path shape and drops the
// ownership net.
//
// The path shape is byte-identical to upstream's and to the hand-written
// `Path.Combine(Path.GetTempPath(), "<name>", Guid.NewGuid().ToString("N"))` it replaced, so
// call-site semantics are unchanged. The leaf is deliberately NOT created — some tests rely on
// observing whether the runner created it.
//
// What is lost relative to upstream: nothing a test asserts, only the automatic cleanup that
// this fork never had in the first place. If the ownership subsystem is ever ported here, this
// file should go back to upstream's version.

namespace AlRunner.Tests;

internal static class TestScratch
{
    /// <summary><c>&lt;temp&gt;/&lt;prefix&gt;/&lt;guid&gt;</c> — the nested shape, one container per fixture kind.</summary>
    public static string Dir(string prefix)
        => Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
}
