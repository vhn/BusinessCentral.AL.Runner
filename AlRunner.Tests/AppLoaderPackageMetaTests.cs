// AppLoaderPackageMetaTests — the two questions every consumer asks about a `.app` (who is
// this package, and can BC's native scanner serve it) are answered by ONE read of the file,
// and that read is a stream, not a slurp.
//
// What was wrong
// --------------
// AppLoader.ReadManifest streams the package via OpenAppZip — ZipArchive's read-mode ctor
// parses the central directory and inflates only the entries actually opened. AppLoader
// .HasSymbolReference did the opposite: File.ReadAllBytes, the WHOLE package, to answer a
// question about one entry NAME. Base Application is ~98MB and the platform-apps dir is 113
// packages / 138MB, and BcCompiler's .app metadata scan asks both questions about every one of
// them on every GetSharedReferences call. That whole-file read is why the scan's fan-out is
// capped (a cold compile is memory-bound, so N workers each holding a package-sized byte[] is
// the thing that had to be bounded) and why the scan touched each package twice.
//
// What these tests pin, and how each one can fail
// -----------------------------------------------
//  * ONE READ PER PACKAGE (the RED one) — asking both questions in either order opens the
//    package's file exactly ONCE. Stated as an open COUNT, via
//    AppLoader.PackageOpenCountForTests: the repo's established idiom for "was this served from
//    a cache or genuinely recomputed" (cf. ManifestParseInvocationCountForTests,
//    R2rExtractInvocationCountForTests, BcAppSymbolCache.ParseInvocationCountByPath). No
//    assertion here is a duration — a "the scan got faster" test measures the CI box, not the
//    code. Against the two-separate-reads implementation this reads 2 and the test fails.
//  * WARM DISK INDEX (the cross-process claim) — the symbol-reference flag is carried in the
//    same app-manifests index entry as the manifest, so a SECOND process (a later al-runner
//    invocation; CI runs four at once) answers both questions with ZERO package reads. Against
//    an implementation that caches only the manifest, the flag costs a full re-read of all
//    138MB in every process, and this test fails.
//  * LEGACY INDEX ENTRY — an index entry written before the flag existed must be RECOMPUTED,
//    never read as `false`. This is the schema-evolution trap with teeth: a false negative here
//    silently drops symbol-bearing packages from the scan, which surfaces as AL1023 "package
//    file is not valid" attributed to the COMPILATION, on a package nothing referenced. Verified
//    by mutation — make FromPayload default the absent flag to false and this test goes red.
//  * EQUIVALENCE ACROSS EVERY PACKAGE SHAPE — flat, R2R-with-nested-.app (flag in the nested
//    package, in the outer zip, or absent), malformed manifest XML, non-zip garbage, missing
//    file. Both answers, both entry points. This is the net under the rewrite: the streamed read
//    must reach the same verdicts the whole-file read did, including for the R2R packages whose
//    nested .app is the only thing carrying either answer.
//  * INVALIDATION — a package rewritten in place (InProcessAppPackager's synthetic .apps, a
//    --watch rebuild) must flip the flag, not serve the stale one. The cache is keyed by
//    path+length+mtime, so this is the test that proves the flag is keyed like the manifest and
//    not pinned for the life of the process.
//  * SIZE INDEPENDENCE — 32MB of payload neither question reads must not change what the read
//    costs. Stated as the DIFFERENCE between two otherwise-identical fixtures, because an
//    absolute allocation ceiling would be pinning ZipArchive's per-entry overhead and the size
//    of whichever fixture happened to be here. Verified by mutation: restore the slurp and the
//    difference becomes 32MB.
//  * THE REAL PACKAGES — the fixtures here are kilobytes; `Microsoft_Base Application` is 93MB
//    wrapped around a nested .app with thousands of entries. Every real Microsoft package in the
//    platform-apps dir CI provisions must answer both questions correctly.
//
// Joins CacheRootsSerialCollection: AppLoader's memo and the CacheRoots override are both
// process-wide mutable statics, and this class writes to the on-disk index (see that
// collection's header, and AppLoaderManifestCacheTests, for the full rationale).
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AlRunner;
using AlRunner.Infrastructure;
using Xunit;

namespace AlRunner.Tests;

[Collection(CacheRootsSerialCollection.Name)]
public sealed class AppLoaderPackageMetaTests : IDisposable
{
    private readonly string _cacheRoot;
    private readonly string _srcDir;

    public AppLoaderPackageMetaTests()
    {
        _cacheRoot = NewTempDir("cache");
        _srcDir = NewTempDir("src");
        CacheRoots.SetOverride(_cacheRoot);
        AppLoader.ResetManifestMemoForTests();
    }

    public void Dispose()
    {
        CacheRoots.ResetForTests();
        AppLoader.ResetManifestMemoForTests();
        try { Directory.Delete(_cacheRoot, recursive: true); } catch { /* best-effort */ }
        try { Directory.Delete(_srcDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── One read per package ────────────────────────────────────────────────────────────

    /// <summary>
    /// THE mechanism test. Both questions, asked separately, in both orders, cost ONE open of
    /// the package file — the second question is served from what the first read already
    /// learned. Two separate reads per package is exactly what the .app metadata scan used to
    /// pay 113 times per pass.
    /// </summary>
    [Fact]
    public void BothQuestions_InEitherOrder_OpenThePackageFileOnce()
    {
        var manifestFirst = WriteFlatApp("manifest-first.app", NewAppId(1), "Manifest First");
        var flagFirst = WriteFlatApp("flag-first.app", NewAppId(2), "Flag First");

        // Manifest, then flag.
        Assert.Equal("Manifest First", AppLoader.ReadManifest(manifestFirst)!.Name);
        Assert.True(AppLoader.HasSymbolReference(manifestFirst));
        Assert.Equal(1, AppLoader.PackageOpenCountForTests(manifestFirst));

        // Flag, then manifest — the reverse order must be no worse, or a caller's incidental
        // question order decides whether the package is read twice.
        Assert.True(AppLoader.HasSymbolReference(flagFirst));
        Assert.Equal("Flag First", AppLoader.ReadManifest(flagFirst)!.Name);
        Assert.Equal(1, AppLoader.PackageOpenCountForTests(flagFirst));

        // Repeat asks add nothing at all.
        Assert.True(AppLoader.HasSymbolReference(manifestFirst));
        Assert.NotNull(AppLoader.ReadManifest(manifestFirst));
        Assert.Equal(1, AppLoader.PackageOpenCountForTests(manifestFirst));
    }

    /// <summary>
    /// The R2R shape, where the answers cost the most: the nested .app has to be buffered to
    /// reach either one. One open of the outer file, one buffering of the nested package —
    /// not one of each per question.
    /// </summary>
    [Fact]
    public void R2RPackage_BothQuestions_OpenTheOuterFileOnce()
    {
        var appId = NewAppId(3);
        var app = WriteR2RApp("r2r.app", appId, "Nested Identity",
            outerSymbolReference: false, nestedSymbolReference: true);

        Assert.True(AppLoader.HasSymbolReference(app));
        var manifest = AppLoader.ReadManifest(app);
        Assert.Equal("Nested Identity", manifest!.Name);
        Assert.Equal(appId, manifest.AppId);
        Assert.Equal(1, AppLoader.PackageOpenCountForTests(app));
    }

    // ── The warm on-disk index ──────────────────────────────────────────────────────────

    /// <summary>
    /// A fresh process (in-process memo cleared, on-disk index intact) answers BOTH questions
    /// without reading the package at all. Without the flag in the index entry, every
    /// al-runner invocation re-reads every package in the scan set to recover it.
    /// </summary>
    [Fact]
    public void SecondProcess_AnswersBothQuestions_WithoutReadingThePackage()
    {
        var appId = NewAppId(4);
        var app = WriteFlatApp("warm.app", appId, "Warm");

        Assert.Equal("Warm", AppLoader.ReadManifest(app)!.Name);
        Assert.True(AppLoader.HasSymbolReference(app));
        Assert.Equal(1, AppLoader.PackageOpenCountForTests(app));
        Assert.Equal(1, AppLoader.ManifestParseInvocationCountForTests(app));
        Assert.True(File.Exists(AppLoader.ManifestIndexPathForTests(app)));

        // Simulate the next al-runner invocation: nothing in memory, everything on disk.
        AppLoader.ResetManifestMemoForTests();

        Assert.True(AppLoader.HasSymbolReference(app));
        var manifest = AppLoader.ReadManifest(app);
        Assert.Equal("Warm", manifest!.Name);
        Assert.Equal(appId, manifest.AppId);
        // Neither a re-read nor a re-parse: the index entry carried both answers.
        Assert.Equal(1, AppLoader.PackageOpenCountForTests(app));
        Assert.Equal(1, AppLoader.ManifestParseInvocationCountForTests(app));
    }

    /// <summary>
    /// An index entry written before the flag existed (or by any writer that omits it) must be
    /// treated as UNKNOWN and recomputed. Defaulting the absent flag to `false` would make the
    /// scan drop a symbol-bearing package — AL1023 against the compilation, on a package
    /// nothing referenced — and it would do so only on machines with a warm cache from an older
    /// build, which is the worst possible failure mode to debug.
    /// </summary>
    [Fact]
    public void LegacyIndexEntry_WithoutTheFlag_IsRecomputed_NotReadAsFalse()
    {
        var appId = NewAppId(5);
        var app = WriteFlatApp("legacy.app", appId, "Legacy", withSymbolReference: true);

        // Hand-write the pre-flag payload shape at the exact key this file's stat maps to.
        var indexPath = AppLoader.ManifestIndexPathForTests(app);
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        File.WriteAllText(indexPath, JsonSerializer.Serialize(new
        {
            Publisher = "Contoso",
            Name = "Legacy",
            Version = "1.0.0.0",
            AppId = appId.ToString("D"),
            Dependencies = Array.Empty<object>(),
            Application = (string?)null,
            Platform = (string?)null,
        }));

        // The flag is recovered from the package, and it is TRUE — the value the entry could
        // not supply, not the default of its type.
        Assert.True(AppLoader.HasSymbolReference(app));
        Assert.Equal("Legacy", AppLoader.ReadManifest(app)!.Name);
        Assert.Equal(1, AppLoader.ManifestParseInvocationCountForTests(app));

        // And the entry is repaired, so the cost is paid once, not once per process forever.
        AppLoader.ResetManifestMemoForTests();
        Assert.True(AppLoader.HasSymbolReference(app));
        Assert.Equal(1, AppLoader.ManifestParseInvocationCountForTests(app));
    }

    /// <summary>
    /// The counterpart to the warm-index test: a package rewritten in place must be re-read.
    /// Both answers are keyed by path+length+mtime, so flipping the SymbolReference.json in a
    /// synthetic .app (InProcessAppPackager, a --watch rebuild) flips the answer.
    /// </summary>
    [Fact]
    public void PackageRewrittenInPlace_FlipsTheFlag_NeverServesTheStaleOne()
    {
        var appId = NewAppId(6);
        var app = WriteFlatApp("rewritten.app", appId, "V1", withSymbolReference: true);

        Assert.True(AppLoader.HasSymbolReference(app));
        Assert.Equal("V1", AppLoader.ReadManifest(app)!.Name);

        // Same path, genuinely different content, and an mtime pinned far enough away that the
        // filesystem's timestamp resolution cannot make this a no-op.
        WriteFlatApp("rewritten.app", appId, "V2", withSymbolReference: false);
        File.SetLastWriteTimeUtc(app, DateTime.UtcNow.AddDays(1));

        Assert.False(AppLoader.HasSymbolReference(app));
        Assert.Equal("V2", AppLoader.ReadManifest(app)!.Name);
        Assert.Equal(2, AppLoader.ManifestParseInvocationCountForTests(app));
    }

    // ── Equivalence across every package shape ──────────────────────────────────────────

    [Fact]
    public void FlatPackage_WithAndWithoutSymbolReference_AnswersBothWays()
    {
        var withSym = WriteFlatApp("flat-sym.app", NewAppId(10), "With Symbols");
        var withoutSym = WriteFlatApp("flat-nosym.app", NewAppId(11), "Without Symbols",
            withSymbolReference: false);

        Assert.True(AppLoader.HasSymbolReference(withSym));
        Assert.Equal("With Symbols", AppLoader.ReadManifest(withSym)!.Name);

        // The synthetic source-only .app shape — a manifest BC's scanner cannot serve. Reading
        // it as symbol-bearing is what AL1023 is made of.
        Assert.False(AppLoader.HasSymbolReference(withoutSym));
        Assert.Equal("Without Symbols", AppLoader.ReadManifest(withoutSym)!.Name);
        Assert.Equal(NewAppId(11), AppLoader.ReadManifest(withoutSym)!.AppId);
    }

    [Fact]
    public void R2RPackage_FlagInNestedApp_InOuterZip_OrNowhere_AllAnswerCorrectly()
    {
        var nestedOnly = WriteR2RApp("r2r-nested.app", NewAppId(12), "Nested Flag",
            outerSymbolReference: false, nestedSymbolReference: true);
        var outerOnly = WriteR2RApp("r2r-outer.app", NewAppId(13), "Outer Flag",
            outerSymbolReference: true, nestedSymbolReference: false);
        var neither = WriteR2RApp("r2r-none.app", NewAppId(14), "No Flag",
            outerSymbolReference: false, nestedSymbolReference: false);

        // The nested .app is the only thing carrying either answer in the first case — the
        // shape Microsoft ships System Application and Base Application in.
        Assert.True(AppLoader.HasSymbolReference(nestedOnly));
        Assert.Equal("Nested Flag", AppLoader.ReadManifest(nestedOnly)!.Name);

        Assert.True(AppLoader.HasSymbolReference(outerOnly));
        Assert.Equal("Outer Flag", AppLoader.ReadManifest(outerOnly)!.Name);

        Assert.False(AppLoader.HasSymbolReference(neither));
        Assert.Equal("No Flag", AppLoader.ReadManifest(neither)!.Name);
    }

    /// <summary>
    /// A manifest that will not parse must not cost the symbol-reference answer: the whole-file
    /// implementation never looked at the XML, so it never depended on it. Answering `false`
    /// here because the sibling question failed would drop a perfectly serveable package.
    /// </summary>
    [Fact]
    public void UnparseableManifestXml_StillAnswersTheSymbolReferenceQuestion()
    {
        var app = Path.Combine(_srcDir, "broken-manifest.app");
        File.WriteAllBytes(app, Navx(BuildZip(zip =>
        {
            AddEntry(zip, "NavxManifest.xml", "<<< this is not xml at all");
            AddEntry(zip, "SymbolReference.json", "{}");
        })));

        Assert.True(AppLoader.HasSymbolReference(app));
        Assert.Null(AppLoader.ReadManifest(app));
    }

    [Fact]
    public void MissingFile_NonZipGarbage_AndEmptyFile_AreNullAndFalse()
    {
        var missing = Path.Combine(_srcDir, "does-not-exist.app");
        var garbage = Path.Combine(_srcDir, "garbage.app");
        var empty = Path.Combine(_srcDir, "empty.app");
        File.WriteAllBytes(garbage, Encoding.UTF8.GetBytes("NAVX not really a zip after this"));
        File.WriteAllBytes(empty, Array.Empty<byte>());

        foreach (var path in new[] { missing, garbage, empty })
        {
            Assert.False(AppLoader.HasSymbolReference(path));
            Assert.Null(AppLoader.ReadManifest(path));
        }
    }

    /// <summary>
    /// A package whose zip payload does NOT start at byte 0 — the real .app shape, a NAVX
    /// header plus an offset. The streamed read hands ZipArchive an offset view of the file
    /// instead of a trimmed byte[], so a wrong offset misaligns every central-directory entry;
    /// this is the test that catches that.
    /// </summary>
    [Fact]
    public void PackageWithNonZeroNavxOffset_IsReadThroughTheOffsetView()
    {
        var app = Path.Combine(_srcDir, "offset.app");
        var zipBytes = BuildZip(zip =>
        {
            AddEntry(zip, "NavxManifest.xml", ManifestXml(NewAppId(20), "Offset", "Contoso", "1.0.0.0"));
            AddEntry(zip, "SymbolReference.json", "{}");
        });
        // 64 bytes of padding between the header and the zip, addressed by the NAVX offset —
        // the same indirection real packages use, exaggerated so an off-by-anything fails.
        const int offset = 64;
        var file = new byte[offset + zipBytes.Length];
        Encoding.ASCII.GetBytes("NAVX").CopyTo(file, 0);
        BitConverter.TryWriteBytes(file.AsSpan(4, 4), (uint)offset);
        zipBytes.CopyTo(file, offset);
        File.WriteAllBytes(app, file);

        Assert.True(AppLoader.HasSymbolReference(app));
        Assert.Equal("Offset", AppLoader.ReadManifest(app)!.Name);
        Assert.Equal(1, AppLoader.PackageOpenCountForTests(app));
    }

    // ── Streamed, not slurped ───────────────────────────────────────────────────────────

    /// <summary>
    /// Answering both questions must cost the same whether the package is 100KB or 100MB, as
    /// long as the bulk is in entries neither question reads. Stated DIFFERENTIALLY — two
    /// packages identical but for an unread, incompressible 32MB payload, and the allocation
    /// difference between them — because an absolute ceiling would be pinning ZipArchive's
    /// per-entry overhead and the size of whatever fixture happened to be here.
    ///
    /// <para>This is the claim the whole-file read violated: <c>File.ReadAllBytes</c> allocates
    /// the padding whether or not anything wants it, so the difference would be ≥32MB. Both
    /// shapes are covered, because the padding lands in a different place in each: a flat
    /// package (padding beside the manifest) and the R2R shape (padding in the OUTER zip,
    /// alongside a nested .app that genuinely must be buffered).</para>
    /// </summary>
    [Fact]
    public void PackageSize_DoesNotChangeWhatItCostsToAnswerBothQuestions()
    {
        const int padding = 32 * 1024 * 1024;
        const long tolerance = 1024 * 1024;   // room for entry bookkeeping, not for a 32MB copy

        var lean = WriteFlatApp("lean.app", NewAppId(30), "Lean");
        var padded = WriteFlatApp("padded.app", NewAppId(31), "Padded", paddingBytes: padding);
        var leanR2R = WriteR2RApp("lean-r2r.app", NewAppId(32), "Lean R2R",
            outerSymbolReference: false, nestedSymbolReference: true);
        var paddedR2R = WriteR2RApp("padded-r2r.app", NewAppId(33), "Padded R2R",
            outerSymbolReference: false, nestedSymbolReference: true, paddingBytes: padding);

        // The padding really is on disk and really is incompressible — otherwise the whole
        // comparison is between two files of the same size and proves nothing.
        Assert.True(new FileInfo(padded).Length - new FileInfo(lean).Length > padding * 0.9,
            "the padded fixture must actually be ~32MB larger on disk");
        Assert.True(new FileInfo(paddedR2R).Length - new FileInfo(leanR2R).Length > padding * 0.9,
            "the padded R2R fixture must actually be ~32MB larger on disk");

        var flatCost = CostOfBothQuestions(padded) - CostOfBothQuestions(lean);
        var r2rCost = CostOfBothQuestions(paddedR2R) - CostOfBothQuestions(leanR2R);

        Assert.True(Math.Abs(flatCost) < tolerance,
            $"32MB of unread payload changed the cost of reading a flat package's metadata by " +
            $"{flatCost / 1024}KB — the package is being read whole, not streamed");
        Assert.True(Math.Abs(r2rCost) < tolerance,
            $"32MB of unread payload changed the cost of reading an R2R package's metadata by " +
            $"{r2rCost / 1024}KB — the outer package is being read whole, not streamed");
    }

    /// <summary>Bytes allocated to answer both questions about one package, from cold (the memo
    /// and the on-disk index are cleared first, or the second call measures a dictionary
    /// lookup). Measured on this thread only, so a background GC or another test's work cannot
    /// contribute.</summary>
    private long CostOfBothQuestions(string appPath)
    {
        AppLoader.ResetManifestMemoForTests();
        var indexPath = AppLoader.ManifestIndexPathForTests(appPath);
        try { File.Delete(indexPath); } catch { /* nothing to clear */ }

        var before = GC.GetAllocatedBytesForCurrentThread();
        AppLoader.ReadManifest(appPath);
        AppLoader.HasSymbolReference(appPath);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // ── Against the real Microsoft packages ─────────────────────────────────────────────

    /// <summary>
    /// The synthetic fixtures above are kilobytes. The packages this code exists for are not:
    /// `Microsoft_Base Application` is ~93MB of R2R published artifacts wrapped around a nested
    /// AL .app, with thousands of zip entries — and it is the file whose whole-file read forced
    /// the scan's concurrency cap. So this runs the real thing, from the platform-apps dir CI
    /// provisions: every real Microsoft package must answer both questions, correctly.
    ///
    /// <para>The one-open claim is asserted against a PRIVATE COPY of a real package, not
    /// against platform-apps itself. The open counter is process-wide and cumulative, and
    /// platform-apps is shared fixture data that DependencyResolver / ProvisioningCheck /
    /// CacheKey suites read from the same test process — so an absolute count on those paths
    /// measures which suites happened to run first, and passes only in isolation. A copy is a
    /// path nothing else touches.</para>
    ///
    /// <para>Size independence is pinned separately and differentially, in
    /// PackageSize_DoesNotChangeWhatItCostsToAnswerBothQuestions — an absolute allocation
    /// ceiling here would be measuring the nested .app these packages carry, not the slurp.</para>
    /// </summary>
    [SkippableFact]
    public void RealMicrosoftPackages_AnswerBothQuestions_OffOneStreamedOpenEach()
    {
        var platformApps = TestArtifacts.PlatformAppsDir();
        TestArtifacts.SkipIf(!Directory.Exists(platformApps),
            $"platform-apps not provisioned under '{platformApps}'.");
        var packages = Directory.EnumerateFiles(platformApps, "*.app", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        TestArtifacts.SkipIf(packages.Count == 0, $"no .app packages under '{platformApps}'.");

        foreach (var package in packages)
        {
            var manifest = AppLoader.ReadManifest(package);
            var hasSymbolReference = AppLoader.HasSymbolReference(package);
            var name = Path.GetFileName(package);
            Assert.True(manifest != null, $"{name}: no identity read from a real BC package");
            Assert.NotEqual(Guid.Empty, manifest!.AppId);
            // Every package Microsoft ships in platform-apps is compiler-valid; if this reads
            // false the scan drops it and every compile that scans the dir fails with AL1023.
            Assert.True(hasSymbolReference, $"{name}: real BC package read as carrying no SymbolReference.json");
        }

        // The R2R shape is the one where both answers live behind a nested .app, so it is the one
        // worth counting. Smallest such package present, to keep the copy cheap.
        var r2r = packages.Where(AppLoader.IsR2R).OrderBy(p => new FileInfo(p).Length).FirstOrDefault();
        TestArtifacts.SkipIf(r2r == null, $"no R2R package under '{platformApps}' to count reads on.");
        var privateCopy = Path.Combine(_srcDir, "real-r2r.app");
        File.Copy(r2r!, privateCopy);

        var copiedManifest = AppLoader.ReadManifest(privateCopy);
        var copiedFlag = AppLoader.HasSymbolReference(privateCopy);
        Assert.NotNull(copiedManifest);
        Assert.True(copiedFlag);
        Assert.Equal(1, AppLoader.PackageOpenCountForTests(privateCopy));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    private static string NewTempDir(string suffix)
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "app-loader-package-meta-tests-" + suffix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Guid NewAppId(int seed) => new($"{seed:x8}-0000-0000-0000-000000000000");

    private string WriteFlatApp(string fileName, Guid appId, string name,
        bool withSymbolReference = true, int paddingBytes = 0)
    {
        var path = Path.Combine(_srcDir, fileName);
        File.WriteAllBytes(path, Navx(BuildZip(zip =>
        {
            AddEntry(zip, "NavxManifest.xml", ManifestXml(appId, name, "Contoso", "1.0.0.0"));
            if (withSymbolReference) AddEntry(zip, "SymbolReference.json", "{}");
            AddEntry(zip, "src/Filler.al", Encoding.UTF8.GetBytes(new string('x', 64 * 1024)));
            if (paddingBytes > 0)
                AddEntry(zip, "publishedartifacts/net8.0/Padding.dll", Incompressible(paddingBytes));
        })));
        return path;
    }

    /// <summary>The Microsoft R2R shape: the outer zip carries a published artifact and a
    /// nested AL .app, and the nested package is where the manifest lives.</summary>
    private string WriteR2RApp(string fileName, Guid appId, string name,
        bool outerSymbolReference, bool nestedSymbolReference, int paddingBytes = 0)
    {
        var nested = Navx(BuildZip(zip =>
        {
            AddEntry(zip, "NavxManifest.xml", ManifestXml(appId, name, "Microsoft", "28.1.0.0"));
            if (nestedSymbolReference) AddEntry(zip, "SymbolReference.json", "{}");
        }));

        var path = Path.Combine(_srcDir, fileName);
        File.WriteAllBytes(path, Navx(BuildZip(zip =>
        {
            AddEntry(zip, "readytorunappmanifest.json", "{}");
            AddEntry(zip, "publishedartifacts/net8.0/DEADBEEF.dll",
                Encoding.UTF8.GetBytes(new string('d', 64 * 1024)));
            if (paddingBytes > 0)
                AddEntry(zip, "publishedartifacts/net8.0/Padding.dll", Incompressible(paddingBytes));
            if (outerSymbolReference) AddEntry(zip, "SymbolReference.json", "{}");
            AddEntry(zip, name + ".app", nested);
        })));
        return path;
    }

    /// <summary>Pseudo-random bytes from a fixed seed: deflate cannot shrink them, so the
    /// fixture's on-disk size really does grow by what the caller asked for, and the run stays
    /// deterministic.</summary>
    private static byte[] Incompressible(int count)
    {
        var bytes = new byte[count];
        new Random(20260819).NextBytes(bytes);
        return bytes;
    }

    private static string ManifestXml(Guid appId, string name, string publisher, string version) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/navx/2015/manifest">
          <App Id="{appId}" Name="{name}" Publisher="{publisher}" Version="{version}"/>
        </Package>
        """;

    private static byte[] BuildZip(Action<ZipArchive> build)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            build(zip);
        return ms.ToArray();
    }

    private static void AddEntry(ZipArchive zip, string entryName, string content)
        => AddEntry(zip, entryName, Encoding.UTF8.GetBytes(content));

    private static void AddEntry(ZipArchive zip, string entryName, byte[] content)
    {
        var entry = zip.CreateEntry(entryName);
        using var es = entry.Open();
        es.Write(content);
    }

    /// <summary>NAVX wrapper: magic + LE uint32 offset of the zip payload (8, immediately
    /// after the header).</summary>
    private static byte[] Navx(byte[] zipBytes)
    {
        var result = new byte[8 + zipBytes.Length];
        Encoding.ASCII.GetBytes("NAVX").CopyTo(result, 0);
        BitConverter.TryWriteBytes(result.AsSpan(4, 4), (uint)8);
        zipBytes.CopyTo(result, 8);
        return result;
    }
}
