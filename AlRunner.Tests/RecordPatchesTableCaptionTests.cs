using System.IO.Compression;
using System.Reflection;
using System.Text;
using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RecordPatchesTableCaptionTests : IDisposable
{
    private const int SourceCaptionTableId = 94670;
    private const int SourceNameFallbackTableId = 94671;
    private const int DependencyCaptionTableId = 94672;
    private const int SourceOnlyDependencyCaptionTableId = 94673;

    private readonly BcEngineFixture _engine;
    private readonly string _root;

    public RecordPatchesTableCaptionTests(BcEngineFixture engine)
    {
        _engine = engine;
        _root = Path.Combine(Path.GetTempPath(), "al-runner-table-caption-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [SkippableFact]
    public void BuildNCLMetaTable_PopulatesDeclaredTableCaptions()
    {
        TestArtifacts.SkipIf(!_engine.Ready,
            _engine.SkipReason ?? "the in-process BC engine is not ready (see BcEngineCollection).");

        var appPath = Path.Combine(_root, "caption-dependency.app");
        WriteApp(appPath, $$"""
            {
              "RuntimeVersion": "15.1",
              "Namespaces": [],
              "Tables": [
                {
                  "Id": {{SourceNameFallbackTableId}},
                  "Name": "Dependency Caption Collision",
                  "Properties": [ { "Name": "Caption", "Value": "Wrong Dependency Caption" } ],
                  "Fields": [
                    { "TypeDefinition": { "Name": "Code[20]" }, "Properties": [], "Id": 1, "Name": "No." }
                  ],
                  "Keys": [ { "Name": "PK", "FieldNames": [ "No." ] } ]
                },
                {
                  "Id": {{DependencyCaptionTableId}},
                  "Name": "Dependency Caption Probe",
                  "Properties": [ { "Name": "Caption", "Value": "Dependency Friendly Caption" } ],
                  "Fields": [
                    { "TypeDefinition": { "Name": "Code[20]" }, "Properties": [], "Id": 1, "Name": "No." }
                  ],
                  "Keys": [ { "Name": "PK", "FieldNames": [ "No." ] } ]
                }
              ]
            }
            """);
        RecordPatches.AddBcAppPath(appPath);

        var sourceOnlyAppPath = Path.Combine(_root, "source-only-caption-dependency.app");
        WriteSourceApp(sourceOnlyAppPath, $$"""
            table {{SourceOnlyDependencyCaptionTableId}} "Source Only Dependency Probe"
            {
                Caption = 'Source Only Dependency Caption';
                fields
                {
                    field(1; "No."; Code[20]) { }
                }
                keys
                {
                    key(PK; "No.") { Clustered = true; }
                }
            }
            """);
        RecordPatches.AddBcAppPath(sourceOnlyAppPath);

        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "CaptionTables.al"), $$"""
            table {{SourceCaptionTableId}} "Source Caption Probe"
            {
                Caption = 'Source Friendly Caption';
                fields
                {
                    field(1; "No."; Code[20]) { }
                }
                keys
                {
                    key(PK; "No.") { Clustered = true; }
                }
            }

            table {{SourceNameFallbackTableId}} "Source Name Fallback"
            {
                fields
                {
                    field(1; "No."; Code[20]) { }
                }
                keys
                {
                    key(PK; "No.") { Clustered = true; }
                }
            }
            """);
        RecordPatches.AddSourceDir(sourceDir);

        var skeleton = BcRuntime.SkeletonNCLMetadata;
        Assert.NotNull(skeleton);

        var sourceCaption = RecordPatches.NCLMetadata_GetMetaTableById(
            skeleton!, SourceCaptionTableId, false, 0);
        Assert.Equal("Source Friendly Caption", ReadTableCaption(sourceCaption));
        Assert.NotEqual(ReadTableName(sourceCaption), ReadTableCaption(sourceCaption));

        var sourceFallback = RecordPatches.NCLMetadata_GetMetaTableById(
            skeleton, SourceNameFallbackTableId, false, 0);
        Assert.Equal("Source Name Fallback", ReadTableCaption(sourceFallback));

        var dependencyCaption = RecordPatches.NCLMetadata_GetMetaTableById(
            skeleton, DependencyCaptionTableId, false, 0);
        Assert.Equal("Dependency Friendly Caption", ReadTableCaption(dependencyCaption));
        Assert.NotEqual(ReadTableName(dependencyCaption), ReadTableCaption(dependencyCaption));

        var sourceOnlyDependencyCaption = RecordPatches.NCLMetadata_GetMetaTableById(
            skeleton, SourceOnlyDependencyCaptionTableId, false, 0);
        Assert.Equal("Source Only Dependency Caption", ReadTableCaption(sourceOnlyDependencyCaption));
        Assert.NotEqual(ReadTableName(sourceOnlyDependencyCaption), ReadTableCaption(sourceOnlyDependencyCaption));
    }

    private static string ReadTableCaption(object metaTable) => ReadStringProperty(metaTable, "TableCaptionSafe");

    private static string ReadTableName(object metaTable) => ReadStringProperty(metaTable, "TableName");

    private static string ReadStringProperty(object metaTable, string propertyName)
    {
        var property = metaTable.GetType().GetProperty(
            propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"NCLMetaTable.{propertyName} not found — Ncl shape changed.");
        return (string)property.GetValue(metaTable)!;
    }

    private static void WriteApp(string path, string symbolReferenceJson)
    {
        using var stream = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("SymbolReference.json");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(symbolReferenceJson);
    }

    private static void WriteSourceApp(string path, string alSource)
    {
        using var stream = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("src/CaptionTable.al");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(alSource);
    }
}
