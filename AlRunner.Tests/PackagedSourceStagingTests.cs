using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class PackagedSourceStagingTests : IDisposable
{
    private readonly BcEngineFixture _engine;
    private readonly string _root;
    private readonly string _appPath;
    private readonly string _stageRoot;

    public PackagedSourceStagingTests(BcEngineFixture engine)
    {
        _engine = engine;
        _root = Path.Combine(Path.GetTempPath(), "al-runner-packaged-source-tests", Guid.NewGuid().ToString("N"));
        _appPath = Path.Combine(_root, "Packaged Source.app");
        _stageRoot = Path.Combine(_root, "stage");
        Directory.CreateDirectory(_root);
        WritePackage();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void StageSourcePackage_PreservesSourceResourcesAndCompilerManifest()
    {
        var sourceCount = AppLoader.StageSourcePackage(_appPath, _stageRoot);

        Assert.Equal(2, sourceCount);
        Assert.True(File.Exists(Path.Combine(_stageRoot, "src", "Feature Area", "Packaged.Codeunit.al")));
        Assert.Equal(
            "function PackagedWidget() { return true; }",
            File.ReadAllText(Path.Combine(_stageRoot, "src", "Feature Area", "widget.js")));
        Assert.Equal(
            "report-layout",
            File.ReadAllText(Path.Combine(_stageRoot, "src", "Feature Area", "sample.rdlc")));

        using var appJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(_stageRoot, "app.json")));
        var root = appJson.RootElement;
        Assert.Equal("https://docs.example.test/", root.GetProperty("contextSensitiveHelpUrl").GetString());
        Assert.Contains(root.GetProperty("preprocessorSymbols").EnumerateArray(), e => e.GetString() == "BC28");
        Assert.Contains(root.GetProperty("features").EnumerateArray(), e => e.GetString() == "NOIMPLICITWITH");
        var visibleTo = Assert.Single(root.GetProperty("internalsVisibleTo").EnumerateArray());
        Assert.Equal("Packaged Source Tests", visibleTo.GetProperty("name").GetString());
    }

    [SkippableFact]
    public void StagedPackage_CompilesWithItsDefineAndControlAddInResource()
    {
        TestArtifacts.SkipIf(!_engine.Ready,
            _engine.SkipReason ?? "the in-process BC engine is not ready (see BcEngineCollection).");
        AppLoader.StageSourcePackage(_appPath, _stageRoot);

        using (BcCompiler.ScopeCurrentAppIdentity(
                   Guid.Parse("11111111-1111-1111-1111-111111111111"), "Contoso", new Version(1, 0, 0, 0)))
        {
            var output = new BcCompiler().Emit(new[] { _stageRoot }, "PackagedSource", _stageRoot);

            Assert.DoesNotContain(output.Diagnostics, d => d.Contains("AL0327"));
            Assert.DoesNotContain(output.Diagnostics, d => d.Contains("MissingSymbol"));
            Assert.Contains(output.Sources, s => s.Name == "Packaged Source Codeunit");
        }
    }

    private void WritePackage()
    {
        using var payload = new MemoryStream();
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "NavxManifest.xml", ManifestXml);
            AddEntry(zip, "src/src/Feature%2520Area/Packaged.Codeunit.al", CodeunitSource);
            AddEntry(zip, "src/src/Feature%2520Area/Packaged.ControlAddIn.al", ControlAddInSource);
            AddEntry(zip, "addin/src/src/Feature%2520Area/widget.js", "function PackagedWidget() { return true; }");
            AddEntry(zip, "layout/src/Feature%2520Area/sample.rdlc", "report-layout");
        }

        using var file = File.Create(_appPath);
        file.Write(Encoding.ASCII.GetBytes("NAVX"));
        file.Write(BitConverter.GetBytes(8u));
        payload.Position = 0;
        payload.CopyTo(file);
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private const string ManifestXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/navx/2015/manifest">
          <App Id="11111111-1111-1111-1111-111111111111" Name="Packaged Source" Publisher="Contoso"
               Version="1.0.0.0" Platform="28.0.0.0" Application="28.1.0.0" Runtime="17.0"
               Target="Cloud" ContextSensitiveHelpUrl="https://docs.example.test/" />
          <Dependencies />
          <InternalsVisibleTo>
            <Module Id="22222222-2222-2222-2222-222222222222" Name="Packaged Source Tests" Publisher="Contoso" />
          </InternalsVisibleTo>
          <Features><Feature>NOIMPLICITWITH</Feature></Features>
          <PreprocessorSymbols><PreprocessorSymbol Name="BC28" /></PreprocessorSymbols>
        </Package>
        """;

    private const string CodeunitSource = """
        #if BC28
        codeunit 50100 "Packaged Source Codeunit"
        {
            procedure Value(): Integer
            begin
                exit(42);
            end;
        }
        #else
        codeunit 50100 "Packaged Source Codeunit"
        {
            procedure Value(): Integer
            begin
                exit(MissingSymbol);
            end;
        }
        #endif
        """;

    private const string ControlAddInSource = """
        controladdin "Packaged Widget"
        {
            Scripts = 'src/Feature Area/widget.js';
            RequestedHeight = 100;
            RequestedWidth = 200;

            procedure Refresh();
        }
        """;
}
