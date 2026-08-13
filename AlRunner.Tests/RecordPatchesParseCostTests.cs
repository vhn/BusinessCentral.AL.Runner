// RecordPatchesParseCostTests — the cost invariant of the single most expensive thing a
// warm `--watch` cycle does.
//
// `RecordPatches.AddSourceDir` is what re-establishes the runner's view of the AL tree
// after every watch reload, and on a 7,000-file app it was measured at ~26 s per cycle —
// an order of magnitude more than the delta compile it surrounds. The reason was not I/O
// and not regex: it was that the file's text was handed to eight independent parsers, each
// of which built its OWN full AL syntax tree from it. One file, eight parses of the same
// bytes, every cycle.
//
// The claim this suite pins is therefore a COUNT, not a duration: whatever else changes,
// registering a source directory must build exactly one syntax tree per .al file. A
// wall-clock assertion would be flaky on CI and would not say what regressed; a count says
// exactly which invariant broke, and it breaks loudly the moment someone adds a ninth
// parser by copying the eighth.
//
// What this suite does NOT claim is that the extraction is correct — that is the job of the
// parser suites (PageExtensionParserTests, ReportParserTests, the RAD metadata suites, and
// every end-to-end test that resolves a table, page, report, query or xmlport). This one
// only guards the cost.

using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RecordPatchesParseCostTests(BcEngineFixture engine)
{
    private static readonly string TwentyObjectSrc = Path.Combine(
        RadFixtureSource(), "src");

    private static string RadFixtureSource() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "RadTwentyObject"));

    /// <summary>
    /// Object id for this suite's own probe page and report. Inside the fixture app's
    /// 71000-71199 range so it stays out of every other suite's way, and at the top of it so
    /// the fixture can grow without colliding.
    /// </summary>
    private const int ProbeId = 71198;

    /// <summary>
    /// Twenty files spanning every object kind the eight parsers between them care about —
    /// table, tableextension, page, pageextension, report, query, xmlport, enum,
    /// enumextension, codeunit. Registering them must cost twenty parses.
    /// </summary>
    [Fact]
    public void AddSourceDir_BuildsOneSyntaxTreePerFile_NotOnePerParser()
    {
        if (!engine.Ready)
        {
            Console.Error.WriteLine($"[skip] {engine.SkipReason}");
            return;
        }

        // A private copy: AddSourceDir de-dups by directory path, so re-registering the
        // checked-in fixture would silently parse nothing and the count would be zero.
        var dir = Path.Combine(
            Path.GetTempPath(), "al-runner-parse-cost", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.EnumerateFiles(TwentyObjectSrc, "*.al"))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));

        // Two probe objects on ids nothing else in this assembly declares. The parsed
        // dictionaries are process-wide and every suite in this collection writes into them,
        // so asserting on the fixture's own 71000 would pass on residue from whichever test
        // ran first. These can only become parsed by the AddSourceDir below.
        File.WriteAllText(Path.Combine(dir, "ParseCostProbe.Page.al"), """
            page 71198 "Parse Cost Probe Page"
            {
                PageType = Card;
                SourceTable = "RAD Perf Header";
            }
            """);
        File.WriteAllText(Path.Combine(dir, "ParseCostProbe.Report.al"), """
            report 71198 "Parse Cost Probe Report"
            {
                ProcessingOnly = true;
            }
            """);

        try
        {
            var files = Directory.GetFiles(dir, "*.al", SearchOption.AllDirectories).Length;
            Assert.Equal(22, files);
            Assert.False(RecordPatches.IsPageParsed(ProbeId),
                "the probe id is not private to this test any more — pick another");

            var before = RecordPatches.AlObjectParseCount;
            RecordPatches.AddSourceDir(dir);
            var parses = RecordPatches.AlObjectParseCount - before;

            Assert.True(parses == files,
                $"registering {files} .al file(s) built {parses} AL syntax trees. One tree per " +
                "file is the contract — a parser that re-parses the text it was handed " +
                $"multiplies the warm watch cycle by however many parsers there are ({parses / files}x here).");

            // The dispatch really did reach the parsers, rather than the count being low
            // because nothing ran: the page parser and the report parser are two of the
            // eight, each fed by a different filter over the shared node list.
            Assert.True(RecordPatches.IsPageParsed(ProbeId),
                "the probe page was not parsed — the single-pass dispatch skipped the page parser");
            Assert.True(RecordPatches.IsReportProcessingOnly(ProbeId),
                "the probe report's ProcessingOnly was not parsed — the single-pass dispatch " +
                "skipped the report parser");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The other direction: a directory with no AL in it must cost nothing. Without this,
    /// a "one parse per file" implementation that parsed unconditionally — including the
    /// empty-text case the parser short-circuits today — would still satisfy the count
    /// above on a populated tree while doubling the cost of every dependency source dir
    /// that happens to hold no .al files.
    /// </summary>
    [Fact]
    public void AddSourceDir_WithNoAlFiles_ParsesNothing()
    {
        if (!engine.Ready)
        {
            Console.Error.WriteLine($"[skip] {engine.SkipReason}");
            return;
        }

        var dir = Path.Combine(
            Path.GetTempPath(), "al-runner-parse-cost-empty", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "app.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "notes.md"), "not AL");

        try
        {
            var before = RecordPatches.AlObjectParseCount;
            RecordPatches.AddSourceDir(dir);
            Assert.Equal(0, RecordPatches.AlObjectParseCount - before);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
