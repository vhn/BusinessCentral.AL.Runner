// ConcurrentEmitDefaultTests — BC's emit phase runs across threads by default, and the
// capture order it produces is put back into a deterministic sequence.
//
// Why on by default
// -----------------
// ConcurrentEmit defaults to false on both CompilationOptions and EmitOptions, so every one
// of npcore's 6,956 objects reached AddApplicationObject on a single thread while the bind
// phase beside it used the whole machine. Turning it on was measured, refuted, and then
// re-measured with the opposite result — see BcCompiler.ConcurrentEmitEnabled for both legs
// and .context/perf/concurrent-emit-ab.sh for the harness. The short version: on a
// memory-starved host without Server GC it lost; under the shipped ServerGarbageCollection
// the arms do not overlap and the worst on-leg beats the best off-leg by 1.45x on the emit
// phase (median 1.8x), with no heap penalty.
//
// Why the ordering test is the load-bearing one here
// --------------------------------------------------
// Concurrency changes an observable: the order objects arrive in becomes the syntax-tree
// order of the C# compilation and therefore the emitted assembly's member layout. A sort
// keyed on Name alone LOOKS like it fixes that and does not, because AL only requires an
// object name to be unique per object TYPE — a table and its page routinely share one, 439
// times over in npcore's Application app. OrderBy is stable, so each tie group kept its
// arrival order, i.e. exactly the nondeterminism being sorted away. These tests pin the
// (Name, Code) total order, and the reversed-input case genuinely discriminates: it fails
// against a Name-only sort rather than passing for free.
using AlRunner;
using Xunit;

namespace AlRunner.Tests;

[Collection("concurrent-emit-env")]
public sealed class ConcurrentEmitDefaultTests
{
    private const string Flag = "AL_RUNNER_BC_CONCURRENT_EMIT";

    private static bool ReadEnabled()
    {
        var p = typeof(BcCompiler).GetProperty(
            "ConcurrentEmitEnabled",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(p);
        return (bool)p!.GetValue(null)!;
    }

    private static void WithFlag(string? value, Action assert)
    {
        var saved = Environment.GetEnvironmentVariable(Flag);
        try
        {
            Environment.SetEnvironmentVariable(Flag, value);
            assert();
        }
        finally { Environment.SetEnvironmentVariable(Flag, saved); }
    }

    [Fact]
    public void Unset_IsOn_SoTheDefaultIsConcurrent()
        => WithFlag(null, () => Assert.True(ReadEnabled(),
            "Concurrent BC emit must be the default. If this fails the flag has been reverted to "
            + "opt-in (== \"1\"); see BcCompiler.ConcurrentEmitEnabled for the measurement that "
            + "made it the default."));

    [Fact]
    public void Zero_TurnsItOff_WhichIsTheOnlyOverride()
        => WithFlag("0", () => Assert.False(ReadEnabled(),
            "AL_RUNNER_BC_CONCURRENT_EMIT=0 is the documented escape hatch AND how the A/B "
            + "harness drives its off arm — it must keep working."));

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("")]
    [InlineData("no")]
    public void AnyValueOtherThanZero_LeavesItOn(string value)
        => WithFlag(value, () => Assert.True(ReadEnabled()));

    // ── the capture ordering ────────────────────────────────────────────────────────────

    [Fact]
    public void TwoObjectsSharingAName_OrderIdenticallyWhateverOrderTheyArriveIn()
    {
        // The npcore shape: a table and its page carry the same AL name, so Name ties.
        var table = new EmittedSource("NPR Adyen Setup", "class Record6014400 { }");
        var page = new EmittedSource("NPR Adyen Setup", "class Page6014400 { }");

        var arrivedTableFirst = BcCompiler.OrderCapturesDeterministically([table, page]);
        var arrivedPageFirst = BcCompiler.OrderCapturesDeterministically([page, table]);

        // Same sequence both times — this is what a stable Name-only sort does NOT give,
        // because it would preserve each input's own arrival order within the tie.
        Assert.Equal(
            arrivedTableFirst.Select(s => s.Code).ToArray(),
            arrivedPageFirst.Select(s => s.Code).ToArray());

        // And concretely: Code breaks the tie ordinally, so Page sorts before Record.
        Assert.Equal(
            new[] { "class Page6014400 { }", "class Record6014400 { }" },
            arrivedTableFirst.Select(s => s.Code).ToArray());
    }

    [Fact]
    public void NameIsThePrimaryKey_AndCodeOnlyBreaksTies()
    {
        var beta = new EmittedSource("Beta", "aaa");
        var alpha = new EmittedSource("Alpha", "zzz");

        var ordered = BcCompiler.OrderCapturesDeterministically([beta, alpha]);

        // Alpha first despite its Code sorting last — otherwise the tie-break has quietly
        // become the primary key and the emitted module is grouped by generated text.
        Assert.Equal(new[] { "Alpha", "Beta" }, ordered.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void ManyCapturesShufflePermutationsToOneSequence()
    {
        // Four objects over two names — the tie groups a real module is full of.
        EmittedSource[] sources =
        [
            new("Shared", "b"), new("Unique", "a"), new("Shared", "a"), new("Zed", "a"),
        ];
        var expected = BcCompiler.OrderCapturesDeterministically(sources)
            .Select(s => s.Name + "|" + s.Code).ToArray();

        Assert.Equal(["Shared|a", "Shared|b", "Unique|a", "Zed|a"], expected);

        // Every permutation of the same set collapses to that one sequence.
        foreach (var permutation in Permutations(sources))
            Assert.Equal(expected, BcCompiler.OrderCapturesDeterministically(permutation)
                .Select(s => s.Name + "|" + s.Code).ToArray());
    }

    [Fact]
    public void EmptyAndSingleton_AreReturnedIntact()
    {
        Assert.Empty(BcCompiler.OrderCapturesDeterministically([]));

        var only = new EmittedSource("Solo", "x");
        Assert.Equal(["Solo"], BcCompiler.OrderCapturesDeterministically([only])
            .Select(s => s.Name).ToArray());
    }

    private static IEnumerable<EmittedSource[]> Permutations(EmittedSource[] items)
    {
        if (items.Length <= 1) { yield return items; yield break; }
        for (int i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, j) => j != i).ToArray();
            foreach (var tail in Permutations(rest))
                yield return new[] { items[i] }.Concat(tail).ToArray();
        }
    }
}
