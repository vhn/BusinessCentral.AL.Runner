using AlRunner.Patches;
using System.Reflection;
using Xunit;

namespace AlRunner.Tests;

[Collection(RecordPatchesSerialCollection.Name)]
public sealed class SystemAppWatchReloadTests
{
    private const int RecordLinkTableId = 2000000068;
    private const int BundleSentinelTableId = 72499;

    [SkippableFact]
    public void Reload_RestoresMicrosoftSystemPackageTableShapes()
    {
        TestArtifacts.SkipIfMissing();

        RecordPatches.RegisterSystemAppPackage();
        var packageTables = GetSystemAppPackageTables();
        Assert.NotEmpty(packageTables);
        Assert.Contains(RecordPatches.TenantMediaTableId, packageTables.Keys);
        Assert.Contains(RecordLinkTableId, packageTables.Keys);

        var expectedFieldIds = packageTables.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Fields.Select(field => field.FieldId).Order().ToArray());
        Assert.Contains(10, expectedFieldIds[RecordPatches.TenantMediaTableId]);

        var parsedTables = GetParsedTables();
        parsedTables[BundleSentinelTableId] = new ParsedTable(
            BundleSentinelTableId,
            "Watch Bundle Sentinel",
            new List<ParsedField> { new(1, "Primary Key", "Integer", 0) },
            new List<int> { 1 });

        RecordPatches.ResetForReload();

        parsedTables = GetParsedTables();
        Assert.False(parsedTables.ContainsKey(BundleSentinelTableId));
        Assert.Equal(packageTables.Count, packageTables.Keys.Count(parsedTables.ContainsKey));
        foreach (var (id, fieldIds) in expectedFieldIds)
        {
            Assert.True(parsedTables.TryGetValue(id, out var restored),
                $"SystemPackage table {id} was not restored after watch reload.");
            Assert.Equal(fieldIds, restored!.Fields.Select(field => field.FieldId).Order());
        }
    }

    private static Dictionary<int, ParsedTable> GetParsedTables()
        => (Dictionary<int, ParsedTable>)typeof(RecordPatches)
            .GetField("_parsedTables", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    private static IReadOnlyDictionary<int, ParsedTable> GetSystemAppPackageTables()
        => (IReadOnlyDictionary<int, ParsedTable>)typeof(RecordPatches)
            .GetField("_systemAppPackageTables", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
}
