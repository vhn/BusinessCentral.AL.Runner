// BcAssemblerPolyfillRedirectTests — the redirect pass walks each file once, and that one
// walk computes exactly what the 35 sequential string.Replace calls it replaces computed.
//
// What the pass is
// ----------------
// BcAssembler.ApplyPolyfillRedirects rewrites BC-emitted C# so calls to service-tier members
// the skeleton runtime cannot serve land on AlRunnerShim.NavRuntimeHelpersShim instead. It ran
// as one `code.Replace(from, to)` per entry in _polyfillRedirects — 35 walks of every generated
// source, on every compile, each matching call also allocating a fresh copy of the whole file.
// CompileCore calls it per source inside a Parallel.For, so it runs ~6,950 times for npcore's
// Application app alone (165 MB of generated C# in total).
//
// What these tests pin
// --------------------
//  * MECHANISM (the RED one) — one call walks the text ONCE, not once per redirect
//    (BcAssembler.PolyfillRedirectPassCount, a per-thread COUNT — never a duration; timing
//    assertions are exactly what makes a perf test flaky on a loaded CI box). This read 35
//    before the rewrite.
//  * EQUIVALENCE (differential) — production output is byte-identical to the naive sequential
//    algorithm it replaces, over inputs built from every entry in the real table: each key
//    alone, all keys in one document, keys concatenated with no separator, keys repeated
//    adjacently, keys embedded in longer identifiers, and text containing none of them. This is
//    the regression net: the naive version is the specification.
//  * EQUIVALENCE (structural) — the four properties of the redirect TABLE that make
//    "leftmost match wins, one pass" and "redirect 1 everywhere, then redirect 2 everywhere"
//    the same function. The differential test can only speak for the inputs it was given;
//    these speak for all of them, and they are what fails if someone later adds a redirect
//    whose key overlaps an existing one — the case where the two algorithms genuinely diverge
//    and the differential test would keep passing because nobody thought to feed it the
//    overlapping pair.
//
// Pure string logic — no BC engine, no artifacts, no collection needed.
using Xunit;

namespace AlRunner.Tests;

public sealed class BcAssemblerPolyfillRedirectTests
{
    private static IReadOnlyList<(string From, string To)> Redirects => BcAssembler.PolyfillRedirectsForTests;

    /// <summary>The algorithm the single pass replaces. This is the specification the
    /// differential tests below compare against — deliberately written out in full here rather
    /// than referenced, so that changing the production implementation cannot silently change
    /// what "correct" means.</summary>
    private static string NaiveSequential(string code)
    {
        foreach (var (from, to) in Redirects)
            code = code.Replace(from, to);
        return code;
    }

    // ── Mechanism ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void OneCall_WalksTheTextOnce_NotOncePerRedirect()
    {
        var code = SampleWithEveryRedirect();

        var before = BcAssembler.PolyfillRedirectPassCount;
        BcAssembler.ApplyPolyfillRedirectsForTests(code);
        var after = BcAssembler.PolyfillRedirectPassCount;

        // Read Redirects.Count (35) before the rewrite: one full walk of the file per entry,
        // whether or not that entry occurs in it.
        Assert.Equal(1, after - before);
    }

    [Fact]
    public void TextWithNoRedirect_IsReturnedUnchanged_AndStillCostsOneWalk()
    {
        const string code = "public static class Plain { public static int Value => 42; }";

        var before = BcAssembler.PolyfillRedirectPassCount;
        var result = BcAssembler.ApplyPolyfillRedirectsForTests(code);
        var after = BcAssembler.PolyfillRedirectPassCount;

        Assert.Equal(1, after - before);
        // Not merely equal — the SAME instance. A file that needs no redirect is the common
        // case, and it must not cost a copy of itself.
        Assert.Same(code, result);
    }

    // ── Equivalence: differential against the algorithm being replaced ───────────────────

    [Fact]
    public void EveryRedirectAlone_MatchesTheSequentialResult()
    {
        foreach (var (from, _) in Redirects)
        {
            var code = $"    var x = {from}session, arg);\n";
            Assert.Equal(NaiveSequential(code), BcAssembler.ApplyPolyfillRedirectsForTests(code));
            // Not vacuous: the redirect really did fire.
            Assert.DoesNotContain(from, BcAssembler.ApplyPolyfillRedirectsForTests(code), StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(EquivalenceCorpus))]
    public void ProductionOutput_IsByteIdenticalToTheSequentialAlgorithm(string label, string code)
    {
        Assert.Equal(NaiveSequential(code), BcAssembler.ApplyPolyfillRedirectsForTests(code));
        Assert.False(string.IsNullOrEmpty(label));
    }

    public static TheoryData<string, string> EquivalenceCorpus()
    {
        var keys = Redirects.Select(r => r.From).ToList();
        var data = new TheoryData<string, string>
        {
            { "empty", "" },
            { "no redirect at all", "namespace N { class C { void M() { } } }" },
            { "every redirect in one document", SampleWithEveryRedirect() },
            // No separator anywhere: the only shape where one key could begin inside another
            // key's span, which is where a one-pass leftmost match and a per-redirect sweep
            // would part company if the table ever allowed it.
            { "all keys concatenated", string.Concat(keys) },
            // Each key immediately followed by itself — the classic off-by-one for a scanner
            // that resumes at the wrong offset after a replacement.
            { "each key doubled", string.Concat(keys.Select(k => k + k + "\n")) },
            // A key that is a substring of a longer identifier still matches under both
            // algorithms (the pass is textual, by design). Pinned so that stays true.
            { "keys inside longer identifiers", string.Concat(keys.Select(k => "Prefixed" + k + "Suffix\n")) },
            // A key split across a newline must match under neither.
            { "keys split by a newline", string.Concat(keys.Select(k => k[..(k.Length / 2)] + "\n" + k[(k.Length / 2)..] + "\n")) },
            { "one key at the very start", keys[0] + " rest of file" },
            { "one key at the very end", "start of file " + keys[^1] },
            { "only a key", keys[0] },
        };

        // Anchor characters that begin no redirect, packed around real ones. The one-pass form
        // scans for each key's FIRST character and only then tries to match, so a run of
        // anchors that go nowhere is where a scanner most easily loses its place — either by
        // advancing the copy cursor past text it never emitted, or by failing to resume inside
        // a run it already rejected. Sequential string.Replace has no such state to lose, which
        // is exactly why this case has to be compared against it.
        var anchors = new string(Redirects.Select(r => r.From[0]).Distinct().ToArray());
        foreach (var (key, i) in keys.Select((k, i) => (k, i)).Take(6))
        {
            data.Add($"anchor noise around key {i}",
                anchors + anchors + key + anchors + key + anchors);
            data.Add($"anchor noise interleaved with key {i}",
                string.Concat(anchors.Select(c => c + "x")) + key + string.Concat(anchors.Select(c => "y" + c)));
        }
        return data;
    }

    // ── Equivalence: the structural properties the one-pass rewrite rests on ────────────

    [Fact]
    public void NoRedirectKeyIsASubstringOfAnotherKey()
    {
        foreach (var (a, _) in Redirects)
            foreach (var (b, _) in Redirects)
            {
                if (ReferenceEquals(a, b)) continue;
                Assert.False(b.Contains(a, StringComparison.Ordinal),
                    $"redirect key '{a}' occurs inside key '{b}'. Two keys can then match at or " +
                    "across the same position, and which one wins becomes a function of the order " +
                    "of _polyfillRedirects — which the one-pass rewrite does not preserve. Split " +
                    "the pair, or make the pass order-aware and say so here.");
            }
    }

    [Fact]
    public void NoRedirectKeyOccursInsideAReplacement()
    {
        foreach (var (_, to) in Redirects)
            foreach (var (from, _) in Redirects)
                Assert.False(to.Contains(from, StringComparison.Ordinal),
                    $"replacement '{to}' contains redirect key '{from}'. The sequential form would " +
                    "rewrite that text again on a later sweep; the one-pass form never revisits " +
                    "what it has emitted, so the two would produce different C#.");
    }

    [Fact]
    public void NoRedirectKeyCanSpanTheEndOfAReplacementIntoTheTextAfterIt()
    {
        foreach (var (_, to) in Redirects)
            foreach (var (from, _) in Redirects)
                for (int len = 1; len < from.Length; len++)
                    Assert.False(to.EndsWith(from[..len], StringComparison.Ordinal),
                        $"replacement '{to}' ends with '{from[..len]}', a prefix of redirect key " +
                        $"'{from}'. A later sequential sweep could match that key across the seam " +
                        "between the replacement and the original text following it; the one-pass " +
                        "form cannot see such a match at all.");
    }

    [Fact]
    public void NoRedirectKeyCanSpanTheStartOfAReplacement()
    {
        foreach (var (_, to) in Redirects)
            foreach (var (from, _) in Redirects)
                for (int len = 1; len < from.Length; len++)
                    Assert.False(to.StartsWith(from[len..], StringComparison.Ordinal),
                        $"replacement '{to}' starts with '{from[len..]}', a suffix of redirect key " +
                        $"'{from}'. A later sequential sweep could match that key across the seam " +
                        "between the untouched text and the replacement; the one-pass form cannot.");
    }

    [Fact]
    public void NoTwoRedirectKeysCanOverlap()
    {
        foreach (var (a, _) in Redirects)
            foreach (var (b, _) in Redirects)
                for (int len = 1; len < a.Length; len++)
                    Assert.False(b.StartsWith(a[len..], StringComparison.Ordinal) && a[len..].Length < b.Length,
                        $"redirect key '{a}' can overlap key '{b}' (the suffix '{a[len..]}' of the " +
                        "first is a prefix of the second). Which of them fires on text containing " +
                        "both then depends on sweep order, so the sequential and one-pass forms " +
                        "would disagree.");
    }

    [Fact]
    public void TheRedirectTableIsNotEmpty_AndNoEntryIsDegenerate()
    {
        // Guards every test above from passing vacuously against an empty or malformed table.
        Assert.True(Redirects.Count >= 30, $"only {Redirects.Count} redirects — table looks truncated");
        foreach (var (from, to) in Redirects)
        {
            Assert.False(string.IsNullOrEmpty(from));
            Assert.False(string.IsNullOrEmpty(to));
            Assert.NotEqual(from, to);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    /// <summary>Every redirect key once, in C#-shaped surroundings, so a single document
    /// exercises the whole table.</summary>
    private static string SampleWithEveryRedirect()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("namespace Generated { public static class M {");
        int i = 0;
        foreach (var (from, _) in Redirects)
        {
            sb.AppendLine($"    // call site {i}");
            sb.AppendLine($"    public static void Call{i}() {{ var r{i} = {from}session, {i}); }}");
            i++;
        }
        sb.AppendLine("} }");
        return sb.ToString();
    }
}
