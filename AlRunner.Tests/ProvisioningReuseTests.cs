// ProvisioningReuseTests — the runner must REUSE an already-provisioned runner-owned
// artifact set instead of re-downloading it on every invocation.
//
// The defect these tests pin down
// ------------------------------
// `--auto-provision` re-downloaded the Microsoft platform R2R apps (~106 MB) on EVERY
// invocation, forever, into a directory it then never read back. Two independent causes:
//
//   1. EnsurePlatformAppsProvisioned decided what an empty project needed by scanning only
//      the target bundle's `.alpackages`. With no downloaded symbols there was no observed
//      gap, so --auto-provision skipped both Microsoft app sets entirely.
//
//   2. Explicit --package-cache arguments replace the defaults, and --artifact-path can
//      bypass the early provisioner. The startup gate therefore also has to discover and
//      attach the runner-owned versioned dirs after BC selection.
//
// Ghost-test traps avoided
// ------------------------
// A "reuse whenever the directory exists" stub would pass the positive cases and FAIL
// SymbolOnlyDestination_IsNotAHit / EmptyDestination_IsNotAHit — the destination is only a
// hit when it actually satisfies the same R2R/toolkit predicate the gate applies.
//
// The cold subprocess tests assert on the fetch ATTEMPT for a fabricated full version. The
// log is emitted before network I/O and no artifact can exist for that version, keeping the
// tests deterministic while proving an incomplete destination is not treated as warm.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Xunit;
using AlRunner.Infrastructure;

namespace AlRunner.Tests;

public sealed class ProvisioningReuseTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    // A BC version that will never exist on Microsoft's artifact index. Any code path that
    // tries to RESOLVE it has, by definition, decided to download rather than reuse.
    private const string FakeVersion = "99.0.1.2";
    private const string FakeMajorMinor = "99.0";
    // A synthetic engine version for the fully-offline `provision` cases. Distinct from
    // FakeVersion so a reuse hit can never be an accident of both sides sharing a prefix.
    private const string SyntheticEngineVersion = "98.7.6.5";

    private readonly string _root;

    public ProvisioningReuseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "al-runner-prov-reuse", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ── .app fabrication ──────────────────────────────────────────────────────
    // Same NAVX shape ProvisioningCheckTests uses: an 8-byte "NAVX" header followed by a
    // zip. R2R-ness is decided by the presence of a `publishedartifacts/*.dll` entry, which
    // is exactly what AppLoader.IsR2R looks for.

    private static byte[] Navx(string appId, string name, string publisher, string version,
        bool r2r)
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
            using (var es = zip.CreateEntry("NavxManifest.xml").Open())
                es.Write(Encoding.UTF8.GetBytes(xml));
            if (r2r)
                using (var ds = zip.CreateEntry("publishedartifacts/app.dll").Open())
                    ds.Write(new byte[] { 0x4D, 0x5A }); // fake PE header
        }
        var zipBytes = ms.ToArray();
        var result = new byte[8 + zipBytes.Length];
        result[0] = (byte)'N'; result[1] = (byte)'A'; result[2] = (byte)'V'; result[3] = (byte)'X';
        BitConverter.TryWriteBytes(result.AsSpan(4, 4), (uint)8);
        zipBytes.CopyTo(result, 8);
        return result;
    }

    private static void WriteApp(string dir, string name, string version, bool r2r,
        string publisher = "Microsoft")
    {
        Directory.CreateDirectory(dir);
        var file = $"{publisher}_{name}_{version}.app";
        File.WriteAllBytes(Path.Combine(dir, file),
            Navx(Guid.NewGuid().ToString(), name, publisher, version, r2r));
    }

    /// <summary>The three platform runtime apps, as R2R packages, in one directory.</summary>
    private static void WriteR2RPlatformSet(string dir, string version)
    {
        foreach (var n in ProvisioningCheck.KnownPlatformRuntimeApps)
            WriteApp(dir, n, version, r2r: true);
    }

    private static void WriteCompleteSelectedPlatformSet(string dir, string version,
        bool includeApplicationTestLibrary)
    {
        WriteR2RPlatformSet(dir, version);
        WriteApp(dir, "Application", version, r2r: true);
        WriteApp(dir, "System", version, r2r: false);
        if (includeApplicationTestLibrary)
            WriteApp(dir, "Application Test Library", version, r2r: true);
    }

    private IReadOnlyList<DependencyRef> WriteEmptyMicrosoftBundle(
        string bundle, string dependencyName = "Application Test Library")
    {
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "app.json"), $$"""
        {
          "id": "{{Guid.NewGuid()}}",
          "name": "Empty Cache Microsoft Fixture",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [
            {
              "id": "{{Guid.NewGuid()}}",
              "publisher": "Microsoft",
              "name": "{{dependencyName}}",
              "version": "28.0.0.0"
            }
          ],
          "platform": "28.0.0.0",
          "application": "28.0.0.0",
          "idRanges": [ { "from": 62150, "to": 62159 } ],
          "runtime": "17.0"
        }
        """);
        return new[]
        {
            new DependencyRef(
                Guid.NewGuid(), dependencyName, "Microsoft", new Version(28, 0, 0, 0)),
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Unit level: the reuse DECISION, pointed at a temp artifacts root.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Positive: a complete R2R platform set already sitting in the runner-owned
    /// destination for the major.minor the download would target IS a hit, so the caller
    /// reuses it instead of downloading.
    /// </summary>
    // Thin wrappers so each test reads as one claim: the API returns an ordered candidate
    // LIST (callers adjudicate each in turn), but most cases assert on the best candidate.
    private static string? BestPlatform(string root, string mm, Version? floor = null)
        => ProvisioningCheck.FindProvisionedPlatformAppsDirs(root, mm, floor).FirstOrDefault();

    private static string? BestTestApps(string root, string mm)
        => ProvisioningCheck.FindProvisionedTestAppsDirs(root, mm, minVersion: null).FirstOrDefault();

    [Fact]
    public void ProvisionedPlatformApps_AreFound_AndReused()
    {
        var dest = ProvisioningCheck.PlatformAppsDirFor(_root, FakeVersion);
        WriteR2RPlatformSet(dest, FakeVersion);

        var hit = BestPlatform(_root, FakeMajorMinor);

        Assert.Equal(dest, hit);
    }

    [Fact]
    public void Bc28PlatformSet_MissingApplicationTestLibrary_IsNotCompleteForATestBundle()
    {
        var bundle = Path.Combine(_root, "bundle");
        var roots = WriteEmptyMicrosoftBundle(bundle, "Tests-TestLibraries");
        var dest = ProvisioningCheck.PlatformAppsDirFor(_root, "28.1.49838.50794");
        WriteCompleteSelectedPlatformSet(dest, "28.1.49838.50794",
            includeApplicationTestLibrary: false);

        var report = ProvisioningCheck.CheckPlatformApps("28.1.49838.50794", new[] { dest });
        Assert.True(ProvisioningCheck.DecideManifestProvisioning(roots, report, new[] { dest })
            .ShouldDownloadPlatform);

        WriteApp(dest, "Application Test Library", "28.1.49838.50794", r2r: true);
        report = ProvisioningCheck.CheckPlatformApps("28.1.49838.50794", new[] { dest });
        Assert.False(ProvisioningCheck.DecideManifestProvisioning(roots, report, new[] { dest })
            .ShouldDownloadPlatform);
    }

    [Fact]
    public void TestSet_MustContainTheMicrosoftAppsNamedByTheTargetManifest()
    {
        var bundle = Path.Combine(_root, "bundle");
        var roots = WriteEmptyMicrosoftBundle(bundle, "Tests-TestLibraries");
        var floors = ProvisioningCheck.DetermineVersionFloors(roots);
        var dest = ProvisioningCheck.TestAppsDirFor(_root, "28.1.49838.50794");
        WriteApp(dest, ProvisioningCheck.TestToolkitSentinelApp, "28.1.49838.50794", r2r: false);

        Assert.False(ProvisioningCheck.TestToolkitPresent(new[] { dest }, floors));

        WriteApp(dest, "Tests-TestLibraries", "28.1.49838.50794", r2r: false);
        Assert.True(ProvisioningCheck.TestToolkitPresent(new[] { dest }, floors));
    }

    /// <summary>
    /// Negative that kills the "reuse whenever the directory exists" stub: a destination
    /// holding SYMBOL-ONLY platform apps is not a hit. Symbol-only packages cannot execute
    /// (procedure bodies are external/native), so reusing them would be exactly the
    /// provisioning gap the check exists to catch — silently, one layer down.
    /// </summary>
    [Fact]
    public void SymbolOnlyDestination_IsNotAHit()
    {
        var dest = ProvisioningCheck.PlatformAppsDirFor(_root, FakeVersion);
        foreach (var n in ProvisioningCheck.KnownPlatformRuntimeApps)
            WriteApp(dest, n, FakeVersion, r2r: false);

        Assert.Null(BestPlatform(_root, FakeMajorMinor));
    }

    /// <summary>
    /// Negative: an empty destination (a download that created the dir then failed) is not
    /// a hit. This is the case the old `Directory.Exists(dir) &amp;&amp; any *.app` guard on the
    /// test-toolkit path got wrong in the other direction.
    /// </summary>
    [Fact]
    public void EmptyDestination_IsNotAHit()
    {
        Directory.CreateDirectory(ProvisioningCheck.PlatformAppsDirFor(_root, FakeVersion));

        Assert.Null(BestPlatform(_root, FakeMajorMinor));
    }

    /// <summary>Negative: nothing provisioned at all is not a hit.</summary>
    [Fact]
    public void AbsentArtifactsRoot_IsNotAHit()
    {
        Assert.Null(BestPlatform(Path.Combine(_root, "does-not-exist"), FakeMajorMinor));
    }

    /// <summary>
    /// Positive: two provisioned builds of the same major.minor — the highest wins, matching
    /// what a fresh download would produce (ResolveVersion returns the latest published
    /// build for the prefix). Uses 9 vs 10 in the last segment so a string sort would pick
    /// the wrong one.
    /// </summary>
    [Fact]
    public void MultipleProvisionedBuilds_HighestVersionWins()
    {
        WriteR2RPlatformSet(ProvisioningCheck.PlatformAppsDirFor(_root, "99.0.1.9"), "99.0.1.9");
        var newest = ProvisioningCheck.PlatformAppsDirFor(_root, "99.0.1.10");
        WriteR2RPlatformSet(newest, "99.0.1.10");

        Assert.Equal(newest, BestPlatform(_root, FakeMajorMinor));
    }

    /// <summary>
    /// Negative: the prefix match is segment-wise, so "99.0" must NOT match "99.01.x".
    /// A naive StartsWith would reuse a set for an unrelated minor.
    /// </summary>
    [Fact]
    public void NeighbouringMinor_DoesNotMatchThePrefix()
    {
        WriteR2RPlatformSet(ProvisioningCheck.PlatformAppsDirFor(_root, "99.01.1.2"), "99.01.1.2");

        Assert.Null(BestPlatform(_root, FakeMajorMinor));
    }

    /// <summary>
    /// Positive, test-toolkit half: a provisioned test-apps dir carrying the toolkit
    /// sentinel app is a hit.
    /// </summary>
    [Fact]
    public void ProvisionedTestApps_AreFound_AndReused()
    {
        var dest = ProvisioningCheck.TestAppsDirFor(_root, FakeVersion);
        WriteApp(dest, ProvisioningCheck.TestToolkitSentinelApp, FakeVersion, r2r: false);

        Assert.Equal(dest, BestTestApps(_root, FakeMajorMinor));
    }

    /// <summary>
    /// Negative, test-toolkit half: a test-apps dir with .app files but WITHOUT the sentinel
    /// is a partial download and must not read as a hit. This is precisely what the old
    /// `Directory.Exists &amp;&amp; any *.app` guard accepted.
    /// </summary>
    [Fact]
    public void PartialTestApps_WithoutSentinel_IsNotAHit()
    {
        var dest = ProvisioningCheck.TestAppsDirFor(_root, FakeVersion);
        WriteApp(dest, "Library Assert", FakeVersion, r2r: false);
        WriteApp(dest, "Any", FakeVersion, r2r: false);

        Assert.Null(BestTestApps(_root, FakeMajorMinor));
    }

    /// <summary>
    /// The version floor, which is the sharpest correctness rule here. A provisioned R2R set
    /// OLDER than the symbols a project vendors satisfies CheckPlatformApps (it compares only
    /// publisher, name and R2R-ness) but DependencyResolver.SelectBestVersion then discards it
    /// as below the declared minimum and falls back to the symbol-only copy — ending in the
    /// "object with ID 0 does not have a member with that ID" failure. Reusing it would be
    /// worse than downloading, and sticky: no later --auto-provision could repair it.
    /// </summary>
    [Fact]
    public void ProvisionedSetOlderThanTheFloor_IsNotReused()
    {
        WriteR2RPlatformSet(ProvisioningCheck.PlatformAppsDirFor(_root, "99.0.1.2"), "99.0.1.2");

        Assert.Null(BestPlatform(_root, FakeMajorMinor, new Version("99.0.1.3")));
    }

    /// <summary>Positive counterpart: exactly at the floor is usable, so the guard is a
    /// minimum and not an off-by-one exclusion of the only good candidate.</summary>
    [Fact]
    public void ProvisionedSetExactlyAtTheFloor_IsReused()
    {
        var dest = ProvisioningCheck.PlatformAppsDirFor(_root, FakeVersion);
        WriteR2RPlatformSet(dest, FakeVersion);

        Assert.Equal(dest, BestPlatform(_root, FakeMajorMinor, new Version(FakeVersion)));
    }

    /// <summary>
    /// Why discovery returns a LIST: an interrupted newer download (one R2R app) must not
    /// mask a complete older set. Returning only the newest candidate would make the caller
    /// adjudicate the partial one, fail, and download 106 MB it already had.
    /// </summary>
    [Fact]
    public void PartialNewerSet_DoesNotMaskACompleteOlderSet()
    {
        WriteApp(ProvisioningCheck.PlatformAppsDirFor(_root, "99.0.9.9"),
            "Base Application", "99.0.9.9", r2r: true);   // partial: one app only
        var complete = ProvisioningCheck.PlatformAppsDirFor(_root, "99.0.1.2");
        WriteR2RPlatformSet(complete, "99.0.1.2");

        var candidates = ProvisioningCheck.FindProvisionedPlatformAppsDirs(_root, FakeMajorMinor, null);

        // Newest first, but the complete older set is still offered so the caller can reach it.
        Assert.Equal(2, candidates.Count);
        Assert.Equal(complete, candidates[1]);
        Assert.Equal(ProvisioningCheck.PlatformAppsDirFor(_root, "99.0.9.9"), candidates[0]);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // The artifacts-root override itself.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Positive: AL_RUNNER_ARTIFACTS_ROOT wins over the home-rooted default, so a caller can
    /// isolate the artifacts root (and with it the provisioning destinations) without moving
    /// HOME and dragging every other home-rooted path along.
    /// </summary>
    [Fact]
    public void ArtifactsRootOverride_WhenSet_WinsOverTheHomeDefault()
    {
        Assert.Equal("/somewhere/else",
            BcArtifacts.ResolveArtifactsRoot("/somewhere/else", "/home/someone"));
    }

    /// <summary>
    /// Negative: unset, empty and whitespace-only all fall back to the home-rooted default —
    /// an exported-but-empty variable must not silently point the runner at the filesystem
    /// root, where the version scan would find nothing and report the engine missing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ArtifactsRootOverride_WhenBlank_FallsBackToTheHomeDefault(string? blank)
    {
        var resolved = BcArtifacts.ResolveArtifactsRoot(blank, "/home/someone");

        Assert.StartsWith("/home/someone", resolved);
        Assert.Contains("artifacts", resolved);
        Assert.NotEqual("/home/someone", resolved);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Integration level: the real startup gate, in a subprocess, hermetic.
    //
    // Relocation is done with AL_RUNNER_ARTIFACTS_ROOT, the runner's own override for the
    // artifacts root. The alternative was moving HOME, which drags every other home-rooted
    // path along (cache roots, default package caches) and forces the fixture to rebuild the
    // `.local/share/al-runner/artifacts` layout by hand — a second spelling of a path
    // BcArtifacts owns, and the exact drift TestArtifactsGateTests exists to forbid.
    //
    // `--package-cache <empty dir>` is load-bearing, not decoration: it REPLACES the default
    // package caches rather than adding to them, so it stops whatever Microsoft symbol
    // packages happen to be cached on the host machine from changing which app the gate
    // reports first — and therefore which major.minor the reuse lookup is keyed on.

    private sealed record Fixture(string ArtifactsRoot, string Bundle, string EmptyPackageCache);

    /// <summary>
    /// A synthetic artifacts root holding a complete R2R platform set provisioned for the
    /// fabricated version, plus a bundle whose own `.alpackages` vendors the SYMBOL-ONLY
    /// counterpart — which is what makes the gate see a provisioning gap in the first place.
    /// The toolkit sentinel is vendored alongside so the toolkit half of the gate is already
    /// satisfied and this fixture isolates the platform-app decision.
    /// </summary>
    private Fixture WriteFixture(bool withProvisionedSet = true,
        string provisionedVersion = FakeVersion)
    {
        var artifacts = Path.Combine(_root, "artifacts");
        Directory.CreateDirectory(artifacts);
        if (withProvisionedSet)
            WriteCompleteSelectedPlatformSet(
                ProvisioningCheck.PlatformAppsDirFor(artifacts, provisionedVersion),
                provisionedVersion,
                includeApplicationTestLibrary: false);

        var emptyCache = Path.Combine(_root, "pkgcache");
        Directory.CreateDirectory(emptyCache);

        var bundle = Path.Combine(_root, "bundle");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "app.json"), """
        {
          "id": "c7f1a9d2-3b4e-4f50-9a61-2d8e7c0b5f31",
          "name": "Provisioning Reuse Fixture",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 62150, "to": 62159 } ],
          "runtime": "14.0"
        }
        """);
        File.WriteAllText(Path.Combine(bundle, "Assert.Codeunit.al"), """
        codeunit 62151 "PRF Assert"
        {
            procedure AreEqual(Expected: Integer; Actual: Integer; Msg: Text)
            begin
                if Expected <> Actual then
                    Error('Expected:<%1> Actual:<%2> %3', Expected, Actual, Msg);
            end;
        }
        """);
        File.WriteAllText(Path.Combine(bundle, "ReuseTest.Codeunit.al"), """
        codeunit 62152 "PRF Reuse Tests"
        {
            Subtype = Test;

            var
                Assert: Codeunit "PRF Assert";

            [Test]
            procedure ReuseCheck()
            begin
                Assert.AreEqual(7, 3 + 4, 'reuse sanity');
            end;
        }
        """);

        var alpackages = Path.Combine(bundle, ".alpackages");
        foreach (var n in ProvisioningCheck.KnownPlatformRuntimeApps)
            WriteApp(alpackages, n, provisionedVersion, r2r: false);
        WriteApp(alpackages, ProvisioningCheck.TestToolkitSentinelApp, provisionedVersion, r2r: false);

        return new Fixture(artifacts, bundle, emptyCache);
    }

    private Fixture WriteEmptyMicrosoftFixture(bool withProvisionedSets)
    {
        var artifacts = Path.Combine(_root, "artifacts");
        Directory.CreateDirectory(artifacts);
        var emptyCache = Path.Combine(_root, "pkgcache");
        Directory.CreateDirectory(emptyCache);
        var bundle = Path.Combine(_root, "empty-microsoft-bundle");
        WriteEmptyMicrosoftBundle(bundle);

        if (withProvisionedSets)
        {
            WriteCompleteSelectedPlatformSet(
                ProvisioningCheck.PlatformAppsDirFor(artifacts, SyntheticEngineVersion),
                SyntheticEngineVersion,
                includeApplicationTestLibrary: true);
            WriteApp(ProvisioningCheck.TestAppsDirFor(artifacts, SyntheticEngineVersion),
                ProvisioningCheck.TestToolkitSentinelApp, SyntheticEngineVersion, r2r: false);
        }

        return new Fixture(artifacts, bundle, emptyCache);
    }

    /// <summary>
    /// Adds a synthetic engine to the fixture's artifacts root: the six files
    /// ProvisioningCheck.Check looks for, so the engine closure reads as COMPLETE and the
    /// multi-GB service-tier download never fires. Sufficient for the `provision` subcommand,
    /// which returns before anything loads the engine — and therefore lets that path be
    /// tested on a machine with no BC artifacts at all.
    /// </summary>
    private static void WriteSyntheticEngine(Fixture fx, string version)
    {
        var dir = Path.Combine(fx.ArtifactsRoot, version);
        Directory.CreateDirectory(dir);
        foreach (var f in new[]
        {
            "Microsoft.Dynamics.Nav.Ncl.dll",
            "Microsoft.Dynamics.Nav.Types.dll",
            "Microsoft.Dynamics.Nav.Common.dll",
            "Microsoft.Dynamics.Nav.Language.dll",
            "Microsoft.Dynamics.Nav.CodeAnalysis.dll",
            "Microsoft.Identity.ServiceEssentials.Core.dll",
        }) File.WriteAllText(Path.Combine(dir, f), "x");
        // Keep the toolkit half satisfied at the exact version `provision` will ask about.
        WriteApp(ProvisioningCheck.TestAppsDirFor(fx.ArtifactsRoot, version),
            ProvisioningCheck.TestToolkitSentinelApp, version, r2r: false);
    }

    private static void LinkRealEngineClosure(Fixture fx, string version, string sourceDir)
    {
        var dir = Path.Combine(fx.ArtifactsRoot, version);
        Directory.CreateDirectory(dir);
        foreach (var source in Directory.EnumerateFiles(sourceDir))
            File.CreateSymbolicLink(Path.Combine(dir, Path.GetFileName(source)), source);
    }

    private static (string output, int exit) RunRunner(
        Fixture fx, string args, bool blockNetwork = false)
    {
        var line = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
        line.Append(' ').Append(args);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet", Arguments = line.ToString(),
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        psi.Environment[AlRunner.Infrastructure.BcArtifacts.ArtifactsRootEnvVar] = fx.ArtifactsRoot;
        if (blockNetwork)
        {
            // Provisioning must reveal the version it chose before any HTTP request. Route
            // HTTP through a guaranteed-closed local endpoint so the exact-version negative
            // path stays hermetic and fails immediately instead of downloading an engine.
            const string closedProxy = "http://127.0.0.1:1";
            foreach (var name in new[]
            {
                "http_proxy", "HTTP_PROXY", "https_proxy", "HTTPS_PROXY",
                "all_proxy", "ALL_PROXY",
            }) psi.Environment[name] = closedProxy;
            psi.Environment.Remove("no_proxy");
            psi.Environment.Remove("NO_PROXY");
        }

        var sb = new StringBuilder();
        var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(600_000)) { try { p.Kill(true); } catch { } throw new TimeoutException("runner hung"); }
        p.WaitForExit();
        lock (sb) return (sb.ToString(), p.ExitCode);
    }

    /// <summary>
    /// The provisioning driver (`al-runner provision`, shared with --auto-provision) must
    /// reuse an already-provisioned R2R set instead of re-downloading it.
    ///
    /// <para>The selected full version is the cache identity and download target. A complete
    /// set at that exact destination must be accepted before any CDN operation.</para>
    ///
    /// <para>Fully synthetic — no real BC artifacts, so it runs on any machine.</para>
    /// </summary>
    [Fact]
    public void Provision_ReusesProvisionedPlatformApps_WithoutResolvingOrDownloading()
    {
        var fx = WriteFixture(provisionedVersion: SyntheticEngineVersion);
        WriteSyntheticEngine(fx, SyntheticEngineVersion);

        var (output, exit) = RunRunner(fx,
            $"provision \"{fx.Bundle}\" --bc-version {SyntheticEngineVersion}");

        // Negative: no CDN resolution, no download of either artifact set.
        Assert.DoesNotContain("could not resolve a full BC artifact version", output);
        Assert.DoesNotContain("Resolving BC version prefix", output);
        Assert.DoesNotContain("fetching Microsoft platform R2R apps", output);
        // Positive: it named the directory it reused, and finished cleanly.
        Assert.Contains("platform apps already complete", output);
        Assert.Contains(
            ProvisioningCheck.PlatformAppsDirFor(fx.ArtifactsRoot, SyntheticEngineVersion), output);
        Assert.Equal(0, exit);
    }

    /// <summary>
    /// Contrast case proving the reuse above is conditional on the destination really being
    /// populated, not on the code path merely being taken: with the provisioned set absent,
    /// the same command DOES go to the CDN. An "always reuse" implementation fails here.
    /// </summary>
    [Fact]
    public void Provision_WithoutProvisionedPlatformApps_StillGoesToTheCdn()
    {
        var fx = WriteFixture(withProvisionedSet: false);
        WriteSyntheticEngine(fx, SyntheticEngineVersion);

        var (output, _) = RunRunner(fx,
            $"provision \"{fx.Bundle}\" --bc-version {SyntheticEngineVersion}");

        // Asserts the ATTEMPT (logged before the HTTP call) rather than the CDN's answer, so
        // the claim "it went to the network" holds identically online and offline. A selected
        // four-part version is used directly; provisioning must not resolve a different build.
        Assert.Contains(
            $"fetching Microsoft platform R2R apps for BC {SyntheticEngineVersion}", output);
        Assert.DoesNotContain("Resolving BC version prefix", output);
        Assert.DoesNotContain("already provisioned at", output);
    }

    [Fact]
    public void Provision_EmptyAlpackages_AttemptsSelectedVersionPlatformAppsFromManifest()
    {
        var fx = WriteEmptyMicrosoftFixture(withProvisionedSets: false);
        WriteSyntheticEngine(fx, SyntheticEngineVersion);

        var (output, exit) = RunRunner(fx,
            $"provision \"{fx.Bundle}\" --bc-version {SyntheticEngineVersion}");

        Assert.Contains(
            $"fetching Microsoft platform R2R apps for BC {SyntheticEngineVersion}", output);
        Assert.DoesNotContain("platform R2R apps already present for the target bundle(s)", output);
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void Provision_EmptyAlpackages_WarmSelectedVersionCacheAvoidsTheCdn()
    {
        var fx = WriteEmptyMicrosoftFixture(withProvisionedSets: true);
        WriteSyntheticEngine(fx, SyntheticEngineVersion);

        var (output, exit) = RunRunner(fx,
            $"provision \"{fx.Bundle}\" --bc-version {SyntheticEngineVersion}");

        Assert.DoesNotContain("Resolving artifact size", output);
        Assert.DoesNotContain("fetching Microsoft platform R2R apps", output);
        Assert.Contains("platform apps already complete", output);
        Assert.Contains(ProvisioningCheck.PlatformAppsDirFor(
            fx.ArtifactsRoot, SyntheticEngineVersion), output);
        Assert.Equal(0, exit);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvisioningModes_WithoutExplicitVersion_TargetTheExactBuiltEngine(bool autoProvision)
    {
        var built = BcArtifacts.EngineBuiltVersion();
        TestArtifacts.SkipIf(built == null, "engine built version is not baked into this build");
        var differentMinor = $"{built!.Major}.{built.Minor + 3}.999998.999998";
        var fx = WriteEmptyMicrosoftFixture(withProvisionedSets: false);
        WriteSyntheticEngine(fx, differentMinor);

        var args = autoProvision
            ? $"\"{fx.Bundle}\" --auto-provision --test 85257"
            : "provision";
        var (output, exit) = RunRunner(fx, args, blockNetwork: true);

        Assert.Contains($"targeting BC {built}", output);
        Assert.Contains($"downloading BC {built} engine service-tier closure", output);
        Assert.DoesNotContain(differentMinor, output);
        Assert.Contains("could not be provisioned. If that build is no longer published", output);
        Assert.Equal(autoProvision ? 2 : 1, exit);
        Assert.False(Directory.Exists(Path.Combine(fx.ArtifactsRoot, built.ToString())),
            "a failed cold provision must not leave an empty exact-version cache that wins later selection");
    }

    [SkippableFact]
    public void Provision_WithoutExplicitVersion_ReusesTheWarmExactBuild()
    {
        var built = BcArtifacts.EngineBuiltVersion();
        TestArtifacts.SkipIf(built == null, "engine built version is not baked into this build");
        var fx = WriteEmptyMicrosoftFixture(withProvisionedSets: false);
        WriteSyntheticEngine(fx, built!.ToString());

        var (output, exit) = RunRunner(fx, "provision", blockNetwork: true);

        Assert.Contains($"targeting BC {built}", output);
        Assert.Contains($"BC {built} engine artifacts already complete", output);
        Assert.DoesNotContain("downloading BC", output);
        Assert.DoesNotContain("Resolving BC version prefix", output);
        Assert.Equal(0, exit);
    }

    [SkippableFact]
    public void Provision_ManifestAppFailure_DoesNotBlameTheExactEngineBuild()
    {
        var built = BcArtifacts.EngineBuiltVersion();
        TestArtifacts.SkipIf(built == null, "engine built version is not baked into this build");
        var fx = WriteEmptyMicrosoftFixture(withProvisionedSets: false);
        WriteSyntheticEngine(fx, built!.ToString());

        var (output, exit) = RunRunner(fx,
            $"provision \"{fx.Bundle}\"", blockNetwork: true);

        Assert.Contains($"BC {built} engine artifacts already complete", output);
        Assert.Contains($"fetching Microsoft platform R2R apps for BC {built}", output);
        Assert.DoesNotContain("If that build is no longer published", output);
        Assert.Equal(1, exit);
    }

    [Fact]
    public void AutoProvision_DownloaderException_CleansEmptyTargetAndReturnsFalse()
    {
        var target = Path.Combine(_root, "artifacts", "28.1.999997.999997");
        var messages = new List<string>();

        var ok = ProvisioningCheck.AutoProvision(
            "28.1.999997.999997",
            target,
            (_, outputDir, _) =>
            {
                Directory.CreateDirectory(outputDir);
                throw new IOException("synthetic interrupted download");
            },
            messages.Add);

        Assert.False(ok);
        Assert.False(Directory.Exists(target));
        Assert.Contains(messages,
            message => message.Contains("synthetic interrupted download", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void AutoProvision_WithoutExplicitVersion_ReusesWarmExactBuildAndRuns()
    {
        TestArtifacts.SkipIfMissing();
        var built = BcArtifacts.EngineBuiltVersion();
        TestArtifacts.SkipIf(built == null, "engine built version is not baked into this build");
        var engineDir = BcArtifacts.ArtifactDirFor(built!.ToString());
        TestArtifacts.SkipIfDirectoryMissing(engineDir, "the built engine's artifact dir");
        var fx = WriteFixture(provisionedVersion: built.ToString());
        try
        {
            LinkRealEngineClosure(fx, built.ToString(), engineDir);
        }
        catch (Exception ex)
        {
            throw new SkipException($"engine-closure symlinks unavailable: {ex.Message}");
        }

        var (output, exit) = RunRunner(fx,
            $"\"{fx.Bundle}\" --auto-provision --test 62152", blockNetwork: true);

        Assert.Contains($"targeting BC {built}", output);
        Assert.Contains($"BC {built} engine artifacts already complete", output);
        Assert.Contains($"selected BC {built}", output);
        Assert.DoesNotContain("downloading BC", output);
        Assert.Contains("Codeunit62152.ReuseCheck", output);
        Assert.Equal(0, exit);
    }

    [SkippableFact]
    public void AutoProvision_FailedPlatformFetch_IsAttemptedOnlyOncePerInvocation()
    {
        TestArtifacts.SkipIfMissing();
        var built = BcArtifacts.EngineBuiltVersion();
        TestArtifacts.SkipIf(built == null, "engine built version is not baked into this build");
        var engineDir = BcArtifacts.ArtifactDirFor(built!.ToString());
        TestArtifacts.SkipIfDirectoryMissing(engineDir, "the built engine's artifact dir");
        var unavailableVersion = $"{built!.Major}.{built.Minor}.999999.999999";
        var fx = WriteEmptyMicrosoftFixture(withProvisionedSets: false);
        try
        {
            LinkRealEngineClosure(fx, unavailableVersion, engineDir);
        }
        catch (Exception ex)
        {
            throw new SkipException($"engine-closure symlinks unavailable: {ex.Message}");
        }

        var (output, exit) = RunRunner(fx,
            $"\"{fx.Bundle}\" --bc-version {unavailableVersion} " +
            $"--package-cache \"{fx.EmptyPackageCache}\" --auto-provision");

        var attempt = $"fetching Microsoft platform R2R apps for BC {unavailableVersion}";
        var attempts = output.Split(attempt, StringSplitOptions.None).Length - 1;
        Assert.True(attempts == 1, $"expected one provisioning attempt; attempts={attempts}\n{output}");
        Assert.NotEqual(0, exit);
    }

    [SkippableFact]
    public void AutoProvision_ReusesCompleteExplicitCacheBeforeAnyDownload()
    {
        TestArtifacts.SkipIfMissing();
        var built = BcArtifacts.EngineBuiltVersion();
        TestArtifacts.SkipIf(built == null, "engine built version is not baked into this build");
        var engineDir = BcArtifacts.ArtifactDirFor(built!.ToString());
        TestArtifacts.SkipIfDirectoryMissing(engineDir, "the built engine's artifact dir");
        var unavailableVersion = $"{built!.Major}.{built.Minor}.999998.999998";
        var fx = WriteFixture(withProvisionedSet: false, provisionedVersion: unavailableVersion);
        try
        {
            LinkRealEngineClosure(fx, unavailableVersion, engineDir);
        }
        catch (Exception ex)
        {
            throw new SkipException($"engine-closure symlinks unavailable: {ex.Message}");
        }
        var completeCache = Path.Combine(_root, "complete-explicit-cache");
        WriteCompleteSelectedPlatformSet(
            completeCache, unavailableVersion, includeApplicationTestLibrary: false);

        var (output, exit) = RunRunner(fx,
            $"\"{fx.Bundle}\" --bc-version {unavailableVersion} " +
            $"--package-cache \"{completeCache}\" --auto-provision --test Codeunit62152");

        Assert.DoesNotContain("fetching Microsoft platform R2R apps", output);
        Assert.Contains("Codeunit62152.ReuseCheck", output);
        Assert.Equal(0, exit);
    }

    [SkippableFact]
    public void Run_WithoutAutoProvision_ReusesWarmTestAppsWithoutDownloading()
    {
        TestArtifacts.SkipIfMissing();
        var built = BcArtifacts.EngineBuiltVersion();
        TestArtifacts.SkipIf(built == null, "engine built version is not baked into this build");
        var engineDir = BcArtifacts.ArtifactDirFor(built!.ToString());
        TestArtifacts.SkipIfDirectoryMissing(engineDir, "the built engine's artifact dir");
        var selectedVersion = built.ToString();
        var fx = WriteEmptyMicrosoftFixture(withProvisionedSets: false);
        try
        {
            LinkRealEngineClosure(fx, selectedVersion, engineDir);
        }
        catch (Exception ex)
        {
            throw new SkipException($"engine-closure symlinks unavailable: {ex.Message}");
        }
        WriteApp(ProvisioningCheck.TestAppsDirFor(fx.ArtifactsRoot, selectedVersion),
            ProvisioningCheck.TestToolkitSentinelApp, selectedVersion, r2r: false);

        var (output, _) = RunRunner(fx,
            $"\"{fx.Bundle}\" --bc-version {selectedVersion} " +
            $"--package-cache \"{fx.EmptyPackageCache}\" --no-auto-provision");

        Assert.Contains("reusing already-provisioned MS test toolkit", output);
        Assert.DoesNotContain("fetching the MS test toolkit", output);
    }

    /// <summary>
    /// The startup gate — the half that decides whether a normal RUN proceeds — must reuse
    /// too, and must do so ahead of its loud `exit 2` bail rather than only ahead of the
    /// download: a machine holding a complete provisioned set must never be told it has a
    /// provisioning gap. Needs the real engine (the run executes AL), supplied via
    /// --artifact-path so the synthetic artifacts root stays in charge of everything else.
    /// </summary>
    [SkippableFact]
    public void Run_ReusesProvisionedPlatformApps_AndExecutesTheBundle()
    {
        TestArtifacts.SkipIfMissing();
        var built = BcArtifacts.EngineBuiltVersion();
        TestArtifacts.SkipIf(built == null, "engine built version is not baked into this build");
        var engineDir = BcArtifacts.ArtifactDirFor(built!.ToString());
        TestArtifacts.SkipIfDirectoryMissing(engineDir, "the built engine's artifact dir");

        var fx = WriteFixture(provisionedVersion: built.ToString());

        var (output, exit) = RunRunner(fx,
            $"\"{fx.Bundle}\" --artifact-path \"{engineDir}\" " +
            $"--package-cache \"{fx.EmptyPackageCache}\" --auto-provision");

        Assert.DoesNotContain("could not resolve a full BC artifact version", output);
        Assert.DoesNotContain("platform R2R apps missing — downloading", output);
        Assert.Contains("reusing already-provisioned platform apps for selected BC", output);
        Assert.Contains("Codeunit62152.ReuseCheck", output);
        Assert.Equal(0, exit);
    }

    [SkippableFact]
    public void AutoProvision_EmptyExplicitCache_AddsManifestRequiredWarmSetsAndRuns()
    {
        TestArtifacts.SkipIfMissing();
        var built = BcArtifacts.EngineBuiltVersion();
        TestArtifacts.SkipIf(built == null, "engine built version is not baked into this build");
        var engineDir = BcArtifacts.ArtifactDirFor(built!.ToString());
        TestArtifacts.SkipIfDirectoryMissing(engineDir, "the built engine's artifact dir");

        var platformApps = TestArtifacts.PlatformAppsDir();
        var testApps = Path.Combine(TestArtifacts.HomeDir() ?? string.Empty,
            ".al-runner", "test-apps");
        TestArtifacts.SkipIfDirectoryMissing(platformApps, "CI-style platform-apps cache");
        TestArtifacts.SkipIfDirectoryMissing(testApps, "CI-style test-apps cache");

        var artifacts = Path.Combine(_root, "artifacts");
        var selectedRoot = Path.Combine(artifacts, built.ToString());
        Directory.CreateDirectory(selectedRoot);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(selectedRoot, "platform-apps"), platformApps);
            Directory.CreateSymbolicLink(Path.Combine(selectedRoot, "test-apps"), testApps);
        }
        catch (Exception ex)
        {
            throw new SkipException($"directory symlinks unavailable for warm-cache integration test: {ex.Message}");
        }

        var emptyCache = Path.Combine(_root, "empty-package-cache");
        Directory.CreateDirectory(emptyCache);
        var bundle = Path.Combine(RepoRoot, "tests", "runner-extras", "microsoft-test-library");
        var fx = new Fixture(artifacts, bundle, emptyCache);

        var (output, exit) = RunRunner(fx,
            $"\"{bundle}\" --artifact-path \"{engineDir}\" " +
            $"--package-cache \"{emptyCache}\" --auto-provision --test Codeunit62201");

        Assert.DoesNotContain("fetching Microsoft platform R2R apps", output);
        Assert.Contains("reusing already-provisioned platform", output);
        Assert.Contains("Codeunit62201.BaseAppCodeunit", output);
        Assert.Equal(0, exit);
    }
}
