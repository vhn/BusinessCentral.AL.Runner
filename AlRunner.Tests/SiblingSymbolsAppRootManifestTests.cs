using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The in-bundle sibling-symbols compile (<c>Program.PublishSiblingSymbols</c> →
/// <c>BcCompiler.EmitDepSymbols</c>) resolved the compiled app's own app.json by scanning
/// only its SOURCE folders. <c>CollectSuitePaths</c> reduces an app that keeps its AL under
/// <c>src/</c> to exactly <c>[&lt;app&gt;/src]</c>, so for the app-root-plus-<c>src/</c>
/// layout — the one every real BC app uses — the lookup found no manifest at all and
/// <c>ReadManifestCompilerInputs</c> returned its "unset" defaults: no
/// <c>contextSensitiveHelpUrl</c>, no <c>features</c>, no <c>preprocessorSymbols</c>.
///
/// <para>The flat layout (app.json beside the .al) is the one shape where that scan
/// accidentally succeeds, and every fixture in the repo used it — including #1898's and
/// #1948's own — so the manifest work on this path was only ever exercised in the layout
/// that cannot expose the gap. Measured on a real ISV bundle (Application + Test as ONE
/// bundle): the Application's own <c>Emit</c> compiled cleanly, because <c>Emit</c> resolves
/// through <c>ResolveManifestInputs(appRootDir, dirs)</c>, while the sibling-symbols compile
/// of the SAME app raised AL0543 on all 295 of its <c>ContextSensitiveHelpPage</c> properties,
/// threw, and cost the dependent Test app all 298 of its objects to EMIT-EXCLUDED.</para>
///
/// <para>These tests take the SINGLE-bundle path deliberately: two app dirs under one bundle
/// root, so <c>EnumerateSuites</c> yields two suites and the sibling-symbols machinery runs.
/// <see cref="LayeredDepManifestTests"/> covers the same manifest properties on the
/// two-bundle layered path, which never had this gap — its callers pass the app root as the
/// source folder too.</para>
///
/// <para>Three tests. Two positives that were RED before the fix, one per manifest property
/// reaching this path (a help URL, whose absence is a diagnostic; and a preprocessor symbol,
/// whose absence silently removes a procedure from the symbols the dependent binds against),
/// and one negative guard: an app root that GENUINELY omits <c>contextSensitiveHelpUrl</c>
/// must still fail AL0543, so the fix cannot be "stop checking".</para>
///
/// <para>Spawns the real runner; needs the BC artifact cache. Skips (no-op) when absent.</para>
/// </summary>
public class SiblingSymbolsAppRootManifestTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private readonly List<string> _roots = new();

    public void Dispose()
    {
        foreach (var r in _roots)
            try { Directory.Delete(r, recursive: true); } catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// A fresh bundle root. The directory's own NAME is unique because
    /// <c>PrepareSiblingSymbolsDir</c> keys the published-symbols temp dir on
    /// <c>Path.GetFileName(bundleAbs)</c> and clears it — two runs sharing a bundle folder
    /// name would delete each other's symbols.
    /// </summary>
    private string NewBundleRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "al-runner-sibling-approot-manifest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        return root;
    }

    private static (string output, int exit) RunRunner(params string[] bundles)
    {
        var args = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
        args.Append(TestBuildConfig.BcVersionArg);
        foreach (var b in bundles) args.Append(" \"").Append(b).Append('"');
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet", Arguments = args.ToString(),
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepoRoot,
        };
        var sb = new StringBuilder();
        var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(180_000)) { try { p.Kill(true); } catch { } throw new TimeoutException("runner hung"); }
        p.WaitForExit();
        lock (sb) return (sb.ToString(), p.ExitCode);
    }

    /// <summary>
    /// Writes one app in the app-root-plus-<c>src/</c> layout: app.json at the root, every
    /// .al under <c>src/</c>. This is the whole point of the fixture — a flat app.json would
    /// land in the source folder the old lookup scanned and the gap would not reproduce.
    /// </summary>
    private static void WriteApp(string dir, string appJson, params (string Name, string Al)[] sources)
    {
        var src = Path.Combine(dir, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(dir, "app.json"), appJson);
        Assert.False(File.Exists(Path.Combine(src, "app.json")),
            "the fixture's manifest must sit at the app root, never in src/");
        foreach (var (name, al) in sources)
            File.WriteAllText(Path.Combine(src, name), al);
    }

    private static string Manifest(
        string id, string name, int idFrom,
        string? contextSensitiveHelpUrl = null,
        string[]? preprocessorSymbols = null,
        (string Id, string Name)? dependsOn = null)
    {
        var helpUrlLine = contextSensitiveHelpUrl == null
            ? ""
            : $"\n  \"contextSensitiveHelpUrl\": \"{contextSensitiveHelpUrl}\",";
        var symbolsLine = preprocessorSymbols == null
            ? ""
            : $"\n  \"preprocessorSymbols\": [ {string.Join(", ", preprocessorSymbols.Select(s => $"\"{s}\""))} ],";
        var dependency = dependsOn == null
            ? ""
            : $"{{ \"id\": \"{dependsOn.Value.Id}\", \"name\": \"{dependsOn.Value.Name}\", "
              + "\"publisher\": \"AL Runner\", \"version\": \"1.0.0.0\" }";
        return $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "publisher": "AL Runner",
          "version": "1.0.0.0",{{helpUrlLine}}{{symbolsLine}}
          "dependencies": [ {{dependency}} ],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": {{idFrom}}, "to": {{idFrom + 19}} } ],
          "runtime": "14.0"
        }
        """;
    }

    /// <summary>A page that is only legal when the manifest sets contextSensitiveHelpUrl.</summary>
    private static (string, string) HelpAwarePage(int pageId) => ("HelpAware.Page.al", $$"""
        page {{pageId}} "SSM Help Aware Page"
        {
            PageType = Card;
            ContextSensitiveHelpPage = 'sales-invoice';

            layout
            {
                area(Content)
                {
                    field(Dummy; DummyValue) { ApplicationArea = All; Caption = 'Dummy'; }
                }
            }

            var
                DummyValue: Text[30];
        }
        """);

    private static (string, string) AnswerCodeunit(int codeunitId) => ("Answer.Codeunit.al", $$"""
        codeunit {{codeunitId}} "SSM Answer"
        {
            procedure Answer(): Integer
            begin
                exit(42);
            end;
        }
        """);

    private static (string, string) TestCodeunit(int codeunitId, string call, int expected) =>
        ("Tests.Codeunit.al", $$"""
        codeunit {{codeunitId}} "SSM Tests"
        {
            Subtype = Test;

            [Test]
            procedure SiblingProcedure_ReturnsExpected()
            var
                Lib: Codeunit "SSM Answer";
                Actual: Integer;
            begin
                Actual := Lib.{{call}};
                if Actual <> {{expected}} then
                    Error('Expected {{expected}} but got %1', Actual);
            end;
        }
        """);

    // ── Positive 1: contextSensitiveHelpUrl off the app root ─────────────────────────────

    [SkippableFact]
    public void AppRootManifestSetsHelpUrl_SiblingSymbolCompileHonoursIt_DependentTestRuns()
    {
        TestArtifacts.SkipIfMissing();

        var root = NewBundleRoot();
        var libId = "5b110000-0000-4000-8000-0000000000a1";
        var mainId = "5b110000-0000-4000-8000-0000000000a2";

        WriteApp(Path.Combine(root, "lib"),
            Manifest(libId, "SSM Pos Lib", 61120, contextSensitiveHelpUrl: "https://example.com/docs/"),
            HelpAwarePage(61120), AnswerCodeunit(61121));
        WriteApp(Path.Combine(root, "main"),
            Manifest(mainId, "SSM Pos Main", 61140, dependsOn: (libId, "SSM Pos Lib")),
            TestCodeunit(61140, "Answer()", 42));

        var (output, exit) = RunRunner(root);

        // Precondition: ONE bundle holding two apps, so the sibling-symbols path ran — not
        // the two-bundle layered pre-pass, which resolves the manifest a different way.
        Assert.Contains("1 bundle(s)", output);
        Assert.DoesNotContain("[layered]", output);

        Assert.DoesNotContain("AL0543", output);
        Assert.DoesNotContain("[sibling-symbols]", output);
        Assert.DoesNotContain("EMIT-ZERO", output);
        Assert.DoesNotContain("EMIT-EXCLUDED", output);
        Assert.DoesNotContain("Unhandled exception", output);
        Assert.True(exit == 0 && output.Contains("1P/0F/0E"),
            "an app whose OWN app.json sets contextSensitiveHelpUrl must compile to symbols its "
            + $"sibling can bind against, whatever layout it keeps its AL in (exit {exit}):\n{output}");
    }

    // ── Positive 2: preprocessorSymbols off the app root ─────────────────────────────────
    //
    // A different manifest property, and a different failure mode: nothing is diagnosed in
    // the library at all. The symbol-only compile simply parses the #if branch out, so the
    // procedure is missing from the symbols the dependent binds against — while the library's
    // own runtime DLL (emitted by Emit, which always honoured the app root) still has it.
    // That asymmetry is what makes this the sharper of the two positives: pre-fix the gap is
    // invisible in the library's own output and only surfaces one app downstream.

    [SkippableFact]
    public void AppRootManifestSetsPreprocessorSymbols_SiblingSymbolCompileHonoursThem_DependentBindsGatedProcedure()
    {
        TestArtifacts.SkipIfMissing();

        var root = NewBundleRoot();
        var libId = "5b110000-0000-4000-8000-0000000000b1";
        var mainId = "5b110000-0000-4000-8000-0000000000b2";

        WriteApp(Path.Combine(root, "lib"),
            Manifest(libId, "SSM Sym Lib", 61160, preprocessorSymbols: new[] { "SSM_LIB_FEATURE" }),
            ("Answer.Codeunit.al", """
            codeunit 61160 "SSM Answer"
            {
            #if SSM_LIB_FEATURE
                procedure GatedAnswer(): Integer
                begin
                    exit(7);
                end;
            #endif
            }
            """));
        WriteApp(Path.Combine(root, "main"),
            Manifest(mainId, "SSM Sym Main", 61180, dependsOn: (libId, "SSM Sym Lib")),
            TestCodeunit(61180, "GatedAnswer()", 7));

        var (output, exit) = RunRunner(root);

        Assert.Contains("1 bundle(s)", output);
        Assert.DoesNotContain("[layered]", output);
        Assert.DoesNotContain("[sibling-symbols]", output);
        // The pre-fix diagnostic, verbatim: "'Codeunit "SSM Answer"' does not contain a
        // definition for 'GatedAnswer'" — the symbols were written, just without the member.
        Assert.DoesNotContain("AL0132", output);
        Assert.DoesNotContain("EMIT-ZERO", output);
        Assert.DoesNotContain("EMIT-EXCLUDED", output);
        Assert.True(exit == 0 && output.Contains("1P/0F/0E"),
            "a procedure the library's own manifest symbol compiles IN must be present in the "
            + $"symbols its sibling binds against (exit {exit}):\n{output}");
    }

    // ── Negative: a manifest that genuinely omits the property is still invalid ──────────

    [SkippableFact]
    public void AppRootManifestOmitsHelpUrl_SiblingSymbolCompileStillFailsAL0543_DependentLosesTheType()
    {
        TestArtifacts.SkipIfMissing();

        var root = NewBundleRoot();
        var libId = "5b110000-0000-4000-8000-0000000000c1";
        var mainId = "5b110000-0000-4000-8000-0000000000c2";

        // Same layout as the positive, and the app root's manifest IS found now — it just
        // does not set the property the page requires. BC rejects that manifest too, so the
        // fix must not turn a real manifest error into a clean compile.
        WriteApp(Path.Combine(root, "lib"),
            Manifest(libId, "SSM Neg Lib", 61200, contextSensitiveHelpUrl: null),
            HelpAwarePage(61200), AnswerCodeunit(61201));
        WriteApp(Path.Combine(root, "main"),
            Manifest(mainId, "SSM Neg Main", 61220, dependsOn: (libId, "SSM Neg Lib")),
            TestCodeunit(61220, "Answer()", 42));

        var (output, exit) = RunRunner(root);

        Assert.Contains("1 bundle(s)", output);
        // Reported through the documented sibling-symbols channel, naming the property —
        // never as a raw CLR crash (the failure mode #1898 fixed for the layered path).
        Assert.Contains("[sibling-symbols]", output);
        Assert.Contains("AL0543", output);
        Assert.Contains("contextSensitiveHelpUrl", output);
        Assert.DoesNotContain("Unhandled exception", output);
        // And the consequence is visible one app downstream: symbols were never written, so
        // the dependent cannot bind the sibling's type at all. EMIT-ZERO rather than
        // EMIT-EXCLUDED because the test codeunit is this app's only object — the whole
        // module drops, not part of it.
        Assert.Contains("AL0185", output);
        Assert.Contains("Codeunit 'SSM Answer' is missing", output);
        Assert.Contains("COMPILE FAIL", output);
        Assert.Equal(3, exit);
    }
}
