using System;
using System.IO;
using System.Linq;
using AlRunner.Infrastructure;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The layered pre-pass prefers a real, alc-built prebuilt .app over in-process symbol
/// synthesis, because BC's native .app scanner merges tableextensions correctly where our
/// synthetic symbols.json does not. That preference is right — but it used to be
/// unconditional, matched on AppId alone with no staleness check. A months-old .app sitting
/// in a project's .alpackages therefore beat the source directory the user passed on the
/// command line, and the failure surfaced as a wall of misleading AL0791/AL0185 diagnostics
/// against source that is perfectly valid (observed on Pageworks: 136 bogus errors).
///
/// The first staleness check compared MTIMES: keep the prebuilt unless some .al is newer.
/// That comparator answers the wrong question. Git writes mtimes at checkout, not at edit,
/// so mtime ordering tracks "which file was touched last on this machine", never "which
/// bytes are current" — and it is wrong in BOTH directions:
///
///   • False fresh. `Test/.alpackages/NaviPartner_NP_Retail_9999.9999.9999.9999.app` was
///     newer than every .al in the sibling Application bundle, so the prebuilt won and the
///     run compiled a stale package instead of the working tree — dropping 322
///     Pages/ControlAddins through `EMIT-FAIL … excluding and retrying`. The developer is
///     silently testing bytes they did not write.
///   • False stale. A fresh clone, a worktree switch, a CI checkout or a Conductor
///     workspace rewrites every source mtime to "now", so a perfectly current package
///     reads as stale and costs a full needless compile.
///
/// So the verdict now comes from CONTENT: hash the AL text the package ships under src/*.al,
/// hash the AL text in the bundle, compare. Mtime survives only as the fallback for packages
/// that ship no AL source at all, where there is nothing to compare.
///
/// These tests pin both halves — the content verdict where content exists, and the mtime
/// fallback where it does not.
/// </summary>
public class PrebuiltShadowCheckTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "al-runner-shadow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// A bundle dir with an app.json and <paramref name="sources"/> written as .al files,
    /// plus a real NAVX .app emitted from it by the production packager — i.e. exactly the
    /// "package built from this source" shape the check has to recognise.
    /// </summary>
    private static (string BundleDir, string AppPath) NewBundleWithPackage(
        params (string FileName, string Text)[] sources)
    {
        var dir = NewTempDir();
        var appId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(dir, "app.json"),
            $"{{\"id\":\"{appId}\",\"name\":\"Shadow\",\"publisher\":\"Test\",\"version\":\"1.0.0.0\"}}");
        foreach (var (fileName, text) in sources)
        {
            var path = Path.Combine(dir, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        var identity = InProcessAppPackager.ReadIdentity(Path.Combine(dir, "app.json"))!;
        var appPath = Path.Combine(NewTempDir(), "Test_Shadow_1_0_0_0.app");
        InProcessAppPackager.EmitAppPackageToFile(dir, identity, appPath);
        return (dir, appPath);
    }

    // ── The content verdict ───────────────────────────────────────────────────

    /// <summary>
    /// The false-stale direction. A checkout rewrites every source mtime to "now", leaving
    /// the source newer than a package that is nonetheless built from exactly these bytes.
    /// Content is identical, so the prebuilt is current and must keep winning.
    /// </summary>
    [Fact]
    public void PrebuiltContentMatchesSource_IsNotStale_EvenWhenEveryAlFileIsNewer()
    {
        var (bundleDir, appPath) = NewBundleWithPackage(
            ("Foo.Codeunit.al", "codeunit 50100 Foo { }"),
            (Path.Combine("src", "Bar.Codeunit.al"), "codeunit 50101 Bar { }"));
        try
        {
            // Simulate the checkout: every .al is now an hour newer than the package.
            var future = File.GetLastWriteTimeUtc(appPath).AddHours(1);
            foreach (var al in Directory.EnumerateFiles(bundleDir, "*.al", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(al, future);

            var verdict = PrebuiltShadowCheck.Evaluate(appPath, bundleDir);

            Assert.False(verdict.Stale,
                $"content is byte-identical, so the package is current. Reason given: {verdict.Reason}");
            Assert.Contains("content", verdict.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(bundleDir, appPath); }
    }

    /// <summary>
    /// The false-fresh direction — the npcore defect. The package is newer than every .al,
    /// but its src/*.al is not what the bundle holds, so compiling it tests the wrong bytes.
    /// </summary>
    [Fact]
    public void PrebuiltContentDiffersFromSource_IsStale_EvenWhenThePackageIsNewer()
    {
        var (bundleDir, appPath) = NewBundleWithPackage(
            ("Foo.Codeunit.al", "codeunit 50100 Foo { }"));
        try
        {
            // Edit the source, then backdate it so the package is unambiguously "newer".
            var al = Path.Combine(bundleDir, "Foo.Codeunit.al");
            File.WriteAllText(al, "codeunit 50100 Foo { procedure Added() begin end; }");
            var past = File.GetLastWriteTimeUtc(appPath).AddHours(-1);
            File.SetLastWriteTimeUtc(al, past);

            var verdict = PrebuiltShadowCheck.Evaluate(appPath, bundleDir);

            Assert.True(verdict.Stale,
                $"the package no longer carries this source, so it must not shadow it. Reason given: {verdict.Reason}");
            Assert.Contains("content", verdict.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(bundleDir, appPath); }
    }

    /// <summary>
    /// An added .al file is a content change even though every pre-existing file still
    /// matches — the package is missing an object the bundle declares. Backdated so the
    /// mtime comparator would say "not stale": the addition must be caught on content.
    /// </summary>
    [Fact]
    public void SourceGainsAnAlFile_IsStale_EvenWhenTheAdditionIsBackdated()
    {
        var (bundleDir, appPath) = NewBundleWithPackage(
            ("Foo.Codeunit.al", "codeunit 50100 Foo { }"));
        try
        {
            var added = Path.Combine(bundleDir, "Baz.Codeunit.al");
            File.WriteAllText(added, "codeunit 50102 Baz { }");
            var past = File.GetLastWriteTimeUtc(appPath).AddHours(-1);
            foreach (var al in Directory.EnumerateFiles(bundleDir, "*.al", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(al, past);

            Assert.False(PrebuiltShadowCheck.SourceIsNewer(
                File.GetLastWriteTimeUtc(appPath), PrebuiltShadowCheck.NewestAlSourceUtc(bundleDir)));
            Assert.True(PrebuiltShadowCheck.Evaluate(appPath, bundleDir).Stale);
        }
        finally { Cleanup(bundleDir, appPath); }
    }

    // ── The content hashes themselves ─────────────────────────────────────────

    /// <summary>
    /// The package flattens sources to <c>src/&lt;filename&gt;.al</c> while the bundle keeps
    /// its directory layout, so the comparison cannot key on paths. AL object identity lives
    /// in the source text (<c>codeunit 50100 "Foo"</c>), never in the filename, so hashing
    /// the multiset of file texts is the right granularity — and it must not vary with
    /// layout or enumeration order.
    /// </summary>
    [Fact]
    public void SourceAlContentHash_IsIndependentOfLayoutAndFileNames()
    {
        var flat = NewTempDir();
        var nested = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(flat, "A.al"), "codeunit 1 A { }");
            File.WriteAllText(Path.Combine(flat, "B.al"), "codeunit 2 B { }");

            var sub = Path.Combine(nested, "src", "Deep");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "renamed-second.al"), "codeunit 2 B { }");
            File.WriteAllText(Path.Combine(nested, "renamed-first.al"), "codeunit 1 A { }");

            Assert.Equal(PrebuiltShadowCheck.SourceAlContentHash(flat),
                         PrebuiltShadowCheck.SourceAlContentHash(nested));
        }
        finally { Cleanup(flat, nested); }
    }

    /// <summary>
    /// Line endings and a BOM are rewritten by git's autocrlf and by editors without any
    /// AL change, and the compiler's output is identical either way. Treating them as a
    /// change would reintroduce the false-stale compile this fix exists to remove.
    /// </summary>
    [Fact]
    public void SourceAlContentHash_IgnoresLineEndingsAndBom()
    {
        var lf = NewTempDir();
        var crlfBom = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(lf, "A.al"), "codeunit 1 A\n{\n}\n");
            File.WriteAllBytes(Path.Combine(crlfBom, "A.al"),
                new byte[] { 0xEF, 0xBB, 0xBF }
                    .Concat(System.Text.Encoding.UTF8.GetBytes("codeunit 1 A\r\n{\r\n}\r\n"))
                    .ToArray());

            Assert.Equal(PrebuiltShadowCheck.SourceAlContentHash(lf),
                         PrebuiltShadowCheck.SourceAlContentHash(crlfBom));
        }
        finally { Cleanup(lf, crlfBom); }
    }

    /// <summary>NEGATIVE — a real AL edit must change the hash, or the check proves nothing.</summary>
    [Fact]
    public void SourceAlContentHash_ChangesWhenTheAlChanges()
    {
        var dir = NewTempDir();
        try
        {
            var al = Path.Combine(dir, "A.al");
            File.WriteAllText(al, "codeunit 1 A { }");
            var before = PrebuiltShadowCheck.SourceAlContentHash(dir);

            File.WriteAllText(al, "codeunit 1 A { procedure P() begin end; }");

            Assert.NotNull(before);
            Assert.NotEqual(before, PrebuiltShadowCheck.SourceAlContentHash(dir));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// Non-.al files are build artifacts, logs and editor swapfiles. Same rule as
    /// <see cref="NewestAlSourceUtc_IgnoresNonAlFiles"/>: they must not read as a change.
    /// </summary>
    [Fact]
    public void SourceAlContentHash_IgnoresNonAlFiles()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "A.al"), "codeunit 1 A { }");
            var before = PrebuiltShadowCheck.SourceAlContentHash(dir);

            File.WriteAllText(Path.Combine(dir, "notes.txt"), "hello");
            File.WriteAllText(Path.Combine(dir, "app.json"), "{}");

            Assert.Equal(before, PrebuiltShadowCheck.SourceAlContentHash(dir));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>No AL source is not "empty content" — it is "nothing to compare", i.e. null.</summary>
    [Fact]
    public void SourceAlContentHash_IsNullWhenThereIsNoAlSource()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "hello");
            Assert.Null(PrebuiltShadowCheck.SourceAlContentHash(dir));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SourceAlContentHash_IsNullForAMissingDirectory()
    {
        Assert.Null(PrebuiltShadowCheck.SourceAlContentHash(
            Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N"))));
    }

    /// <summary>
    /// The two hashes are only comparable if they are computed the same way over the same
    /// text — this pins that the package side reads the AL out of the NAVX zip and agrees
    /// with the bundle side for a package built from that bundle.
    /// </summary>
    [Fact]
    public void PrebuiltAlContentHash_MatchesTheBundleItWasPackagedFrom()
    {
        var (bundleDir, appPath) = NewBundleWithPackage(
            ("Foo.Codeunit.al", "codeunit 50100 Foo { }"),
            (Path.Combine("nested", "Bar.Codeunit.al"), "codeunit 50101 Bar { }"));
        try
        {
            var packaged = PrebuiltShadowCheck.PrebuiltAlContentHash(appPath);

            Assert.NotNull(packaged);
            Assert.Equal(PrebuiltShadowCheck.SourceAlContentHash(bundleDir), packaged);
        }
        finally { Cleanup(bundleDir, appPath); }
    }

    /// <summary>
    /// NEGATIVE — a package the check cannot read AL out of (a symbols-only download, or a
    /// file that is not a package at all) yields null, which is what routes the verdict to
    /// the mtime fallback instead of silently reading as "no content, therefore equal".
    /// </summary>
    [Fact]
    public void PrebuiltAlContentHash_IsNullForAPackageWithNoAlSource()
    {
        var dir = NewTempDir();
        try
        {
            var notAPackage = Path.Combine(dir, "Symbols_Only.app");
            File.WriteAllBytes(notAPackage, new byte[] { 0x00, 0x01, 0x02, 0x03 });

            Assert.Null(PrebuiltShadowCheck.PrebuiltAlContentHash(notAPackage));
        }
        finally { Cleanup(dir); }
    }

    // ── The mtime fallback ────────────────────────────────────────────────────

    /// <summary>
    /// With no AL source in the package there is nothing to compare, so the old mtime
    /// comparator still decides — unchanged behaviour for that shape. This is what the
    /// former PrebuiltNewerThanSource_IsNotStale_SoThePrebuiltStillWins asserted
    /// unconditionally; it is now scoped to the case where it is the only signal available.
    /// </summary>
    [Fact]
    public void WithNoAlContentToCompare_PrebuiltNewerThanSource_KeepsThePrebuilt()
    {
        var source = new DateTime(2026, 01, 01, 12, 00, 00, DateTimeKind.Utc);
        var prebuilt = source.AddHours(1);

        var verdict = PrebuiltShadowCheck.Evaluate(
            prebuiltAlContentHash: null, sourceAlContentHash: "abc", prebuilt, source);

        Assert.False(verdict.Stale);
        Assert.Contains("no AL source", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Same fallback, other direction: source newer than the package is stale.</summary>
    [Fact]
    public void WithNoAlContentToCompare_SourceNewerThanPrebuilt_IsStale()
    {
        var prebuilt = new DateTime(2026, 01, 01, 12, 00, 00, DateTimeKind.Utc);
        var source = prebuilt.AddSeconds(1);

        var verdict = PrebuiltShadowCheck.Evaluate(
            prebuiltAlContentHash: "abc", sourceAlContentHash: null, prebuilt, source);

        Assert.True(verdict.Stale);
        Assert.Contains("no AL source", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrebuiltNewerThanSource_IsNotStale_ByTheMtimeComparator()
    {
        var source = new DateTime(2026, 01, 01, 12, 00, 00, DateTimeKind.Utc);
        var prebuilt = source.AddHours(1);

        Assert.False(PrebuiltShadowCheck.SourceIsNewer(prebuilt, source));
    }

    [Fact]
    public void SourceNewerThanPrebuilt_IsStale_ByTheMtimeComparator()
    {
        var prebuilt = new DateTime(2026, 01, 01, 12, 00, 00, DateTimeKind.Utc);
        var source = prebuilt.AddSeconds(1);

        Assert.True(PrebuiltShadowCheck.SourceIsNewer(prebuilt, source));
    }

    [Fact]
    public void IdenticalTimestamps_KeepThePrebuilt()
    {
        // A freshly built .app and its sources routinely share a timestamp; that is the
        // normal "prebuilt is current" case and must NOT be treated as stale.
        var t = new DateTime(2026, 01, 01, 12, 00, 00, DateTimeKind.Utc);

        Assert.False(PrebuiltShadowCheck.SourceIsNewer(t, t));
    }

    [Fact]
    public void NewestAlSourceUtc_FindsTheNewestAlFileRecursively()
    {
        var dir = NewTempDir();
        try
        {
            var nested = Path.Combine(dir, "src", "Sub");
            Directory.CreateDirectory(nested);

            var older = Path.Combine(dir, "src", "A.al");
            File.WriteAllText(older, "codeunit 1 A { }");
            File.SetLastWriteTimeUtc(older, new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc));

            var newer = Path.Combine(nested, "B.al");
            File.WriteAllText(newer, "codeunit 2 B { }");
            var newest = new DateTime(2026, 06, 01, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(newer, newest);

            Assert.Equal(newest, PrebuiltShadowCheck.NewestAlSourceUtc(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void NewestAlSourceUtc_IgnoresNonAlFiles()
    {
        var dir = NewTempDir();
        try
        {
            var al = Path.Combine(dir, "A.al");
            File.WriteAllText(al, "codeunit 1 A { }");
            var alTime = new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(al, alTime);

            // A much newer non-.al file must not drag the answer forward — otherwise every
            // build artifact or log dropped in the folder would look like a source change.
            var other = Path.Combine(dir, "notes.txt");
            File.WriteAllText(other, "hello");
            File.SetLastWriteTimeUtc(other, new DateTime(2026, 12, 01, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(alTime, PrebuiltShadowCheck.NewestAlSourceUtc(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void NewestAlSourceUtc_ReturnsMinValueWhenThereIsNoAlSource()
    {
        var dir = NewTempDir();
        try
        {
            // No .al anywhere => nothing can be newer than the prebuilt, so the prebuilt wins.
            Assert.Equal(DateTime.MinValue, PrebuiltShadowCheck.NewestAlSourceUtc(dir));
            Assert.False(PrebuiltShadowCheck.SourceIsNewer(
                new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                PrebuiltShadowCheck.NewestAlSourceUtc(dir)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void NewestAlSourceUtc_MissingDirectory_ReturnsMinValue()
    {
        Assert.Equal(DateTime.MinValue,
            PrebuiltShadowCheck.NewestAlSourceUtc(Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N"))));
    }

    private static void Cleanup(params string[] paths)
    {
        foreach (var p in paths)
        {
            try
            {
                if (File.Exists(p)) { File.Delete(p); Directory.Delete(Path.GetDirectoryName(p)!, recursive: true); }
                else if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
