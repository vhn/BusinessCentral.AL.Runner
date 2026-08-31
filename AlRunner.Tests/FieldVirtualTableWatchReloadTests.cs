using System.Reflection;
using AlRunner.Patches;
using Microsoft.Dynamics.Nav.Runtime;
using Xunit;

namespace AlRunner.Tests;

[Collection(RecordPatchesSerialCollection.Name)]
public sealed class FieldVirtualTableWatchReloadTests : IDisposable
{
    public void Dispose()
    {
        if (BcEngineBootstrap.Ready)
            RecordPatches.ResetForReload();
    }

    [SkippableFact]
    public void Reload_ClearsVirtualBitOnTheRebuiltFieldMetaTable()
    {
        TestArtifacts.SkipIfMissing();
        RequireEngine();

        RecordPatches.RegisterSystemAppPackage();
        RecordPatches.ResetForReload();

        var first = RequireFieldMetaTable();
        Assert.True(HasVirtualBit(first));
        ClearVirtualBit(first);
        Assert.False(HasVirtualBit(first));

        RecordPatches.ResetForReload();

        var rebuilt = RequireFieldMetaTable();
        Assert.NotSame(first, rebuilt);
        Assert.True(HasVirtualBit(rebuilt));
        ClearVirtualBit(rebuilt);

        Assert.False(HasVirtualBit(rebuilt));
    }

    [SkippableFact]
    public void InstallBaseline_DoesNotCaptureSelfPopulatingMetadataRows()
    {
        TestArtifacts.SkipIfMissing();
        RequireEngine();

        RecordPatches.RegisterSystemAppPackage();
        RecordPatches.ResetForReload();

        InvokeTryParseObjectDeclFile("""codeunit 72498 "Baseline Codeunit Projection" { }""");
        var source = RecordPatches.ResolveSkeletonDataAccessSource();
        Assert.NotNull(source);

        Materialize(source, RecordPatches.FeatureKeySystemTableId);
        Materialize(source, RecordPatches.FieldVirtualTableId);
        Materialize(source, RecordPatches.CodeunitMetadataVirtualTableId);

        var snapshot = RecordPatches.CaptureInstallBaselineSnapshot();
        var tables = snapshot.Sources.SelectMany(baselineSource => baselineSource.Tables).ToArray();

        Assert.Contains(tables,
            table => table.TableId == RecordPatches.FeatureKeySystemTableId && table.Rows.Length > 0);
        Assert.DoesNotContain(tables,
            table => RecordPatches.IsSelfPopulatingVirtualTableId(table.TableId));
        Assert.DoesNotContain(tables,
            table => table.TableId == RecordPatches.CodeunitMetadataVirtualTableId);

        RecordPatches.RestoreInstallBaselineSnapshot(snapshot);
        Materialize(source, RecordPatches.FieldVirtualTableId);
        Materialize(source, RecordPatches.CodeunitMetadataVirtualTableId);

        var restored = RecordPatches.CaptureInstallBaselineSnapshot().Sources
            .SelectMany(baselineSource => baselineSource.Tables)
            .ToArray();
        Assert.Contains(restored,
            table => table.TableId == RecordPatches.FeatureKeySystemTableId && table.Rows.Length > 0);
        Assert.DoesNotContain(restored,
            table => RecordPatches.IsSelfPopulatingVirtualTableId(table.TableId));
        Assert.DoesNotContain(restored,
            table => table.TableId == RecordPatches.CodeunitMetadataVirtualTableId);
    }

    private static void InvokeTryParseObjectDeclFile(string source)
    {
        var method = typeof(RecordPatches).GetMethod(
            "TryParseObjectDeclFile", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("RecordPatches.TryParseObjectDeclFile was not found.");
        method.Invoke(null, new object[] { source });
    }

    private static void Materialize(object source, int tableId)
    {
        var metaTable = RecordPatches.EnsureTableInMetadataCache(tableId)
            ?? throw new InvalidOperationException($"Metatable {tableId} was not rebuilt after reload.");
        _ = RecordPatches.NavDataAccessSource_GetDataAccessForTable(source, metaTable, false);
    }

    private static void RequireEngine() => TestArtifacts.SkipIf(
        !BcEngineBootstrap.Ready,
        BcEngineBootstrap.SkipReason
        ?? "the in-process BC engine is not ready (see BcEngineCollection).");

    private static NCLMetaTable RequireFieldMetaTable()
        => RecordPatches.EnsureTableInMetadataCache(RecordPatches.FieldVirtualTableId)
           ?? throw new InvalidOperationException("Field metatable was not rebuilt after reload.");

    private static bool HasVirtualBit(NCLMetaTable metaTable)
    {
        var field = metaTable.GetType().GetField("tableTypes", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("NCLMetaTable.tableTypes was not found.");
        return (Convert.ToInt32(field.GetValue(metaTable)) & 0x8) != 0;
    }

    private static void ClearVirtualBit(NCLMetaTable metaTable)
    {
        var method = typeof(RecordPatches).GetMethod(
            "ClearVirtualBit", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("RecordPatches.ClearVirtualBit was not found.");
        method.Invoke(null, new object[] { metaTable });
    }
}
