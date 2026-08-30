using System.IO.Compression;
using System.Text;
using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RecordPatchesLateCalcFormulaRefreshTests : IDisposable
{
    private readonly BcEngineFixture _engine;
    private readonly string _root;

    public RecordPatchesLateCalcFormulaRefreshTests(BcEngineFixture engine)
    {
        _engine = engine;
        _root = Path.Combine(Path.GetTempPath(), "al-runner-late-calcformula-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [SkippableFact]
    public void LateDependencySymbols_RefreshPreviouslyUnresolvedCalcFormula()
    {
        TestArtifacts.SkipIf(!_engine.Ready,
            _engine.SkipReason ?? "the in-process BC engine is not ready (see BcEngineCollection).");

        const int parentTableId = 94910;
        const int sourceTableId = 94911;

        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "Parent.al"), $$"""
            table {{parentTableId}} "Late Formula Parent"
            {
                fields
                {
                    field(1; "No."; Code[20]) { }
                    field(10; Total; Decimal)
                    {
                        FieldClass = FlowField;
                        CalcFormula = sum("Late Formula Source".Amount where("No." = field("No.")));
                    }
                }
                keys
                {
                    key(PK; "No.") { Clustered = true; }
                }
            }
            """);
        RecordPatches.AddSourceDir(sourceDir);

        var skeleton = AlRunner.BcRuntime.SkeletonNCLMetadata;
        Assert.NotNull(skeleton);

        var before = RecordPatches.NCLMetadata_GetMetaTableById(skeleton!, parentTableId, false, 0);
        var beforeFormula = before.GetFieldByNo(10, trapError: true).CalculationFormula;
        Assert.Equal(0, beforeFormula.TableId);

        var appPath = Path.Combine(_root, "late-dependency.app");
        WriteApp(appPath, $$"""
            {
              "RuntimeVersion": "15.1",
              "Namespaces": [],
              "Tables": [
                {
                  "Id": {{sourceTableId}},
                  "Name": "Late Formula Source",
                  "Fields": [
                    { "TypeDefinition": { "Name": "Code[20]" }, "Properties": [], "Id": 1, "Name": "No." },
                    { "TypeDefinition": { "Name": "Decimal" }, "Properties": [], "Id": 2, "Name": "Amount" }
                  ],
                  "Keys": [
                    { "Name": "PK", "FieldNames": [ "No." ] }
                  ]
                }
              ]
            }
            """);
        RecordPatches.AddBcAppPath(appPath);

        RecordPatches.RefreshUnresolvedCalcFormulaTables();

        var after = RecordPatches.EnsureTableInMetadataCache(parentTableId);
        Assert.NotNull(after);
        Assert.NotSame(before, after);
        var afterFormula = after!.GetFieldByNo(10, trapError: true).CalculationFormula;
        Assert.Equal(sourceTableId, afterFormula.TableId);
        Assert.Equal(2, afterFormula.FieldId);
    }

    private static void WriteApp(string path, string symbolReferenceJson)
    {
        using var zip = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("SymbolReference.json");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(symbolReferenceJson);
    }
}
