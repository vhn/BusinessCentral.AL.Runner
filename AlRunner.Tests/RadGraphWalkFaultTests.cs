// RadGraphWalkFaultTests — a fault inside the reference-graph walk must cost the cycle its
// delta, never cost the graph an edge.
//
// The walk in `BcCompiler.MapObjectReferences` asks Microsoft for a semantic model per file
// and calls `GetSymbolInfo` on every node, across `Environment.ProcessorCount` threads. It
// used to wrap each node in `try { … } catch { }`, justified as "a malformed/incomplete node
// has no useful dependency edge".
//
// That justification does not survive measurement. A probe over BC 28.1 — one clean
// two-codeunit compile plus six malformed shapes (truncated body, unclosed object, unknown
// type, garbage tokens, bad expressions, an empty file; 26 diagnostics between them) — put
// 168 nodes through `GetSymbolInfo` and got 168 answers. It never threw. So the catch
// absorbed no case that occurs, and the only faults it could still swallow were the ones the
// parallel walk itself introduces.
//
// Swallowing those is the worst available outcome. The graph decides which callers a later
// delta rebinds, and a lost edge is indistinguishable from an object that calls nothing — so
// the symptom is not an error but a caller that should have rebound and did not, several
// cycles later, reported green. Both callers of the walk already fail safe (no baseline /
// full-compile fallback, each loudly named), which is why propagating is strictly better:
// it costs speed and self-heals on the next cycle.
//
// WHAT THESE TESTS PIN
//
//   * NEGATIVE — a fault raised where the old catch sat aborts the delta, names ITSELF in the
//     cycle notes, leaves the workspace's committed baseline untouched, and produces a result
//     that cannot be committed. Against the old code this test fails on every one of those
//     four claims: the fault is swallowed and the delta commits a graph with edges missing.
//
//   * UNWRAPPED — the note names `InvalidOperationException`, not `AggregateException`. Only
//     the unwrap in `MapObjectReferences` makes that true, and it is what keeps the message a
//     developer reads pointing at the bug instead of at `Parallel.For`.
//
//   * POSITIVE — with the same seam armed to count instead of throw, the delta still takes the
//     delta path and still commits. This is what makes the negative test mean something: it
//     proves the seam is on the live walk's path, so the injected fault was raised where the
//     production code actually runs.

using AlRunner;
using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadGraphWalkFaultTests(BcEngineFixture engine)
{
    private const string FixtureName = "RadByNameSubtype";
    private const string ModuleName = "RAD ByName Subtype";
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000009");
    private const int EmittedObjectCount = 3;

    private const string CallerFile = "SubtypeCaller.Codeunit.al";
    private const string CallerBefore = "Take(Target) + 1";
    private const string CallerAfter = "Take(Target) + 2";

    /// <summary>
    /// The message the injected fault carries, asserted verbatim in the cycle note so a green
    /// run cannot come from some unrelated exception that happened to abort the delta.
    /// </summary>
    private const string FaultMessage = "injected binder fault";

    [SkippableFact]
    public void AFaultInsideTheGraphWalk_AbortsTheDelta_AndLeavesTheCommittedBaselineIntact()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            FixtureName, ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                // The baseline the seed committed. It must still be there afterwards — that is
                // the correctness half of the claim, as distinct from "the cycle was slow".
                Assert.True(workspace.HasBaseline);

                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, CallerFile), CallerBefore, CallerAfter);

                // Drain first: the seed compile files its own notes, and asserting on a queue
                // that still holds them would pass on the wrong evidence.
                RadCycleNotes.Drain();

                RadEmitResult delta;
                BcCompiler.GraphWalkProbeForTests =
                    () => throw new InvalidOperationException(FaultMessage);
                try { delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace); }
                finally { BcCompiler.GraphWalkProbeForTests = null; }

                var notes = string.Join(" | ", RadCycleNotes.Drain());

                // 1. The fault was not swallowed: the delta gave up and the cycle fell back.
                Assert.True(delta.FullRebuild,
                    "a fault inside the graph walk did not abort the delta — it was swallowed, "
                    + "and the cycle committed a reference graph with edges missing. Notes: " + notes);

                // 2. It named itself, so the developer reading watch output can act on it.
                Assert.Contains(nameof(InvalidOperationException), notes);
                Assert.Contains(FaultMessage, notes);

                // 3. It named the FAULT, not Parallel.For's wrapper. Without the unwrap this
                //    reads "AggregateException: One or more errors occurred", which identifies
                //    the loop and not the bug.
                Assert.DoesNotContain(nameof(AggregateException), notes);

                // 4. Nothing partial was committed. The fallback compile hits the same fault in
                //    TryBuildBaselineSnapshot, so it produces no baseline either — and the
                //    baseline the seed committed is still the one the workspace holds, which is
                //    what lets the next cycle retry the same edit from a known-good state.
                Assert.False(delta.CanCommit,
                    "the fallback produced a committable baseline despite the walk faulting");
                Assert.True(workspace.HasBaseline,
                    "the faulted cycle dropped the previously committed baseline");
            });
    }

    /// <summary>
    /// The premise the negative test rests on: this seam is on the path the production walk
    /// takes. Armed to count rather than throw, the same edit still deltas and still commits —
    /// so the fault above was injected into live code, not into a branch nothing reaches.
    /// </summary>
    [SkippableFact]
    public void TheSameSeam_ArmedToCount_LeavesTheDeltaPathAndItsCommitUntouched()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            FixtureName, ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, CallerFile), CallerBefore, CallerAfter);

                var nodes = 0;
                RadEmitResult delta;
                // Interlocked, not ++: the walk runs the body on ProcessorCount threads, so a
                // plain increment would under-count and the assertion below would be measuring
                // a race rather than the walk.
                BcCompiler.GraphWalkProbeForTests = () => Interlocked.Increment(ref nodes);
                try { delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace); }
                finally { BcCompiler.GraphWalkProbeForTests = null; }

                Assert.False(delta.FullRebuild,
                    "the counting seam changed the cycle's shape, so the fault test's premise "
                    + "does not hold");
                Assert.True(delta.CanCommit);

                // The three fixture objects cannot be described by three nodes; a walk that
                // visited only the object declarations would find no references at all. The
                // bound is deliberately far below the real count (a `codeunit` body alone is
                // dozens of nodes) so it pins "the walk ran over real syntax" without pinning
                // a number BC is free to change.
                Assert.True(nodes > EmittedObjectCount * 10,
                    $"the graph walk visited only {nodes} nodes, which is too few to be the "
                    + "fixture's syntax — the seam is not where the walk runs");
            });
    }
}
