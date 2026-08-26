using AlRunner.Provisioning;
using System.Text.Json;

namespace AlRunner.Infrastructure;

/// <summary>
/// Verifies that the selected BC version's engine artifact closure is COMPLETE, and —
/// when asked — auto-resolves it in-process by downloading the missing pieces from the
/// public BC artifact CDN.
///
/// Why this exists: <see cref="BcArtifacts.SelectVersion"/> already fails loud when the
/// artifact root or the requested version dir is entirely absent. This class covers the
/// subtler case: the version dir EXISTS but is incomplete (e.g. a partial /service/ closure
/// download). Since the version-agnostic engine (StripBcAppClosureFromCopyLocal) now serves
/// the BC-app external closure from this dir at runtime, a partial closure fails deep in a
/// FileLoadException instead of at the surface — so we check it up front.
///
/// Policy (per the runner's "no silent download" rule): a missing piece produces ONE loud,
/// detailed message naming every missing file, its exact expected path, the precise manual
/// download command, AND a single one-command auto-resolve (`al-runner provision` /
/// `--auto-provision`). The runner never downloads unless the user opts in.
/// </summary>
public static class ProvisioningCheck
{
    // The engine DLLs the runner binds directly (must be present in the artifact dir so the
    // ALC resolver and the Cecil rewrite can load them).
    private static readonly string[] CoreEngineDlls =
    {
        "Microsoft.Dynamics.Nav.Ncl.dll",
        "Microsoft.Dynamics.Nav.Types.dll",
        "Microsoft.Dynamics.Nav.Common.dll",
        "Microsoft.Dynamics.Nav.Language.dll",
        "Microsoft.Dynamics.Nav.CodeAnalysis.dll",
    };

    // Sentinel of the BC-app external closure that the version-agnostic engine relies on
    // being served from the artifact dir (it was the exact DLL whose absence/skew produced
    // FileLoadException 0x80131621). Its presence signals the full /service/ closure landed.
    private const string ClosureSentinel = "Microsoft.Identity.ServiceEssentials.Core.dll";

    // ── Platform-app R2R check ────────────────────────────────────────────────
    // These Microsoft apps MUST be provided as R2R (publishedartifacts/*.dll) packages.
    // Their procedure bodies are external/native — BC compiles them from AL source produces
    // EMIT-ZERO (the emitter crashes on NavTypeKind issues). The runner defers to service-tier
    // DLL dispatch for their codeunits at runtime. When a symbol-only .app is found in the
    // package cache instead of the R2R runtime package, it is a PROVISIONING gap.
    public static readonly IReadOnlyList<string> KnownPlatformRuntimeApps = new[]
    {
        "System Application",
        "Base Application",
        "Business Foundation",
    };

    /// <summary>True if <paramref name="appName"/> is a known Microsoft platform runtime app.</summary>
    public static bool IsKnownPlatformRuntimeApp(string appName)
        => KnownPlatformRuntimeApps.Any(n =>
            string.Equals(n, appName, StringComparison.OrdinalIgnoreCase));

    // ── Platform symbol app "System" ──────────────────────────────────────────
    // The Microsoft platform symbols app (Name="System", objects 2000000000..2000001000)
    // is NOT an R2R runtime package and never will be — it ships symbol AL whose procedure
    // bodies are external/native (`_Internal` platform methods). Its Tier-3 source-compile
    // ALWAYS fails Roslyn (dozens of CS0103/CS1061), after which the dependency loader
    // falls back to service-tier DLL dispatch anyway. Skipping the doomed compile up front
    // is observably identical and saves ~14.5s per bundle (measured 2026-07-23, Pageworks).
    // Well-known AppId, stable across BC versions.
    public static readonly Guid PlatformSystemAppId = Guid.Parse("8874ed3a-0643-4247-9ced-7a7002f7135d");

    /// <summary>
    /// True if the app is Microsoft's platform symbols app "System" — matched by its
    /// well-known AppId, or by publisher "Microsoft" + name "System" (symbol packages
    /// synthesized without an AppId). ISV apps that happen to be named "System" do NOT
    /// match (they must keep source-compiling and failing LOUD if broken).
    /// </summary>
    public static bool IsPlatformSymbolOnlySystemApp(Guid appId, string publisher, string name)
        => appId == PlatformSystemAppId
           || (string.Equals(publisher, "Microsoft", StringComparison.OrdinalIgnoreCase)
               && string.Equals(name, "System", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Report returned by <see cref="CheckPlatformApps"/>. Each issue entry is a symbol-only
    /// (non-R2R) platform app found in the cache that should be an R2R runtime package.
    /// </summary>
    public sealed record PlatformAppsReport(
        string Version,
        IReadOnlyList<(string Name, string AppVersion, string AppPath)> Issues,
        IReadOnlyList<string> SearchedDirs)
    {
        public bool Ok => Issues.Count == 0;

        /// <summary>
        /// One loud, self-contained message: names every symbol-only platform app + its path,
        /// the exact manual download command, and the one-command auto-resolve.
        /// </summary>
        public string ToDetailedMessage()
        {
            var lines = new List<string>
            {
                "BC runtime apps are not available as R2R packages — only symbol/dev packages were found.",
                "  Symbol-only packages cannot execute at runtime (their procedure bodies are external/native).",
                "",
                "  Apps found as symbol-only (provisioning gap):",
            };
            foreach (var (name, ver, path) in Issues)
                lines.Add($"    'Microsoft {name}' v{ver}  →  {path}");
            lines.Add("");
            lines.Add("  Resolve it ONE of these ways:");
            lines.Add("");
            lines.Add("  (a) One command (recommended):");
            lines.Add("        al-runner provision");
            lines.Add("      or re-run with --auto-provision.");
            lines.Add("");
            lines.Add("  (b) Download Microsoft platform apps only:");
            // Use the FIRST missing app's own real version — not a truncation of Version
            // (the engine version), which can be a different minor and would 404 against
            // the artifact CDN (it needs a FULL artifact version, e.g. 28.2.50931.52786).
            var suggestVer = Issues.Count > 0 ? Issues[0].AppVersion : "<full-version, e.g. 28.2.50931.52786>";
            lines.Add($"        dotnet run --project tools/DownloadArtifacts -- platform-apps {suggestVer} \"<package-cache-dir>\"");
            return string.Join(Environment.NewLine, lines);
        }
    }

    // ── Bundle .alpackages discovery (issue #1678) ────────────────────────────
    // The startup gate that decides whether to fire --auto-provision (or fail loud
    // without it) used to scan ONLY the home-rooted default package caches
    // (~/.bcartifacts.cache, ~/.local/share/al-runner/{symbols,artifacts}) — never the
    // target bundles' own `.alpackages`. That is exactly where every standard AL project's
    // symbol download lives, so for the ordinary shape (symbol-only Microsoft platform
    // apps vendored in the project) the gate saw an EMPTY set, reported "Ok" vacuously, and
    // neither the loud failure nor the --auto-provision download ever fired — the run
    // limped all the way to a cryptic NavNCLMissingMethodException deep in dispatch. This
    // helper is the single source of truth for the bundle-rooted half of that scan, shared
    // by the startup gate, the `provision` subcommand, and any other caller that needs to
    // reason about what a bundle's own package cache actually vendors.

    /// <summary>
    /// Every `.alpackages` directory nested under any of <paramref name="bundlePaths"/>
    /// (recursively — a bundle can be a parent of many suites, each with its own
    /// `.alpackages`). Nonexistent paths are skipped; the result has no duplicates.
    /// Pure filesystem scan — no network, no manifest parsing.
    /// </summary>
    public static IReadOnlyList<string> CollectBundleAlpackagesDirs(IEnumerable<string> bundlePaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var bundle in bundlePaths)
        {
            if (string.IsNullOrEmpty(bundle) || !Directory.Exists(bundle)) continue;
            IEnumerable<string> found;
            try { found = Directory.EnumerateDirectories(bundle, ".alpackages", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var dir in found)
                if (seen.Add(dir))
                    result.Add(dir);
        }
        return result;
    }

    // ── Manifest-derived Microsoft provisioning requirements ────────────────
    // Package-cache contents cannot answer what an empty cache needs. app.json can: its
    // application/platform roots require the curated platform-app set, while explicit
    // Microsoft test dependencies require both that set and the platform artifact's test
    // apps. The declared versions are floors, not pins; callers use the BC version selected
    // for the run as the download target.

    public sealed record MicrosoftProvisioningRequirements(
        bool PlatformAppsRequired,
        bool TestAppsRequired,
        System.Version? MinimumVersion,
        IReadOnlyList<string> RequiredTestAppNames,
        IReadOnlyList<string> ManifestPaths);

    // Direct roots used by Microsoft test apps. The platform artifact's test-app stream can
    // provide these (and their transitive test closure); it cannot provide arbitrary
    // Microsoft application extensions. Treating every non-platform Microsoft dependency as
    // a test root makes e.g. Sales and Inventory Forecast trigger a 100-app download that can
    // never satisfy the manifest, forever. Keep this bounded to the stable framework roots
    // external test apps actually declare.
    private static readonly HashSet<string> KnownMicrosoftTestAppRoots = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Any",
        "Application Test Library",
        "Business Foundation Test Libraries",
        "Library Assert",
        "Library Variable Storage",
        "Permissions Mock",
        "System Application Test Library",
        "Test Runner",
        "Tests-TestLibraries",
    };

    /// <summary>
    /// Derives the Microsoft artifact sets required by the target bundle manifests without
    /// consulting <c>.alpackages</c>. A bundle root with no manifest of its own is walked like
    /// suite enumeration: stop at the first <c>app.json</c> on each branch, and ignore hidden
    /// workspace metadata plus build-output directories.
    /// </summary>
    public static MicrosoftProvisioningRequirements DeriveMicrosoftRequirements(
        IEnumerable<string> bundlePaths)
    {
        var manifests = CollectTargetManifestPaths(bundlePaths);
        var platformRequired = false;
        var testAppsRequired = false;
        System.Version? minimumVersion = null;
        var requiredTestApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void RaiseFloor(string? value)
        {
            if (!System.Version.TryParse(value, out var parsed)) return;
            if (minimumVersion == null || parsed > minimumVersion)
                minimumVersion = parsed;
        }

        static string? StringProperty(JsonElement element, string name)
            => element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        foreach (var manifest in manifests)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var field in new[] { "application", "platform" })
                {
                    if (!root.TryGetProperty(field, out var value)
                        || value.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(value.GetString()))
                        continue;
                    platformRequired = true;
                    RaiseFloor(value.GetString());
                }

                if (!root.TryGetProperty("dependencies", out var dependencies)
                    || dependencies.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var dependency in dependencies.EnumerateArray())
                {
                    var publisher = StringProperty(dependency, "publisher") ?? "";
                    if (!string.Equals(publisher, "Microsoft", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var name = StringProperty(dependency, "name") ?? "";
                    var version = StringProperty(dependency, "version");
                    platformRequired = true;
                    RaiseFloor(version);

                    if (AlRunner.DependencyResolver.IsMicrosoftPlatformApp(name, publisher))
                        continue;

                    if (!KnownMicrosoftTestAppRoots.Contains(name))
                        continue;

                    // Microsoft's test packages depend on both artifact sets. Application
                    // Test Library itself lives in platform-apps; every other explicitly
                    // named test app must also be present in test-apps.
                    testAppsRequired = true;
                    if (!string.Equals(name, "Application Test Library", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(name))
                        requiredTestApps.Add(name);
                }
            }
            catch (JsonException)
            {
                // Normal bundle loading owns malformed-manifest diagnostics. Provisioning
                // must not invent requirements from a document it cannot read.
            }
            catch (IOException)
            {
                // A manifest can disappear between discovery and read in watch mode.
            }
        }

        return new MicrosoftProvisioningRequirements(
            platformRequired,
            testAppsRequired,
            minimumVersion,
            requiredTestApps.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            manifests);
    }

    private static IReadOnlyList<string> CollectTargetManifestPaths(IEnumerable<string> bundlePaths)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bundlePath in bundlePaths)
        {
            if (string.IsNullOrWhiteSpace(bundlePath)) continue;
            string path;
            try { path = Path.GetFullPath(bundlePath); }
            catch { continue; }

            if (File.Exists(path)
                && string.Equals(Path.GetFileName(path), "app.json", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(path);
                continue;
            }

            var start = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            if (string.IsNullOrEmpty(start) || !Directory.Exists(start)) continue;

            string? current = start;
            var enclosing = false;
            while (!string.IsNullOrEmpty(current))
            {
                var candidate = Path.Combine(current, "app.json");
                if (File.Exists(candidate))
                {
                    found.Add(Path.GetFullPath(candidate));
                    enclosing = true;
                    break;
                }
                current = Directory.GetParent(current)?.FullName;
            }
            if (enclosing) continue;

            Walk(start);
        }

        return found.OrderBy(p => p, StringComparer.Ordinal).ToList();

        void Walk(string dir)
        {
            var manifest = Path.Combine(dir, "app.json");
            if (File.Exists(manifest))
            {
                found.Add(Path.GetFullPath(manifest));
                return;
            }

            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(dir).OrderBy(p => p, StringComparer.Ordinal); }
            catch { return; }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (name.StartsWith(".", StringComparison.Ordinal)
                    || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("obj", StringComparison.OrdinalIgnoreCase))
                    continue;
                Walk(child);
            }
        }
    }

    /// <summary>
    /// True when the supplied directories contain the complete curated platform set needed
    /// by <paramref name="requirements"/>. BC 28+ test dependencies additionally require
    /// Application Test Library, which is delivered by <c>platform-apps</c>, not
    /// <c>test-apps</c>.
    /// </summary>
    public static bool PlatformAppsPresent(
        string dir, string fullVersion, MicrosoftProvisioningRequirements requirements)
        => PlatformAppsPresent(new[] { dir }, fullVersion, requirements);

    public static bool PlatformAppsPresent(
        IEnumerable<string> dirs, string fullVersion, MicrosoftProvisioningRequirements requirements)
    {
        if (!requirements.PlatformAppsRequired) return true;

        var packages = ReadMicrosoftPackages(dirs);
        foreach (var runtimeApp in KnownPlatformRuntimeApps)
            if (!packages.TryGetValue(runtimeApp, out var candidates)
                || !candidates.Any(candidate => candidate.IsR2R))
                return false;

        foreach (var app in new[] { "Application", "System" })
            if (!packages.ContainsKey(app))
                return false;

        if (requirements.TestAppsRequired
            && System.Version.TryParse(fullVersion, out var selected)
            && selected.Major >= 28
            && (!packages.TryGetValue("Application Test Library", out var testLibraries)
                || !testLibraries.Any(candidate => candidate.IsR2R)))
            return false;

        return true;
    }

    public static bool TestAppsPresent(
        string dir, MicrosoftProvisioningRequirements requirements)
        => TestAppsPresent(new[] { dir }, requirements);

    public static bool TestAppsPresent(
        IEnumerable<string> dirs, MicrosoftProvisioningRequirements requirements)
    {
        if (!requirements.TestAppsRequired) return true;
        var packages = ReadMicrosoftPackages(dirs);
        if (!packages.ContainsKey(TestToolkitSentinelApp)) return false;
        return requirements.RequiredTestAppNames.All(packages.ContainsKey);
    }

    private sealed record ProvisionedPackage(bool IsR2R);

    private static Dictionary<string, List<ProvisionedPackage>> ReadMicrosoftPackages(
        IEnumerable<string> dirs)
    {
        var packages = new Dictionary<string, List<ProvisionedPackage>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.app", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var file in files)
            {
                var manifest = AlRunner.AppLoader.ReadManifest(file);
                if (manifest == null
                    || !string.Equals(manifest.Publisher, "Microsoft", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!packages.TryGetValue(manifest.Name, out var candidates))
                    packages[manifest.Name] = candidates = new List<ProvisionedPackage>();
                candidates.Add(new ProvisionedPackage(AlRunner.AppLoader.IsR2R(file)));
            }
        }
        return packages;
    }

    /// <summary>
    /// Scan <paramref name="packageCacheDirs"/> for known Microsoft platform runtime apps
    /// (System Application, Base Application, Business Foundation). If any are found as
    /// symbol-only (non-R2R) packages, returns a report listing them. Returns an Ok report
    /// when the apps are absent from the cache (they'll be served by service-tier DLLs) or
    /// when all found apps are R2R.
    /// </summary>
    public static PlatformAppsReport CheckPlatformApps(
        string version,
        IReadOnlyList<string> packageCacheDirs)
    {
        var issues = new List<(string Name, string AppVersion, string AppPath)>();

        foreach (var platformAppName in KnownPlatformRuntimeApps)
        {
            // Collect all instances of this platform app across all cache dirs.
            var found = new List<(string AppPath, string AppVersion, bool IsR2R)>();
            foreach (var dir in packageCacheDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var appFile in Directory.EnumerateFiles(dir, "*.app", SearchOption.AllDirectories))
                {
                    var m = AlRunner.AppLoader.ReadManifest(appFile);
                    if (m == null) continue;
                    if (!string.Equals(m.Publisher, "Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(m.Name, platformAppName, StringComparison.OrdinalIgnoreCase)) continue;
                    found.Add((appFile, m.Version.ToString(), AlRunner.AppLoader.IsR2R(appFile)));
                }
            }

            // Issue: at least one instance found but NONE is R2R.
            if (found.Count > 0 && !found.Any(f => f.IsR2R))
            {
                var best = found.OrderByDescending(f => f.AppVersion).First();
                issues.Add((platformAppName, best.AppVersion, best.AppPath));
            }
        }

        return new PlatformAppsReport(version, issues, packageCacheDirs);
    }

    /// <summary>
    /// Derives the BC major.minor to auto-provision platform apps for. The engine is
    /// version-agnostic w.r.t. the R2R apps it dispatches to at runtime, so the minor to
    /// download is the one the MISSING apps actually need (carried in
    /// <paramref name="report"/>.Issues[0].AppVersion) — NOT the engine's own
    /// <paramref name="fallbackVersion"/> (SelectedVersion), which can be a different minor
    /// (e.g. engine 28.1 running 28.2 R2R business logic). Falls back to
    /// <paramref name="fallbackVersion"/>'s major.minor when there are no issues. Pure —
    /// does no I/O.
    /// </summary>
    public static string DeriveProvisionMajorMinor(PlatformAppsReport report, string fallbackVersion)
    {
        var source = report.Issues.Count > 0 ? report.Issues[0].AppVersion : fallbackVersion;
        var parts = source.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts.Take(2)) : source;
    }

    /// <summary>
    /// Builds the loud, actionable warning message for a known Microsoft platform app
    /// that was found in the package cache as a symbol-only (non-R2R) package. Emitted
    /// by DependencyLoader when it detects this condition to convert the otherwise cryptic
    /// "EMIT-ZERO" error into a provisioning-gap explanation.
    /// </summary>
    public static string BuildPlatformAppMissingR2RMessage(
        string publisher, string appName, string appVersion, string symbolOnlyPath, string bcVersion)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"[provision-gap] '{publisher} {appName}' v{appVersion} is not available as an R2R runtime package.",
            $"  Found:  {symbolOnlyPath}",
            $"  Status: symbol/dev package only — cannot execute at runtime (procedure bodies are external/native).",
            $"  Engine version: {bcVersion} (the app's own version, {appVersion}, may be a different minor —",
            $"  the engine is version-agnostic w.r.t. the R2R apps it dispatches to at runtime).",
            $"  The runner will use service-tier DLL dispatch as a fallback.",
            $"",
            $"  Fix: run ONE of:",
            $"    al-runner provision  (or re-run with --auto-provision)",
            // Suggest the APP's own version, not bcVersion (the engine's) — the engine is
            // version-agnostic w.r.t. the R2R apps it dispatches to, so these can differ
            // (e.g. engine 28.1 running 28.2 R2R apps); using bcVersion here would 404.
            $"    dotnet run --project tools/DownloadArtifacts -- platform-apps {appVersion} \"<package-cache-dir>\"",
        });
    }

    // ── Test-toolkit presence check ───────────────────────────────────────────
    // Microsoft ships the test-toolkit apps (Business Foundation Test Libraries,
    // Application Test Library, System Application Test Library, Test Runner, Library
    // Assert, Library Variable Storage, Tests-TestLibraries, Permissions Mock, …) as a
    // SEPARATE artifact set from the w1 platform apps (they live under the `platform`
    // artifact's Applications/<area>/Test/ tree, not w1/Extensions). A clean cache that
    // only has the platform apps still fails to compile any test bundle that depends on
    // ALTestRunner/Library Assert/etc. Detect that gap here so --auto-provision can close
    // it too, instead of leaving the "re-run with --auto-provision" message from a
    // different check to lie about what it actually does.
    //
    // We only need to detect the presence of ONE well-known test-toolkit app to know the
    // whole set was provisioned together (ArtifactDownloader.TestApps always downloads the
    // full set in one shot) — checking for all of them would just be redundant I/O.
    // The definitive marker that the Microsoft test toolkit is provisioned. Chosen
    // deliberately: it is the foundational test lib every BC test bundle transitively
    // depends on (chain: Tests-TestLibraries → Application Test Library → Business
    // Foundation Test Libraries) AND it is provided ONLY by the downloaded test-apps
    // set — a project's own .alpackages commonly vendors "Application Test Library" but
    // NOT this one. Using a looser OR-match (accepting Application Test Library) falsely
    // reports the toolkit "present" for a cache that vendors App Test Library yet still
    // lacks Business Foundation Test Libraries, so the auto-provision download never fires
    // and the test bundle then fails to compile — exactly the gap this check exists to catch.
    public const string TestToolkitSentinelApp = "Business Foundation Test Libraries";

    /// <summary>
    /// True if the Microsoft test toolkit is provisioned in <paramref name="packageCacheDirs"/>,
    /// detected via <see cref="TestToolkitSentinelApp"/> (a Microsoft-published .app of that
    /// name). Missing/nonexistent dirs are skipped. Pure filesystem scan — no network.
    /// </summary>
    public static bool TestToolkitPresent(IReadOnlyList<string> packageCacheDirs)
    {
        foreach (var dir in packageCacheDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var appFile in Directory.EnumerateFiles(dir, "*.app", SearchOption.AllDirectories))
            {
                var m = AlRunner.AppLoader.ReadManifest(appFile);
                if (m == null) continue;
                if (!string.Equals(m.Publisher, "Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(TestToolkitSentinelApp, m.Name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Derives the BC major.minor to auto-provision FROM WHAT'S ALREADY IN THE CACHE: scans
    /// <paramref name="packageCacheDirs"/> for a Microsoft "Base Application" or "System
    /// Application" .app and returns its major.minor. Used when there's no
    /// <see cref="PlatformAppsReport"/> issue to derive from (e.g. platform apps are already
    /// R2R-complete but the test toolkit is still missing) — we still need SOME minor to
    /// resolve a full artifact version for the test-toolkit download, and the platform apps
    /// already in the cache are the most reliable signal of which minor this project targets.
    /// Falls back to <paramref name="fallbackVersion"/>'s major.minor when no such app is found.
    /// </summary>
    public static string DerivePresentPlatformMajorMinor(
        IReadOnlyList<string> packageCacheDirs, string fallbackVersion)
    {
        foreach (var dir in packageCacheDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var appFile in Directory.EnumerateFiles(dir, "*.app", SearchOption.AllDirectories))
            {
                var m = AlRunner.AppLoader.ReadManifest(appFile);
                if (m == null) continue;
                if (!string.Equals(m.Publisher, "Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(m.Name, "Base Application", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(m.Name, "System Application", StringComparison.OrdinalIgnoreCase))
                    continue;
                var v = m.Version.ToString();
                var vparts = v.Split('.');
                return vparts.Length >= 2 ? string.Join(".", vparts.Take(2)) : v;
            }
        }
        var parts = fallbackVersion.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts.Take(2)) : fallbackVersion;
    }

    // ── Auto-provision download destination (issue #1653) ────────────────────
    // `--auto-provision` used to download platform R2R apps + the MS test toolkit into
    // whichever `--package-cache` dir the caller passed first — i.e. into the *project's*
    // .alpackages (up to ~135 MB / 112 files). That directory is the user's; the runner
    // must never write into it unasked. These two helpers are the single source of truth
    // for the correct destination: the runner-owned artifact cache, exactly mirroring the
    // layout the standalone `al-runner provision` step already uses for the test toolkit
    // (`<artifacts-root>/<version>/test-apps`). Pure path composition — no I/O, so callers
    // can create the dir, or add it as a search root, as needed.

    /// <summary>
    /// Runner-owned directory to download Microsoft platform R2R runtime apps (System
    /// Application, Base Application, Business Foundation) into. Sibling of
    /// <see cref="TestAppsDirFor"/> under the same per-version artifact root.
    /// </summary>
    public static string PlatformAppsDirFor(string artifactsRootDir, string fullVersion)
        => Path.Combine(artifactsRootDir, fullVersion, "platform-apps");

    /// <summary>
    /// Runner-owned directory to download the Microsoft test toolkit (Business Foundation
    /// Test Libraries, Application Test Library, Library Assert, Test Runner, …) into.
    /// </summary>
    public static string TestAppsDirFor(string artifactsRootDir, string fullVersion)
        => Path.Combine(artifactsRootDir, fullVersion, "test-apps");

    // ── Reusing an already-provisioned set instead of re-downloading it ───────
    // The download destinations above were write-only: nothing ever asked whether they
    // were ALREADY populated before fetching into them again. `EnsurePlatformAppsProvisioned`
    // decided "there is a gap" from the target bundle's own `.alpackages`, which vendors
    // symbol-only packages permanently (the runner must never write into the user's
    // project — #1653), so its check was unsatisfiable by construction and it re-downloaded
    // ~106 MB on every single invocation. The startup gate had the same hole one step over:
    // it scans the DEFAULT package caches, which compose these two dirs from the SELECTED
    // version only, so a set provisioned for any other version — the common case, since the
    // version the download targets is derived from the project's vendored symbols, not from
    // the engine — was invisible on the next run.
    //
    // These two helpers are DISCOVERY, deliberately not adjudication: they answer "is there
    // a plausible already-provisioned set for this major.minor?" with a pure filesystem
    // scan. Callers fold the hit into the dirs they scan and then re-run the authoritative
    // predicate (CheckPlatformApps / TestToolkitPresent) over the combined set. That split
    // matters: a discovery predicate strict enough to be authoritative on its own would
    // re-download forever the moment a BC version shipped one fewer platform app than we
    // expect, which is the very failure being fixed here.
    //
    // No network, by design. Resolving major.minor → a full version costs a CDN index
    // fetch and returns null when offline, so checking the destination only AFTER that
    // resolve would still pay a round-trip on every warm run and would fail outright on a
    // fully provisioned but offline machine.

    /// <summary>
    /// Every already-provisioned <c>platform-apps</c> directory matching
    /// <paramref name="majorMinor"/> that carries at least one Microsoft platform runtime app
    /// as an R2R package, newest version first. Empty when there is none.
    ///
    /// <para><paramref name="minVersion"/> is a hard floor and is the load-bearing parameter:
    /// <c>DependencyResolver.SelectBestVersion</c> discards any candidate below the declared
    /// dependency minimum, so an R2R set OLDER than the symbols a project vendors is worse
    /// than useless — it satisfies <see cref="CheckPlatformApps"/> (which compares only
    /// publisher, name and R2R-ness) while resolution rejects it and falls back to the
    /// symbol-only copy, ending in the "object with ID 0 does not have a member with that ID"
    /// failure that DependencyResolver has a dedicated diagnostic for. Pass the highest
    /// version among the gap's own issues (<see cref="MinimumUsefulR2RVersion"/>): an R2R set
    /// at least as new as the vendored symbols is exactly the condition under which
    /// SelectBestVersion prefers it.</para>
    ///
    /// <para>A LIST rather than the single best candidate, because the caller adjudicates:
    /// returning only the newest would let a partial set (an interrupted download that landed
    /// one app) mask a complete older one, and the download would fire anyway — the very
    /// failure this exists to prevent, one version narrower. The prefix match is segment-wise
    /// (<see cref="BcArtifacts.VersionNameMatchesPrefix"/>), so "27.5" never matches
    /// "27.50.x".</para>
    /// </summary>
    public static IReadOnlyList<string> FindProvisionedPlatformAppsDirs(
        string artifactsRootDir, string majorMinor, System.Version? minVersion)
        => FindProvisionedDirs(artifactsRootDir, majorMinor, minVersion,
            PlatformAppsDirFor, HasAnyR2RPlatformApp);

    /// <summary>
    /// Every already-provisioned <c>test-apps</c> directory matching
    /// <paramref name="majorMinor"/> in which the Microsoft test toolkit is actually present
    /// (<see cref="TestToolkitPresent"/>), newest first. A directory holding some toolkit apps
    /// but not the sentinel is a partial download and is deliberately excluded.
    /// </summary>
    public static IReadOnlyList<string> FindProvisionedTestAppsDirs(
        string artifactsRootDir, string majorMinor, System.Version? minVersion)
        => FindProvisionedDirs(artifactsRootDir, majorMinor, minVersion, TestAppsDirFor,
            dir => TestToolkitPresent(new[] { dir }));

    /// <summary>
    /// The lowest R2R version that could actually close <paramref name="report"/>'s gap: the
    /// HIGHEST version among the symbol-only apps it names. Anything below that loses to the
    /// symbol copy it is meant to replace (see
    /// <see cref="FindProvisionedPlatformAppsDirs"/>). Null when there is no issue to close
    /// or no issue version parses.
    /// </summary>
    public static System.Version? MinimumUsefulR2RVersion(PlatformAppsReport report)
    {
        System.Version? floor = null;
        foreach (var (_, appVersion, _) in report.Issues)
        {
            if (!System.Version.TryParse(appVersion, out var v)) continue;
            if (floor == null || v > floor) floor = v;
        }
        return floor;
    }

    private static IReadOnlyList<string> FindProvisionedDirs(
        string artifactsRootDir, string majorMinor, System.Version? minVersion,
        Func<string, string, string> dirFor, Func<string, bool> plausible)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(artifactsRootDir) || string.IsNullOrEmpty(majorMinor)) return result;
        if (!Directory.Exists(artifactsRootDir)) return result;

        IEnumerable<string> children;
        try { children = Directory.EnumerateDirectories(artifactsRootDir); }
        catch { return result; }

        var candidates = children
            .Select(d => Path.GetFileName(
                d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .Where(name => BcArtifacts.VersionNameMatchesPrefix(name, majorMinor))
            .Select(name => (Name: name, Ver: System.Version.TryParse(name, out var v) ? v : null))
            .Where(t => t.Ver != null && (minVersion == null || t.Ver >= minVersion))
            .OrderByDescending(t => t.Ver)
            .ToList();

        foreach (var (name, _) in candidates)
        {
            var dir = dirFor(artifactsRootDir, name);
            if (!Directory.Exists(dir)) continue;
            try { if (plausible(dir)) result.Add(dir); }
            catch { /* unreadable candidate — skip it rather than failing the run */ }
        }
        return result;
    }

    /// <summary>
    /// True if <paramref name="dir"/> holds at least one Microsoft platform runtime app
    /// (<see cref="KnownPlatformRuntimeApps"/>) as an R2R package. Non-empty-ness alone is
    /// not enough: a directory of symbol-only packages cannot execute anything, and treating
    /// it as provisioned would bury the provisioning gap one layer deeper instead of
    /// reporting it.
    /// </summary>
    private static bool HasAnyR2RPlatformApp(string dir)
    {
        foreach (var appFile in Directory.EnumerateFiles(dir, "*.app", SearchOption.AllDirectories))
        {
            var m = AlRunner.AppLoader.ReadManifest(appFile);
            if (m == null) continue;
            if (!string.Equals(m.Publisher, "Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsKnownPlatformRuntimeApp(m.Name)) continue;
            if (AlRunner.AppLoader.IsR2R(appFile)) return true;
        }
        return false;
    }

    public sealed record Report(string Version, string ServiceTierDir, IReadOnlyList<string> MissingFiles)
    {
        public bool Ok => MissingFiles.Count == 0;

        /// <summary>
        /// One loud, self-contained message: names every missing file + its full path, the
        /// exact manual command to fetch them, and the one-command auto-resolve. Detailed
        /// enough for a human or an agent to fix by hand.
        /// </summary>
        public string ToDetailedMessage(string? projectPathForProvisionCmd = null)
        {
            var provisionTarget = projectPathForProvisionCmd is { Length: > 0 }
                ? $" \"{projectPathForProvisionCmd}\""
                : "";
            var lines = new List<string>
            {
                $"BC {Version} engine artifacts are incomplete — the runner will not auto-download.",
                $"Expected under: {ServiceTierDir}",
                "",
                "Missing:",
            };
            foreach (var f in MissingFiles)
                lines.Add($"  - {Path.Combine(ServiceTierDir, f)}");
            lines.Add("");
            lines.Add("Resolve it ONE of these ways:");
            lines.Add("");
            lines.Add("  (a) One command (recommended) — the runner downloads the missing pieces:");
            lines.Add($"        al-runner provision{provisionTarget}");
            lines.Add($"      or re-run your command with --auto-provision.");
            lines.Add("");
            lines.Add("  (b) Manually — fetch the full service-tier closure for this version:");
            lines.Add($"        dotnet run --project tools/DownloadArtifacts -- service-tier {Version} \"{ServiceTierDir}\"");
            lines.Add("");
            lines.Add("  (c) Point the runner at an existing artifact dir with --artifact-path <dir>,");
            lines.Add("      or select a different cached version with --bc-version <ver>.");
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// Check whether the given version's artifact <paramref name="serviceTierDir"/> holds a
    /// complete engine closure. Never throws; returns a <see cref="Report"/> listing what
    /// (if anything) is missing.
    /// </summary>
    public static Report Check(string version, string serviceTierDir)
    {
        var missing = new List<string>();
        if (!Directory.Exists(serviceTierDir))
        {
            // The whole dir is gone — report every required file as missing.
            missing.AddRange(CoreEngineDlls);
            missing.Add(ClosureSentinel);
            return new Report(version, serviceTierDir, missing);
        }
        foreach (var dll in CoreEngineDlls)
            if (!File.Exists(Path.Combine(serviceTierDir, dll)))
                missing.Add(dll);
        if (!File.Exists(Path.Combine(serviceTierDir, ClosureSentinel)))
            missing.Add(ClosureSentinel);
        return new Report(version, serviceTierDir, missing);
    }

    /// <summary>
    /// Download the engine service-tier closure for <paramref name="version"/> into
    /// <paramref name="serviceTierDir"/> (the full /service/ closure — the same set the
    /// manual `service-tier` command fetches). Returns true on success. This is the
    /// opt-in auto-resolve; callers gate it behind `al-runner provision` / `--auto-provision`.
    /// </summary>
    public static bool AutoProvision(string version, string serviceTierDir, Action<string>? log = null)
        => AutoProvision(version, serviceTierDir, ArtifactDownloader.ServiceTier, log);

    internal static bool AutoProvision(
        string version,
        string serviceTierDir,
        Func<string, string, Action<string>?, int> downloadServiceTier,
        Action<string>? log = null)
    {
        var logf = log ?? Console.Error.WriteLine;
        logf($"[provision] downloading BC {version} engine service-tier closure → {serviceTierDir}");
        int rc;
        try
        {
            rc = downloadServiceTier(version, serviceTierDir, logf);
        }
        catch (Exception ex)
        {
            RemoveEmptyFailedProvisioningTarget(serviceTierDir);
            logf($"[provision] download failed before completion: {ex.Message}");
            return false;
        }
        if (rc != 0)
        {
            RemoveEmptyFailedProvisioningTarget(serviceTierDir);
            logf($"[provision] download failed (exit {rc}). See messages above.");
            return false;
        }
        var after = Check(version, serviceTierDir);
        if (!after.Ok)
        {
            RemoveEmptyFailedProvisioningTarget(serviceTierDir);
            logf($"[provision] still incomplete after download; missing: {string.Join(", ", after.MissingFiles)}");
            return false;
        }
        logf($"[provision] BC {version} engine artifacts complete.");
        return true;
    }

    private static void RemoveEmptyFailedProvisioningTarget(string serviceTierDir)
    {
        try
        {
            // ArtifactDownloader creates the version directory before its first request.
            // Do not let an empty failed target outrank a usable same-major cache on the
            // next non-provisioning run. Never remove a partial/non-empty download.
            if (Directory.Exists(serviceTierDir)
                && !Directory.EnumerateFileSystemEntries(serviceTierDir).Any())
                Directory.Delete(serviceTierDir);
        }
        catch
        {
            // Cleanup is best-effort. A concurrent writer or filesystem race must not hide
            // the provisioning error that caused this path.
        }
    }
}
