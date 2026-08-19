// RecordPatchesIncrementalReparseTests — a warm --watch cycle must not re-parse the whole
// AL source tree to service an edit to one file.
//
// THE COST THIS PINS
//   Every save re-enters Program.cs's per-bundle loop, which calls
//   BcRuntime.ResetForNewBundleReload -> RecordPatches.ResetForReload: that clears _sourceDirs
//   and every parsed dictionary, so the following AddSourceDirs re-reads and re-parses EVERY
//   .al file in the bundle. Measured on a 7,339-file corpus (.context/perf/w4-servergc), the
//   register-source-dirs stage was 1.40-2.85 s of a 6.0 s median warm cycle — 29%, the largest
//   single line item, and 5-10x the AL delta emit (0.14-0.79 s) it exists to serve. The cost is
//   O(whole tree) no matter how many files moved.
//
// WHAT THE FIX IS, AND WHY THESE TESTS ARE SHAPED THIS WAY
//   Per-file extraction results are memoized on (path, content hash, preprocessor symbols) and
//   REPLAYED into the freshly-cleared dictionaries in enumeration order, rather than re-derived.
//   Replay rather than retraction is the whole point: _parsedExtensionFields and
//   _extensionIdsByBaseTable ACCUMULATE across files (the second is ordered, and that order is
//   AL declaration order, which drives record-trigger dispatch), so one file's contribution
//   cannot be subtracted — but rebuilding from empty in the same order reproduces it exactly.
//
//   So every test here asserts BOTH halves:
//     * MECHANISM — RecordPatches.ParseObjectTextCallCount, a COUNT of real tree builds and
//       never a duration (the instrument #1903 established).
//     * CONTENT — that the parsed state is genuinely correct afterwards, through the same
//       accessors AL code reaches at runtime. Without this, an implementation that simply
//       skipped the whole stage would pass every count assertion.
//
// COLLECTION: RecordPatchesSerialCollection, not BcEngineCollection. This class calls
// RecordPatches.ResetForReload, which wipes process-wide parse state that a concurrently
// running class could be mid-read on — exactly what ParserStaticsIsolationGuardTests exists to
// catch. The BC engine is still available: BcEngineBootstrap runs at [ModuleInitializer], i.e.
// once per assembly load, so RecordPatches.Register() (and hence AddSourceDirs' parse-on-
// register branch) is live in every collection, not just BcEngineCollection's.
using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

[Collection(RecordPatchesSerialCollection.Name)]
public sealed class RecordPatchesIncrementalReparseTests : IDisposable
{
    private readonly string _root;

    public RecordPatchesIncrementalReparseTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "al-runner-incremental-reparse-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private static void RequireEngine() => TestArtifacts.SkipIf(
        !BcEngineBootstrap.Ready,
        BcEngineBootstrap.SkipReason ?? "the in-process BC engine is not ready (see BcEngineCollection).");

    private string NewDir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string dir, string file, string al) =>
        File.WriteAllText(Path.Combine(dir, file), al);

    /// <summary>A table plus a page over it, so one file exercises more than one extractor and
    /// its result is observable through <see cref="RecordPatches.GetSourceTableIdForPage"/> —
    /// which reads the parsed page AND the parsed table, with no BC-side cache in the path that
    /// could answer for a dictionary the replay failed to rebuild.</summary>
    private static string TableAndPage(int id, string name, string? sourceTable = null) => $$"""
        table {{id}} "{{name}}"
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

        page {{id}} "{{name}} Card"
        {
            SourceTable = "{{sourceTable ?? name}}";
        }
        """;

    /// <summary>The warm-cycle boundary: what Program.cs does at the top of every --watch
    /// iteration before re-registering the same source dirs.</summary>
    private static void BeginWarmCycle() => RecordPatches.ResetForReload();

    private static int Parses(Action body)
    {
        var before = RecordPatches.ParseObjectTextCallCount;
        body();
        return RecordPatches.ParseObjectTextCallCount - before;
    }

    // ── the core claim ────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void SecondRegistrationOfUnchangedFiles_CostsNoRealParses_AndStillRebuildsTheState()
    {
        RequireEngine();

        var dir = NewDir("unchanged");
        var ids = new[] { 94101, 94102, 94103, 94104, 94105 };
        foreach (var id in ids) Write(dir, $"T{id}.al", TableAndPage(id, $"Inc Unchanged {id}"));

        BeginWarmCycle();
        var cold = Parses(() => RecordPatches.AddSourceDirs(new[] { dir }));
        Assert.Equal(ids.Length, cold);
        foreach (var id in ids) Assert.Equal(id, RecordPatches.GetSourceTableIdForPage(id));

        // The warm cycle: same dirs, same bytes, dictionaries cleared underneath.
        BeginWarmCycle();
        var warm = Parses(() => RecordPatches.AddSourceDirs(new[] { dir }));

        // MECHANISM: not one tree is rebuilt for a tree that did not move.
        Assert.Equal(0, warm);

        // CONTENT: and the cleared dictionaries really were repopulated. An implementation that
        // simply returned early — never replaying anything — passes the line above and fails
        // every line below.
        foreach (var id in ids) Assert.Equal(id, RecordPatches.GetSourceTableIdForPage(id));
    }

    [SkippableFact]
    public void EditingOneFile_ReparsesOnlyThatFile_AndServesTheEditedContent()
    {
        RequireEngine();

        var dir = NewDir("one-edit");
        var ids = new[] { 94111, 94112, 94113, 94114, 94115 };
        foreach (var id in ids) Write(dir, $"T{id}.al", TableAndPage(id, $"Inc Edit {id}"));

        BeginWarmCycle();
        RecordPatches.AddSourceDirs(new[] { dir });
        Assert.Equal(94111, RecordPatches.GetSourceTableIdForPage(94111));

        // Repoint page 94111 at a DIFFERENT table declared by a different file, so the edit is
        // observable as a value change rather than merely "still parses".
        Write(dir, "T94111.al", TableAndPage(94111, "Inc Edit 94111", sourceTable: "Inc Edit 94112"));

        BeginWarmCycle();
        var warm = Parses(() => RecordPatches.AddSourceDirs(new[] { dir }));

        // MECHANISM: exactly the one file whose bytes moved.
        Assert.Equal(1, warm);

        // CONTENT: the edit is live...
        Assert.Equal(94112, RecordPatches.GetSourceTableIdForPage(94111));
        // ...and the four files that did not move are still fully parsed, from the memo.
        foreach (var id in new[] { 94112, 94113, 94114, 94115 })
            Assert.Equal(id, RecordPatches.GetSourceTableIdForPage(id));
    }

    [SkippableFact]
    public void TouchingAFileWithIdenticalBytes_CostsNoRealParses()
    {
        RequireEngine();

        var dir = NewDir("touch");
        var al = TableAndPage(94121, "Inc Touch");
        Write(dir, "T94121.al", al);

        BeginWarmCycle();
        RecordPatches.AddSourceDirs(new[] { dir });

        // A git checkout, a formatter no-op or an editor autosave rewrites the same bytes with a
        // new mtime. The rest of the runner already decides recompiles on CONTENT for exactly
        // this reason (RadWorkspace.HashSourceTree); the memo must agree, or a branch switch
        // re-parses the whole tree for no edit at all.
        File.SetLastWriteTimeUtc(Path.Combine(dir, "T94121.al"), DateTime.UtcNow.AddMinutes(5));
        Write(dir, "T94121.al", al);

        BeginWarmCycle();
        var warm = Parses(() => RecordPatches.AddSourceDirs(new[] { dir }));

        Assert.Equal(0, warm);
        Assert.Equal(94121, RecordPatches.GetSourceTableIdForPage(94121));
    }

    // ── the two negatives that kill the cheaper "just never clear the dictionaries" design ────

    [SkippableFact]
    public void AnObjectDeletedFromAnEditedFile_DisappearsFromTheParsedState()
    {
        RequireEngine();

        var dir = NewDir("object-dropped");
        Write(dir, "Pair.al",
            TableAndPage(94131, "Inc Dropped Keeper") + "\n\n" + TableAndPage(94132, "Inc Dropped Goner"));

        BeginWarmCycle();
        RecordPatches.AddSourceDirs(new[] { dir });
        Assert.True(RecordPatches.IsPageParsed(94132));

        // The second object is deleted from the file.
        Write(dir, "Pair.al", TableAndPage(94131, "Inc Dropped Keeper"));

        BeginWarmCycle();
        var warm = Parses(() => RecordPatches.AddSourceDirs(new[] { dir }));

        Assert.Equal(1, warm);
        // The survivor is still there...
        Assert.True(RecordPatches.IsPageParsed(94131));
        // ...and the deleted object is GONE. Keeping the dictionaries across cycles and merely
        // overwriting the changed file's keys would leave 94132 answering for an object the
        // source no longer declares.
        Assert.False(RecordPatches.IsPageParsed(94132));
    }

    [SkippableFact]
    public void ADeletedFilesObjects_DisappearFromTheParsedState()
    {
        RequireEngine();

        var dir = NewDir("file-deleted");
        Write(dir, "Keep.al", TableAndPage(94141, "Inc Deleted Keeper"));
        Write(dir, "Gone.al", TableAndPage(94142, "Inc Deleted Goner"));

        BeginWarmCycle();
        RecordPatches.AddSourceDirs(new[] { dir });
        Assert.True(RecordPatches.IsPageParsed(94142));

        File.Delete(Path.Combine(dir, "Gone.al"));

        BeginWarmCycle();
        var warm = Parses(() => RecordPatches.AddSourceDirs(new[] { dir }));

        // Nothing to re-parse: the surviving file did not move and the other one is gone.
        Assert.Equal(0, warm);
        Assert.True(RecordPatches.IsPageParsed(94141));
        Assert.False(RecordPatches.IsPageParsed(94142));
    }

    // ── the accumulating dictionaries, which are why this is replay and not retraction ────────

    [SkippableFact]
    public void TableExtensionRegistrationOrderAndFields_SurviveRepeatedWarmCyclesUnchanged()
    {
        RequireEngine();

        var dir = NewDir("tableext");
        Write(dir, "Base.al", $$"""
            table 94151 "Inc Ext Base"
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
        Write(dir, "ExtA.al", """
            tableextension 94152 "Inc Ext A" extends "Inc Ext Base"
            {
                fields
                {
                    field(50100; "Inc Ext A Field"; Code[10]) { }
                }
            }
            """);
        Write(dir, "ExtB.al", """
            tableextension 94153 "Inc Ext B" extends "Inc Ext Base"
            {
                fields
                {
                    field(50200; "Inc Ext B Field"; Code[10]) { }
                }
            }
            """);

        // Read straight off the internal registry: this IS the ordered accumulate whose order
        // the fix has to preserve, and there is no accessor that exposes it.
        static int[] ExtensionIds(string baseTable) =>
            RecordPatches._extensionIdsByBaseTable.TryGetValue(baseTable, out var ids)
                ? ids.ToArray() : Array.Empty<int>();

        const string key = "inc ext base";

        BeginWarmCycle();
        RecordPatches.AddSourceDirs(new[] { dir });
        var coldOrder = ExtensionIds(key);
        Assert.Equal(2, coldOrder.Length);
        Assert.Equal(new[] { 94152, 94153 }, coldOrder.OrderBy(x => x).ToArray());

        for (var cycle = 2; cycle <= 3; cycle++)
        {
            BeginWarmCycle();
            var warm = Parses(() => RecordPatches.AddSourceDirs(new[] { dir }));
            Assert.Equal(0, warm);

            // ORDER: identical to the cold pass, entry for entry. Not asserted against a
            // hard-coded sequence — Directory.GetFiles order is the filesystem's — the claim
            // is that replay reproduces whatever the cold pass produced.
            Assert.Equal(coldOrder, ExtensionIds(key));

            // FIELDS: both extension fields, once each. Duplication is the failure mode a
            // corrupted accumulate produces, and it is fatal rather than cosmetic — a repeated
            // NCLMetaField corrupts NCLMetaTable.AssignFromMetaTable's positional field-count
            // arithmetic.
            Assert.Equal(
                new[] { 50100, 50200 },
                RecordPatches.ExtensionFieldsForBaseTable("Inc Ext Base")
                    .Select(f => f.FieldId).OrderBy(id => id).ToArray());

            // And they still resolve through the runtime path AL itself uses, on a metatable
            // rebuilt from the replayed state.
            var table = RecordPatches.NCLMetadata_GetMetaTableById(
                AlRunner.BcRuntime.SkeletonNCLMetadata!, 94151, false, 0);
            Assert.Equal(50100, RecordPatches.NCLMetaTable_GetFieldByNoExt(table, 94152, 50100).FieldNo);
            Assert.Equal(50200, RecordPatches.NCLMetaTable_GetFieldByNoExt(table, 94153, 50200).FieldNo);
        }

        // Now delete the extension that was applied SECOND. This is the shape that exposes the
        // aliasing hazard, and the reason ApplyTableExtensions copies the field list rather
        // than publishing the extract's own.
        //
        // The list stored under the base-table key is appended to IN PLACE by every later
        // contributor — the next tableextension over the same base table, and
        // EnsureBcSymbolExtensionIndex's precompiled merge. So publishing an extract's own list
        // lets the SECOND extension's field land inside the FIRST one's memo entry,
        // permanently. Field-id dedup hides that for as long as both files exist (the merged
        // set is the same either way); only removing the appender can show it, and it has to be
        // the appender — remove the publisher instead and the survivor's entry was never
        // touched. Which is which is decided by Directory.GetFiles order, i.e. by the
        // filesystem, so it is read off the observed order rather than assumed.
        var appliedFirst = coldOrder[0];
        var appliedSecond = coldOrder[1];
        File.Delete(Path.Combine(dir, appliedSecond == 94152 ? "ExtA.al" : "ExtB.al"));

        BeginWarmCycle();
        Assert.Equal(0, Parses(() => RecordPatches.AddSourceDirs(new[] { dir })));

        // Only the survivor is registered, and it contributes exactly its own field — the
        // deleted extension's does not live on inside the survivor's memoized record.
        //
        // Asserted against the accumulate rather than the merged NCLMetaTable on purpose: BC's
        // skeleton metadata cache is deliberately NOT cleared by a reload (see
        // BcRuntime.ResetForNewBundleReload's remarks on table-SHAPE edits), so it can still
        // answer for a field the source has since dropped. That is a pre-existing, documented
        // reload limitation and not this memo's business; what the memo owns is the parse
        // state, and that is what this checks.
        Assert.Equal(new[] { appliedFirst }, ExtensionIds(key));
        Assert.Equal(
            new[] { appliedFirst == 94152 ? 50100 : 50200 },
            RecordPatches.ExtensionFieldsForBaseTable("Inc Ext Base")
                .Select(f => f.FieldId).ToArray());
    }

    // ── the #1900 guard: a memo keyed on content alone would silently freeze --define ─────────

    [SkippableFact]
    public void ChangingThePreprocessorSymbols_InvalidatesTheMemo()
    {
        RequireEngine();

        var dir = NewDir("symbols");
        Write(dir, "Gated.al", $$"""
            table 94161 "Inc Symbol Base"
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

            #if INC_EXTRA_PAGE
            page 94162 "Inc Symbol Extra"
            {
                SourceTable = "Inc Symbol Base";
            }
            #endif
            """);

        var restore = AlRunner.BcCompiler.GetExtraPreprocessorSymbols().ToArray();
        try
        {
            AlRunner.BcCompiler.SetExtraPreprocessorSymbols(Array.Empty<string>());
            BeginWarmCycle();
            RecordPatches.AddSourceDirs(new[] { dir });
            Assert.False(RecordPatches.IsPageParsed(94162));

            // Same bytes, different --define set. The parse is a pure function of
            // (text, symbols), so the symbols are half the key — memoizing on content alone
            // reintroduces #1900 through a different door: a stale answer for a genuinely
            // different parse.
            AlRunner.BcCompiler.SetExtraPreprocessorSymbols(new[] { "INC_EXTRA_PAGE" });
            BeginWarmCycle();
            var warm = Parses(() => RecordPatches.AddSourceDirs(new[] { dir }));

            Assert.Equal(1, warm);
            Assert.True(RecordPatches.IsPageParsed(94162));
        }
        finally
        {
            AlRunner.BcCompiler.SetExtraPreprocessorSymbols(restore);
        }
    }
}
