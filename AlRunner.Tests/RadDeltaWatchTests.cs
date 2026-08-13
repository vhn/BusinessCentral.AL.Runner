using System.Diagnostics;
using System.Text;
using AlRunner.Infrastructure;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// Proves the `--watch` delta path across a three-app dependency chain, editing only the
/// leaf library while its tableextension bridge and test app stay warm.
///
/// Four claims, each of which has failed in practice:
///
/// 1. <b>The edited app's new code actually runs.</b> Before the explicit ownership chain
///    (AlRunner.Rad.AlObjectResolution), a two-app bundle resolved a cross-app call by
///    scanning loaded assemblies in unspecified order, and the PREVIOUS cycle's still-loaded
///    types won as often as not — so this exact edit left the test GREEN against code the
///    developer had just changed.
/// 2. <b>Only the changed object is recompiled.</b> The `[watch] … delta +0 ~1 -0` line is the
///    difference between a proportional inner loop and re-emitting the whole module.
/// 3. <b>The untouched app is not recompiled at all.</b>
/// 4. <b>A rejected C# generation never advances or executes the workspace.</b>
///
/// Spawns the real runner; needs the BC artifact cache. Skips (no-op) when absent.
/// </summary>
[Collection("server-serial")]
public class RadDeltaWatchTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");
    private static readonly string FixtureSrc = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "DeltaTwoApp"));
    private static readonly string TableExtDepSrc = Path.Combine(
        RepoRoot, "tests", "runner-extras", "dep-tableext-platform-base-dep");
    private static readonly string TableExtMainSrc = Path.Combine(
        RepoRoot, "tests", "runner-extras", "dep-tableext-platform-base-main");

    private static bool ArtifactsPresent()
    {
        try { return Directory.Exists(AlRunner.Infrastructure.BcArtifacts.ServiceTierDir); }
        catch { return false; }
    }

    [Fact]
    public async Task Watch_EditingTheLibraryApp_RecompilesOnlyThatObject_AndRunsTheNewCode()
    {
        if (!ArtifactsPresent()) { Console.Error.WriteLine("[skip] BC artifact cache not present"); return; }

        var bundle = Path.Combine(Path.GetTempPath(), "al-runner-rad-delta", Guid.NewGuid().ToString("N"));
        CopyTree(FixtureSrc, bundle);
        var libSource = Path.Combine(bundle, "Lib", "src", "DeltaLib.Codeunit.al");
        var testSource = Path.Combine(bundle, "LibTests", "src", "DeltaLibTests.Codeunit.al");

        // The non-RAD path publishes the same sibling symbols before compiling C in the
        // A <- B <- C chain. Pin that wiring separately; the watch process below uses the
        // resident RAD publisher even for its first, full compile.
        var once = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                + $" \"{bundle}\" --no-cache",
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        once.Environment["AL_RUNNER_RAD"] = "0";
        using (var full = Process.Start(once)!)
        {
            try
            {
                var stdout = full.StandardOutput.ReadToEndAsync();
                var stderr = full.StandardError.ReadToEndAsync();
                await full.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(240));
                var output = (await stdout) + (await stderr);
                Assert.True(full.ExitCode == 0, output);
                Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", output);
            }
            finally
            {
                if (!full.HasExited) try { full.Kill(true); } catch { }
            }
        }

        var lines = new List<string>();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                + $" \"{bundle}\" --watch --no-cache",
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        using var p = Process.Start(psi)!;
        void Pump(StreamReader r) => Task.Run(async () =>
        {
            string? l;
            while ((l = await r.ReadLineAsync()) != null) lock (lines) lines.Add(l);
        });
        Pump(p.StandardOutput);
        Pump(p.StandardError);

        async Task<int> WaitForMarkerAfter(int fromIndex, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (lines)
                    for (int i = fromIndex; i < lines.Count; i++)
                        if (lines[i].Contains("[watch] waiting for AL source")) return i;
                await Task.Delay(200);
            }
            string dump; lock (lines) dump = string.Join("\n", lines.TakeLast(40));
            throw new TimeoutException($"watch marker not seen.\n--- last output ---\n{dump}");
        }

        string Segment(int from, int to)
        {
            lock (lines) return string.Join("\n", lines.GetRange(from, Math.Max(0, to - from)));
        }

        try
        {
            // Cycle 1 (cold): the test passes, and all three apps get a delta baseline.
            int m1 = await WaitForMarkerAfter(0, TimeSpan.FromSeconds(240));
            var cycle1 = Segment(0, m1);
            Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", cycle1);
            Assert.DoesNotContain("FAIL  Codeunit", cycle1);
            Assert.Contains("[watch] Delta Lib: baseline built", cycle1);
            Assert.Contains("[watch] Delta Bridge: baseline built", cycle1);
            Assert.Contains("[watch] Delta Lib Tests: baseline built", cycle1);

            // First edit the test codeunit itself. Its unchanged Install codeunit remains
            // in the baseline generation, but must still seed data before the overlay's
            // test runs.
            await File.AppendAllTextAsync(testSource, "\n// exercise test-app overlay\n");
            int m2 = await WaitForMarkerAfter(m1 + 1, TimeSpan.FromSeconds(240));
            var cycle2 = Segment(m1 + 1, m2);
            Assert.Contains("PASS  Codeunit60941.AnswerIsFortyTwo", cycle2);
            Assert.Contains("[watch] Delta Lib Tests: delta +0 ~1 -0", cycle2);
            Assert.Contains("[watch] Delta Lib Tests: overlay", cycle2);

            // Edit ONLY the library app. The test app is now untouched and still asserts 42.
            var lib = await File.ReadAllTextAsync(libSource);
            var edited = lib.Replace("exit(42);", "exit(43);");
            Assert.NotEqual(lib, edited);
            await File.WriteAllTextAsync(libSource, edited);

            // Cycle 3. Generous budget — this asserts "the cycle finished", not "it was
            // fast"; the delta claim is made by the log assertions below, which fail
            // loudly if the whole module was re-emitted instead.
            int m3 = await WaitForMarkerAfter(m2 + 1, TimeSpan.FromSeconds(240));
            var cycle3 = Segment(m2 + 1, m3);

            // The new body ran: a stale generation would still return 42 and PASS.
            Assert.Contains("Delta Lib Answer returned 43, expected 42", cycle3);

            // Exactly one object recompiled in the edited app…
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle3);
            Assert.Contains("[watch] Delta Lib: overlay", cycle3);
            // …and nothing at all in the app that did not change.
            Assert.Contains("[watch] Delta Bridge: unchanged", cycle3);
            Assert.Contains("[watch] Delta Lib Tests: unchanged", cycle3);
            // The full path is what the delta path replaces here; seeing it means the
            // delta bailed out and the speed claim is not being met.
            Assert.DoesNotContain("[watch] Delta Lib: baseline built", cycle3);

            // Edit the same codeunit again while its first overlay is already loaded.
            // The next overlay must bind against the generation chain without duplicate
            // type ambiguity, and the newest owner must win at runtime.
            var editedAgain = edited.Replace("exit(43);", "exit(44);");
            Assert.NotEqual(edited, editedAgain);
            await File.WriteAllTextAsync(libSource, editedAgain);
            int m4 = await WaitForMarkerAfter(m3 + 1, TimeSpan.FromSeconds(240));
            var cycle4 = Segment(m3 + 1, m4);
            Assert.Contains("FAIL  Codeunit60941.AnswerIsFortyTwo", cycle4);
            Assert.Contains("Delta Lib Answer returned 44, expected 42", cycle4);
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle4);
            Assert.Contains("[watch] Delta Lib: overlay", cycle4);
            Assert.DoesNotContain("[watch] Delta Lib: baseline built", cycle4);

            // A callable-surface change is still an object delta. This app has no
            // same-module callers, so only the changed codeunit is replaced.
            const string surfaceChanged = """
                codeunit 60921 "Delta Lib Answer"
                {
                    Access = Internal;

                    procedure Answer(): Integer
                    begin
                        exit(45);
                    end;

                    procedure Marker(): Integer
                    begin
                        exit(1);
                    end;
                }
                """;
            await File.WriteAllTextAsync(libSource, surfaceChanged);
            int m5 = await WaitForMarkerAfter(m4 + 1, TimeSpan.FromSeconds(240));
            var cycle5 = Segment(m4 + 1, m5);
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle5);
            Assert.Contains("[watch] Delta Lib: overlay", cycle5);
            Assert.DoesNotContain("[watch] Delta Lib: baseline built", cycle5);
            Assert.Contains("Delta Lib Answer returned 45, expected 42", cycle5);

            // Change the callable surface and introduce an AL-valid call whose generated
            // C# is rejected by Roslyn. A failed backend must not commit or run the
            // untouched test app against the last-good generation.
            const string broken = """
                codeunit 60921 "Delta Lib Answer"
                {
                    Access = Internal;

                    procedure Answer(): Integer
                    var
                        FileName: Text;
                    begin
                        Database.ExportData(false, FileName);
                        exit(44);
                    end;

                    procedure Marker(): Integer
                    begin
                        exit(1);
                    end;

                    procedure BrokenMarker(): Integer
                    begin
                        exit(2);
                    end;
                }
                """;
            await File.WriteAllTextAsync(libSource, broken);
            int m6 = await WaitForMarkerAfter(m5 + 1, TimeSpan.FromSeconds(240));
            var cycle6 = Segment(m5 + 1, m6);
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle6);
            Assert.Contains("COMPILE-FAIL", cycle6);
            Assert.DoesNotContain("[watch] Delta Lib: overlay", cycle6);
            Assert.DoesNotContain("PASS  Codeunit60941.", cycle6);
            Assert.DoesNotContain("FAIL  Codeunit60941.", cycle6);

            // Saving the same broken bytes again must retry the delta, not accept
            // hashes recorded before the failed backend and report the app unchanged.
            await File.WriteAllTextAsync(libSource, broken);
            int m7 = await WaitForMarkerAfter(m6 + 1, TimeSpan.FromSeconds(240));
            var cycle7 = Segment(m6 + 1, m7);
            Assert.Contains("COMPILE-FAIL", cycle7);
            Assert.DoesNotContain("[watch] Delta Lib: unchanged", cycle7);
            Assert.DoesNotContain("PASS  Codeunit60941.", cycle7);
            Assert.DoesNotContain("FAIL  Codeunit60941.", cycle7);

            // Repair is another one-object delta from the last committed baseline.
            await File.WriteAllTextAsync(libSource, editedAgain);
            int m8 = await WaitForMarkerAfter(m7 + 1, TimeSpan.FromSeconds(240));
            var cycle8 = Segment(m7 + 1, m8);
            Assert.Contains("[watch] Delta Lib: delta +0 ~1 -0", cycle8);
            Assert.Contains("[watch] Delta Lib: overlay", cycle8);
            Assert.Contains("[watch] Delta Bridge: unchanged", cycle8);
            Assert.Contains("[watch] Delta Lib Tests: unchanged", cycle8);
            Assert.Contains("FAIL  Codeunit60941.AnswerIsFortyTwo", cycle8);
            Assert.Contains("Delta Lib Answer returned 44, expected 42", cycle8);
        }
        finally
        {
            try { p.Kill(true); } catch { }
            try { Directory.Delete(bundle, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Watch_PrecompiledTableExtensionDependency_RehydratesFieldsAfterReload()
    {
        if (!ArtifactsPresent()) { Console.Error.WriteLine("[skip] BC artifact cache not present"); return; }
        var platformApps = CompatiblePlatformApps();
        if (platformApps == null) { Console.Error.WriteLine("[skip] compatible platform-apps not present"); return; }

        var root = Path.Combine(Path.GetTempPath(), "al-runner-rad-tableext", Guid.NewGuid().ToString("N"));
        var dep = Path.Combine(root, "dep");
        var main = Path.Combine(root, "main");
        var packages = Path.Combine(root, "packages");
        CopyTree(TableExtDepSrc, dep);
        CopyTree(TableExtMainSrc, main);
        Directory.CreateDirectory(packages);

        // A real precompiled-dependency shape: source lets DependencyLoader build the
        // runtime DLL, while the embedded SymbolReference is what RecordPatches retains
        // across watch cycles. Keeping the dependency outside the watched source tree is
        // essential — a sibling source app would be reparsed and mask the reload bug.
        var identity = InProcessAppPackager.ReadIdentity(Path.Combine(dep, "app.json"));
        Assert.NotNull(identity);
        var depApp = Path.Combine(packages, "AL_Runner_DTB_Platform_Base_Dep_1_0_0_0.app");
        InProcessAppPackager.EmitAppPackageToFile(
            dep, identity!, depApp, Encoding.UTF8.GetBytes(TableExtSymbolReference));
        var symbolsPath = Path.Combine(packages, "AL_Runner_DTB_Platform_Base_Dep_1_0_0_0.symbols.json");
        File.WriteAllText(symbolsPath, TableExtSymbolReference);
        var platformClosure = Directory.EnumerateFiles(platformApps, "*.app", SearchOption.AllDirectories)
            .Select(AppLoader.ReadManifest)
            .OfType<AppManifest>()
            .Select(m => new DepsSidecarWriter.DepEntry(m.Publisher, m.Name, m.Version, m.AppId));
        DepsSidecarWriter.Write(
            Path.ChangeExtension(symbolsPath, ".deps.json"),
            identity.Publisher, identity.Name, identity.Version, identity.AppId, platformClosure);

        var lines = new List<string>();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = TestBuildConfig.RunArgs(ProjectPath) + TestBuildConfig.BcVersionArg
                + $" \"{main}\" --watch --no-cache --verbose"
                + $" --package-cache \"{packages}\" --package-cache \"{platformApps}\"",
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        using var p = Process.Start(psi)!;
        void Pump(StreamReader r) => Task.Run(async () =>
        {
            string? l;
            while ((l = await r.ReadLineAsync()) != null) lock (lines) lines.Add(l);
        });
        Pump(p.StandardOutput);
        Pump(p.StandardError);

        async Task<int> WaitForMarkerAfter(int fromIndex, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (lines)
                    for (int i = fromIndex; i < lines.Count; i++)
                        if (lines[i].Contains("[watch] waiting for AL source")) return i;
                await Task.Delay(200);
            }
            string dump; lock (lines) dump = string.Join("\n", lines.TakeLast(60));
            throw new TimeoutException($"watch marker not seen.\n--- last output ---\n{dump}");
        }

        string Segment(int from, int to)
        {
            lock (lines) return string.Join("\n", lines.GetRange(from, Math.Max(0, to - from)));
        }

        try
        {
            int m1 = await WaitForMarkerAfter(0, TimeSpan.FromSeconds(240));
            var cycle1 = Segment(0, m1);
            Assert.True(CountOccurrences(cycle1, "PASS  Codeunit63411.") == 4, cycle1);
            Assert.DoesNotContain("extension field 61881", cycle1);
            Assert.Contains("[watch] DTB Platform Base Main: baseline built", cycle1);
            Assert.Contains("precompiled tableextension(s) into _parsedExtensionFields", cycle1);

            // Change one real codeunit file. It recompiles as a one-object overlay, while
            // the dependency remains warm and is not re-registered — the exact lifecycle
            // that used to lose its extension fields after ResetForReload.
            var testSource = Path.Combine(main, "DtbTests.Codeunit.al");
            await File.AppendAllTextAsync(testSource, "\n// trigger warm tableextension reload\n");

            int m2 = await WaitForMarkerAfter(m1 + 1, TimeSpan.FromSeconds(240));
            var cycle2 = Segment(m1 + 1, m2);
            Assert.True(CountOccurrences(cycle2, "PASS  Codeunit63411.") == 4, cycle2);
            Assert.DoesNotContain("extension field 61881", cycle2);
            Assert.DoesNotContain("EXEC-FAIL", cycle2);
            Assert.Contains("[watch] DTB Platform Base Main: delta +0 ~1 -0", cycle2);

            // Pin the cache invariant: reload must re-merge extension metadata without
            // throwing away and rebuilding the already-warm base-table symbol index.
            Assert.Contains("precompiled tableextension(s) into _parsedExtensionFields", cycle2);
            Assert.DoesNotContain("BcAppFallback: indexed", cycle2);
        }
        finally
        {
            try { p.Kill(true); } catch { }
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int i = 0; (i = text.IndexOf(value, i, StringComparison.Ordinal)) >= 0; i += value.Length)
            count++;
        return count;
    }

    private static string? CompatiblePlatformApps()
    {
        var built = BcArtifacts.EngineBuiltVersion();
        if (built == null || !Directory.Exists(BcArtifacts.ArtifactsRootDir)) return null;
        return Directory.EnumerateDirectories(BcArtifacts.ArtifactsRootDir)
            .Select(dir => (dir, parsed: Version.TryParse(Path.GetFileName(dir), out var v) ? v : null))
            .Where(x => x.parsed?.Major == built.Major && x.parsed?.Minor == built.Minor)
            .OrderByDescending(x => x.parsed)
            .Select(x => Path.Combine(x.dir, "platform-apps"))
            .FirstOrDefault(dir => Directory.Exists(dir)
                && Directory.EnumerateFiles(dir, "*.app", SearchOption.AllDirectories).Any());
    }

    private const string TableExtSymbolReference = """
        {
          "RuntimeVersion": "17.0",
          "Codeunits": [],
          "TableExtensions": [
            {
              "TargetObject": "#437dbf0e84ff417a965ded2bb9650972#Item",
              "Fields": [
                {
                  "TypeDefinition": { "Name": "Boolean" },
                  "Properties": [{ "Name": "DataClassification", "Value": "CustomerContent" }],
                  "Id": 61881,
                  "Name": "DTB Repro Flag"
                },
                {
                  "TypeDefinition": { "Name": "Integer" },
                  "Properties": [{ "Name": "DataClassification", "Value": "CustomerContent" }],
                  "Id": 61882,
                  "Name": "DTB Repro Counter"
                }
              ],
              "Id": 61881,
              "Name": "DTB Item Ext"
            }
          ],
          "Reports": [],
          "XmlPorts": [],
          "Queries": [],
          "ControlAddIns": [],
          "EnumTypes": [],
          "DotNetPackages": [],
          "Interfaces": [],
          "PermissionSets": [],
          "PermissionSetExtensions": [],
          "ReportExtensions": [],
          "InternalsVisibleToModules": [],
          "AppId": "5d3c2b1a-6f4e-4a2d-9c1b-8e7f6a5d4c31",
          "Name": "DTB Platform Base Dep",
          "Publisher": "AL Runner",
          "Version": "1.0.0.0"
        }
        """;

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to), overwrite: true);
    }
}
