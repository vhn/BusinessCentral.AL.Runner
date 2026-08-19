// RecordPatchesSourceDirBatchOrderTests — the invariant that parallelising the parse must not
// break.
//
// `RecordPatches.AddSourceDir` re-reads and re-parses every .al file under each registered dir,
// which on npcore's 7,339 files is ~7s and 84% of a warm --watch cycle — against 0.4s for the
// delta compile the cycle exists to serve. #1903 removed the 8×-per-file waste and left the floor:
// one real `ParseObjectText` per file, at roughly 2 MB/s on one core. Reading the files is under 1s
// of that, so the rest is the parser, and the parser is the part that can use more than one core.
//
// So the files of one batch are parsed in parallel and the eight extractors then run over the
// results SERIALLY, in enumeration order. The serial half is not conservatism: the extractors
// write into shared dictionaries, and `_extensionIdsByBaseTable` holds tableextension ids in AL
// declaration order because that is the order BC registers them and the record-trigger pipeline
// preserves it. Reordering that reorders trigger dispatch — a bug no compile error would catch and
// no existing fixture would notice, because every fixture has one tableextension per table.
//
// This test is deliberately larger than the batch size, with its tableextensions placed on both
// sides of a batch boundary, because a batching scheme that got order right WITHIN a batch and
// wrong ACROSS batches is exactly the plausible mistake.

using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RecordPatchesSourceDirBatchOrderTests : IDisposable
{
    /// <summary>
    /// Comfortably more than the 256-file pre-parse batch, so the run crosses a boundary and takes
    /// the parallel path (which needs at least 32 files) rather than the inline one.
    /// </summary>
    private const int FileCount = 300;

    private const int BaseTableId = 93800;
    private const int FirstFillerTableId = 93801;

    /// <summary>Indices in the written order, chosen to straddle the boundary at 256.</summary>
    private static readonly int[] ExtensionAtIndex = [10, 150, 290];

    private readonly string _dir;
    private readonly BcEngineFixture _engine;

    public RecordPatchesSourceDirBatchOrderTests(BcEngineFixture engine)
    {
        _engine = engine;
        _dir = Path.Combine(
            Path.GetTempPath(), "al-runner-batch-order-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [SkippableFact]
    public void AddSourceDirs_AcrossABatchBoundary_KeepsExtensionOrderAndParsesEachFileOnce()
    {
        TestArtifacts.SkipIf(!_engine.Ready,
            _engine.SkipReason ?? "the in-process BC engine is not ready (see BcEngineCollection).");

        // File NAMES carry the index so the enumeration order below is deterministic and readable;
        // the AL inside them is what decides what gets registered.
        var extensionIdByFile = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < FileCount; i++)
        {
            var path = Path.Combine(_dir, $"Obj{i:D4}.al");
            var extensionSlot = Array.IndexOf(ExtensionAtIndex, i);
            if (extensionSlot >= 0)
            {
                var extensionId = 94000 + extensionSlot;
                extensionIdByFile[path] = extensionId;
                File.WriteAllText(path, $$"""
                    tableextension {{extensionId}} "Batch Order Ext {{extensionSlot}}" extends "Batch Order Base"
                    {
                        fields
                        {
                            field({{100 + extensionSlot}}; "Ext Field {{extensionSlot}}"; Integer) { }
                        }
                    }
                    """);
            }
            else
            {
                var tableId = i == 0 ? BaseTableId : FirstFillerTableId + i;
                var name = i == 0 ? "Batch Order Base" : $"Batch Order Filler {i}";
                File.WriteAllText(path, $$"""
                    table {{tableId}} "{{name}}"
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
            }
        }

        // The oracle is the enumeration the production code itself walks, not the write order —
        // `Directory.GetFiles` is not documented to be sorted, and "AL declaration order" has
        // always meant "the order this enumeration yields". Asserting against the write order would
        // pin a platform detail instead of the invariant.
        var expectedExtensionIds = Directory
            .GetFiles(_dir, "*.al", SearchOption.AllDirectories)
            .Where(extensionIdByFile.ContainsKey)
            .Select(path => extensionIdByFile[path])
            .ToArray();
        Assert.Equal(ExtensionAtIndex.Length, expectedExtensionIds.Length);

        var before = RecordPatches.ParseObjectTextCallCount;
        RecordPatches.AddSourceDirs([_dir]);
        var after = RecordPatches.ParseObjectTextCallCount;

        // #1903's invariant, now over a run that crosses batch boundaries and parses in parallel:
        // one real tree build per registered file, never eight, and never fewer — two files that
        // happen to hold identical text still cost two.
        Assert.Equal(FileCount, after - before);

        // The invariant this file exists for.
        Assert.True(
            RecordPatches._extensionIdsByBaseTable.TryGetValue("batch order base", out var actual),
            "the three tableextensions on the base table registered no extension ids at all");
        Assert.Equal(expectedExtensionIds, actual);

        // …and the batches really did all get extracted, rather than one of them being registered
        // and the rest silently dropped. Both ends of the range and the far side of the boundary.
        var skeleton = AlRunner.BcRuntime.SkeletonNCLMetadata;
        Assert.NotNull(skeleton);
        foreach (var id in new[] { BaseTableId, FirstFillerTableId + 1, FirstFillerTableId + 299 })
            Assert.Equal(id, RecordPatches.NCLMetadata_GetMetaTableById(skeleton!, id, false, 0).TableId);

        // A field contributed by an extension on the FAR side of the boundary reaches the merged
        // metatable — the extension ids being in the right order is not the same claim as the last
        // batch's extension having been merged at all. Field 102 is the one declared by the
        // extension at index 290, which lands in the second batch.
        var baseTable = RecordPatches.NCLMetadata_GetMetaTableById(skeleton!, BaseTableId, false, 0);
        Assert.True(
            baseTable.TryGetFieldByNo(102, out var farSideField) && farSideField != null,
            "the tableextension in the second batch did not contribute its field to the base table");
    }
}
