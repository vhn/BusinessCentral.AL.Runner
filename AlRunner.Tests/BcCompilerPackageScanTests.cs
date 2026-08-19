// BcCompilerPackageScanTests — the .app metadata scan reads packages concurrently, and the
// dedup decisions it feeds still see the exact sequence they always did.
//
// What the scan is
// ----------------
// BcCompiler.DeduplicateAppPackageDirs walks every --package-cache dir, and for each `.app`
// performs TWO full zip reads (AppLoader.ReadManifest + AppLoader.HasSymbolReference) before it
// can decide whether to keep the package. GetSharedReferences has to run the whole scan on
// every call — its output is what the reference loader's signature is computed from — and the
// scan's own comment sizes it at ~1–2.5 s for a 113-package, 138 MB platform-apps dir.
//
// The reads are independent. The DECISIONS are not: `seen` keeps the FIRST occurrence of an
// (AppId, Version) in scan order and drops later ones, the excluded AppId is dropped wherever
// it appears, symbol-less packages are dropped, and `inventory` is handed downstream in scan
// order. So the read fans out and the merge stays serial — and the risk the fan-out introduces
// is precisely that the merge stops seeing the order those rules are written against.
//
// What these tests pin
// --------------------
//  * MECHANISM (the RED one) — two package reads are in flight at the same moment. Stated as
//    an overlap, never as a duration: a "the scan finished in under N ms" assertion measures
//    the CI box, not the code. A serial scan cannot make two reads overlap however fast the
//    machine is, so this is RED before the change and GREEN after, deterministically.
//  * ORDER (the regression net) — the picked list and the inventory come back in exactly the
//    sequence a serial scan produces, across a fixture that exercises every branch of the merge
//    at once: a cross-dir duplicate, the same AppId at two different versions, a symbol-less
//    package, a dir that does not exist, and an excluded AppId. This is the test that fails if
//    the fan-out ever leaks into the decisions.
//  * NO-CHANGE FAST PATH — a scan that finds nothing to drop still returns the caller's own
//    list instance, so the staging branch stays off the hot path.
//
// Pure filesystem/zip logic, invoked by reflection exactly like BcCompilerPkgDedupRelativePath-
// Tests and BcCompilerLoaderSelfExclusionTests. Joins BcCompilerSharedReferenceCollection
// because it resets the shared reference/scan caches.
using System.IO.Compression;
using System.Reflection;
using System.Text;
using AlRunner;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcCompilerSharedReferenceCollection.Name)]
public sealed class BcCompilerPackageScanTests : IDisposable
{
    private readonly string _root;

    public BcCompilerPackageScanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "al-runner-pkgscan-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        BcCompiler.ResetSharedReferencesForTests();
    }

    public void Dispose()
    {
        BcCompiler.PackageScanProbeForTests = null;
        BcCompiler.ResetSharedReferencesForTests();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    // ── Mechanism: the reads overlap ────────────────────────────────────────────────────

    [SkippableFact]
    public void PackageMetadata_ForDifferentPackages_IsReadConcurrently()
    {
        TestArtifacts.SkipIf(Environment.ProcessorCount < 2,
            "a single-core host has no concurrency to observe.");

        var dir = Path.Combine(_root, "many");
        Directory.CreateDirectory(dir);
        // Enough packages that the fan-out has real work to partition; each is a few hundred
        // bytes, so the fixture costs nothing to build or read.
        for (int i = 0; i < 64; i++)
            WriteApp(dir, $"Pkg{i}.app", NewAppId(i), $"Package {i}", "Contoso", "1.0.0.0");

        // The FIRST read into the probe parks until a SECOND one arrives. Only that first read
        // decides the verdict, and only from its own wait: under a serial scan it waits alone
        // until the cap elapses, returns false, and the second read then finds nobody parked —
        // so `overlapped` stays false however quickly the rest of the scan runs. Under a
        // concurrent scan a second worker arrives while the first is still parked and the wait
        // returns true. Nothing here asserts a duration; the cap exists only so a serial run
        // ends instead of hanging.
        using var secondArrived = new ManualResetEventSlim(false);
        var arrivals = 0;
        var overlapped = false;
        BcCompiler.PackageScanProbeForTests = () =>
        {
            var n = Interlocked.Increment(ref arrivals);
            if (n == 1) overlapped = secondArrived.Wait(TimeSpan.FromSeconds(30));
            else if (n == 2) secondArrived.Set();
        };

        InvokeDeduplicate(new List<string> { dir }, excludeAppId: null, out var inventory);

        Assert.True(overlapped,
            "no two package reads were ever in flight at the same time — the metadata scan is " +
            "still serial. Each .app costs two full zip reads (ReadManifest + HasSymbolReference) " +
            "and GetSharedReferences reruns the whole scan on every call.");
        Assert.Equal(64, inventory.Count);
    }

    // ── Order: the merge still sees the sequence its rules are written against ──────────

    [Fact]
    public void ScanOrder_AndEveryDropRule_AreUnchangedByTheConcurrentRead()
    {
        var dirA = Path.Combine(_root, "a");
        var dirB = Path.Combine(_root, "b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        var shared = NewAppId(1);
        var twoVersions = NewAppId(2);
        var symbolLess = NewAppId(3);
        var excluded = NewAppId(4);

        // dirA: the first occurrence of `shared` (must win), both versions of `twoVersions`
        // (both must survive — collapsing by AppId alone drops a version-pinned reference),
        // a package with no SymbolReference.json (must be dropped), and the excluded AppId.
        WriteApp(dirA, "Shared.app", shared, "Shared", "Microsoft", "28.1.0.0");
        WriteApp(dirA, "TwoV1.app", twoVersions, "Two Versions", "Contoso", "1.0.0.0");
        WriteApp(dirA, "TwoV2.app", twoVersions, "Two Versions", "Contoso", "2.0.0.0");
        WriteApp(dirA, "NoSymbols.app", symbolLess, "No Symbols", "Contoso", "1.0.0.0",
            withSymbolReference: false);
        WriteApp(dirA, "Excluded.app", excluded, "Excluded", "Contoso", "1.0.0.0");
        // dirB: the SECOND occurrence of `shared` (must lose to dirA's), plus one unique app.
        WriteApp(dirB, "Shared.app", shared, "Shared", "Microsoft", "28.1.0.0");
        WriteApp(dirB, "OnlyInB.app", NewAppId(5), "Only In B", "Contoso", "1.0.0.0");

        var dirs = new List<string>
        {
            dirA,
            Path.Combine(_root, "does-not-exist"),   // must contribute nothing, not throw
            dirB,
        };

        InvokeDeduplicate(dirs, excluded, out var inventory);
        var actual = inventory.Cast<object>().ToList();

        // Differential: the serial algorithm the scan replaced, written out here and run over
        // the SAME enumeration. Comparing against a hardcoded list instead would pin the
        // filesystem's within-directory ordering, which no platform guarantees — this compares
        // the two algorithms, which is the actual claim.
        Assert.Equal(
            SerialReference(dirs, excluded),
            actual.Select(e => (PathOf(e), AppIdOf(e), VersionOf(e).ToString())).ToList());

        // Independently of ordering, every drop rule must still have fired — so the test still
        // means something if both algorithms were wrong in the same way.
        Assert.Equal(4, actual.Count);
        Assert.DoesNotContain(actual, e => AppIdOf(e) == symbolLess);   // no SymbolReference.json
        Assert.DoesNotContain(actual, e => AppIdOf(e) == excluded);     // excludeAppId
        // The cross-dir duplicate collapses to ONE entry, and it is dirA's — first occurrence
        // in scan order wins.
        var sharedEntries = actual.Where(e => AppIdOf(e) == shared).ToList();
        Assert.Single(sharedEntries);
        Assert.Equal(Path.GetFullPath(Path.Combine(dirA, "Shared.app")), PathOf(sharedEntries[0]));
        // Both versions of one AppId survive: collapsing by AppId alone would drop the version
        // a version-pinned reference needs and produce AL1022.
        Assert.Equal(new[] { "1.0.0.0", "2.0.0.0" },
            actual.Where(e => AppIdOf(e) == twoVersions).Select(e => VersionOf(e).ToString())
                  .OrderBy(v => v, StringComparer.Ordinal).ToArray());
        // Names and publishers come off the manifest each entry was actually read from — rules
        // out an implementation that got the order right by pairing the wrong metadata with the
        // right path, which is exactly what a per-index array can get wrong.
        foreach (var e in actual)
        {
            var expectedName = Path.GetFileNameWithoutExtension(PathOf(e)) switch
            {
                "Shared" => "Shared",
                "TwoV1" or "TwoV2" => "Two Versions",
                "OnlyInB" => "Only In B",
                var other => throw new Xunit.Sdk.XunitException($"unexpected package '{other}' survived"),
            };
            Assert.Equal(expectedName, NameOf(e));
            Assert.Equal(NameOf(e) == "Shared" ? "Microsoft" : "Contoso", PublisherOf(e));
        }
    }

    /// <summary>The scan as it was before the read fanned out: enumerate serially, read each
    /// package's manifest and symbol-reference flag in that order, apply the drop rules in that
    /// order. The specification the concurrent version has to reproduce exactly.</summary>
    private static List<(string Path, Guid AppId, string Version)> SerialReference(
        List<string> packageDirs, Guid? excludeAppId)
    {
        var seen = new HashSet<(Guid, string)>();
        var result = new List<(string, Guid, string)>();
        foreach (var dir in packageDirs)
        {
            List<FileInfo> apps;
            try { apps = new DirectoryInfo(dir).EnumerateFiles("*.app", SearchOption.AllDirectories).ToList(); }
            catch { continue; }
            foreach (var fi in apps)
            {
                var m = AppLoader.ReadManifest(fi.FullName);
                if (m == null) continue;
                if (excludeAppId != null && m.AppId == excludeAppId.Value) continue;
                if (!seen.Add((m.AppId, m.Version.ToString()))) continue;
                if (!AppLoader.HasSymbolReference(fi.FullName)) continue;
                result.Add((Path.GetFullPath(fi.FullName), m.AppId, m.Version.ToString()));
            }
        }
        return result;
    }

    // ── Cost: one read per package, not one per question ────────────────────────────────

    /// <summary>
    /// Every candidate package is read ONCE per scan, however many facts the scan needs from
    /// it. The scan asks two questions about each .app (identity, and whether BC's native
    /// scanner can serve it) and used to pay a separate full read for each — 226 reads over the
    /// 113-package platform-apps dir, on every GetSharedReferences call.
    ///
    /// <para>An open COUNT rather than a duration: the fan-out means the reads no longer happen
    /// in a fixed order or on a fixed thread, so wall-clock says nothing reliable, while
    /// "how many times was this file opened" is exact on any machine under any load.</para>
    /// </summary>
    [Fact]
    public void EachPackage_IsReadOncePerScan_NotOncePerQuestion()
    {
        var dir = Path.Combine(_root, "cost");
        Directory.CreateDirectory(dir);
        var paths = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            // Half without a SymbolReference.json: that is the branch which, in the old
            // implementation, still cost a whole-file read to discover.
            WriteApp(dir, $"Cost{i}.app", NewAppId(100 + i), $"Cost {i}", "Contoso", "1.0.0.0",
                withSymbolReference: i % 2 == 0);
            paths.Add(Path.Combine(dir, $"Cost{i}.app"));
        }

        InvokeDeduplicate(new List<string> { dir }, excludeAppId: null, out var inventory);

        Assert.Equal(4, inventory.Count);   // the symbol-less half is dropped, as always
        foreach (var path in paths)
            Assert.Equal(1, AppLoader.PackageOpenCountForTests(path));
    }

    [Fact]
    public void NothingToDrop_ReturnsTheCallersOwnList_Unstaged()
    {
        var dir = Path.Combine(_root, "clean");
        Directory.CreateDirectory(dir);
        WriteApp(dir, "One.app", NewAppId(10), "One", "Contoso", "1.0.0.0");
        WriteApp(dir, "Two.app", NewAppId(11), "Two", "Contoso", "1.0.0.0");

        var dirs = new List<string> { dir };
        var result = InvokeDeduplicate(dirs, excludeAppId: null, out var inventory);

        // Same instance: no duplicate, no exclusion, no symbol-less package means no staging
        // dir is built and the loader scans exactly what the caller passed.
        Assert.Same(dirs, result);
        Assert.Equal(2, inventory.Count);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────

    private static Guid NewAppId(int seed) => new($"{seed:x8}-0000-0000-0000-000000000000");

    private static readonly MethodInfo DeduplicateWithInventory = typeof(BcCompiler)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .SingleOrDefault(m => m.Name == "DeduplicateAppPackageDirs" && m.GetParameters().Length == 3)
        ?? throw new InvalidOperationException(
            "BcCompiler.DeduplicateAppPackageDirs(dirs, excludeAppId, out inventory) not found — signature changed.");

    private static List<string> InvokeDeduplicate(
        List<string> packageDirs, Guid? excludeAppId, out System.Collections.IList inventory)
    {
        var args = new object?[] { packageDirs, excludeAppId, null };
        var result = (List<string>)DeduplicateWithInventory.Invoke(null, args)!;
        inventory = (System.Collections.IList)args[2]!;
        return result;
    }

    // PackageScanEntry is a private-nested-visibility record struct on BcCompiler; read its
    // fields by name rather than referencing the type, so this test does not need it public.
    private static string PathOf(object e) => (string)e.GetType().GetProperty("Path")!.GetValue(e)!;
    private static Guid AppIdOf(object e) => (Guid)e.GetType().GetProperty("AppId")!.GetValue(e)!;
    private static string NameOf(object e) => (string)e.GetType().GetProperty("Name")!.GetValue(e)!;
    private static string PublisherOf(object e) => (string)e.GetType().GetProperty("Publisher")!.GetValue(e)!;
    private static Version VersionOf(object e) => (Version)e.GetType().GetProperty("Version")!.GetValue(e)!;

    private static void WriteApp(string dir, string fileName, Guid appId, string name,
        string publisher, string version, bool withSymbolReference = true)
    {
        File.WriteAllBytes(Path.Combine(dir, fileName),
            MakeMinimalApp(appId, name, publisher, version, withSymbolReference));
    }

    private static byte[] MakeMinimalApp(Guid appId, string name, string publisher,
        string version, bool withSymbolReference)
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/navx/2015/manifest">
              <App Id="{appId}" Name="{name}" Publisher="{publisher}" Version="{version}"/>
            </Package>
            """;

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("NavxManifest.xml");
            using (var es = entry.Open())
                es.Write(Encoding.UTF8.GetBytes(xml));

            if (withSymbolReference)
            {
                var symEntry = zip.CreateEntry("SymbolReference.json");
                using (var symStream = symEntry.Open())
                    symStream.Write(Encoding.UTF8.GetBytes("{}"));
            }
        }
        var zipBytes = ms.ToArray();

        var result = new byte[8 + zipBytes.Length];
        result[0] = (byte)'N'; result[1] = (byte)'A'; result[2] = (byte)'V'; result[3] = (byte)'X';
        BitConverter.TryWriteBytes(result.AsSpan(4, 4), (uint)8);
        zipBytes.CopyTo(result, 8);
        return result;
    }
}
