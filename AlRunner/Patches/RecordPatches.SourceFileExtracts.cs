// RecordPatches.SourceFileExtracts — makes registering a source dir cost the files that MOVED
// rather than every file in the tree.
//
// THE COST
//   RecordPatches is the runner's stand-in for BC's metadata service: with no service tier
//   there is no NCLMetadata to ask, so the AL source itself is the only description of table /
//   page / report / query / xmlport shape on disk, and the eight extractors read it.
//
//   In --watch, every save re-enters Program.cs's per-bundle loop, which calls
//   BcRuntime.ResetForNewBundleReload -> ResetForReload: that empties _sourceDirs and every
//   parsed dictionary, so the AddSourceDirs immediately after re-reads and re-parses the WHOLE
//   tree to service an edit to one file. Measured on a 7,339-file corpus
//   (.context/perf/w4-servergc, 9-cycle series, one-object deltas, Server GC), the
//   register-source-dirs stage was 1.40-2.85 s of a 6.0 s median warm cycle: the single
//   largest line item, and 5-10x the AL delta emit (0.14-0.79 s) it exists to serve. The parse
//   is essentially all of it — ~6 s of CPU per pass at ~2 MB/s/core, against reading (<1 s)
//   and the extractors' walk over an already-built tree (milliseconds).
//
// REPLAY, NOT RETRACTION
//   The tempting design — keep the dictionaries across cycles and patch only the changed
//   files — needs one file's contribution to be retractable, and it is not. Two things break
//   it: _extensionIdsByBaseTable is an ORDERED accumulate keyed by base-table name whose order
//   is AL declaration order (it drives record-trigger dispatch), and an object deleted from an
//   edited file would linger, answering for something the source no longer declares.
//
//   Replay sidesteps both. ResetForReload still clears everything exactly as before; what
//   changes is only where each file's contribution comes from. An unchanged file's records are
//   re-applied from this memo instead of re-derived, in the same enumeration order, so the
//   accumulate order and the dedup come out identical and a file that vanished is simply never
//   replayed. Nothing is ever subtracted, so nothing needs to be.
//
//   Affordable because the halves are so lopsided: extraction is ~all parse, and application is
//   a few dictionary writes. We pay the cheap half every cycle to avoid needing retraction at
//   all.
//
// THE KEY IS (path, content, preprocessor symbols)
//   Content and not mtime, matching the decision the rest of the runner already made
//   (RadWorkspace.HashSourceTree): a git checkout, a formatter no-op or an editor autosave
//   rewrites bytes identically, and keying on mtime would re-parse the whole tree for no edit.
//   Symbols because the parse is a pure function of (text, symbols) — #1900 was a parser that
//   silently stopped seeing --define symbols, and a memo keyed on content alone would
//   reintroduce it through a different door.
using NavCA = Microsoft.Dynamics.Nav.CodeAnalysis;

namespace AlRunner.Patches;

public static partial class RecordPatches
{
    /// <summary>
    /// Everything the eight extractors derive from ONE file's text, and nothing else. Purely a
    /// function of (text, preprocessor symbols) — no extractor reads runner state — which is
    /// what makes it safe to hold across a reload and replay.
    /// </summary>
    private sealed record AlSourceFileExtract(
        IReadOnlyList<ParsedTable> Tables,
        IReadOnlyList<ParsedTableExtension> TableExtensions,
        IReadOnlyList<ParsedPage> Pages,
        IReadOnlyList<ParsedPage> PageExtensions,
        IReadOnlyList<ParsedReport> Reports,
        IReadOnlyList<ParsedReport> ReportExtensions,
        IReadOnlyList<ParsedQuery> Queries,
        IReadOnlyList<ParsedXmlPort> XmlPorts,
        IReadOnlyList<ParsedAlObjectDecl> ObjectDecls,
        IReadOnlyList<ParsedAlObjectCaption> ObjectCaptions);

    private readonly record struct CachedExtract(string Hash, AlSourceFileExtract Extract);

    /// <summary>
    /// Per registered source dir, that dir's files and what each one extracted to.
    /// <para>Keyed by DIR and rebuilt per pass on purpose: each pass installs a fresh inner map
    /// holding exactly the files it enumerated, so a deleted file's entry is dropped without any
    /// pruning pass, and the memo cannot outgrow the source dirs actually being registered. A
    /// dir that stops being registered keeps its map until the process ends — bounded by the
    /// number of distinct dirs a run has ever seen, which is small even in --server.</para>
    /// <para>Deliberately NOT cleared by <see cref="ResetForReload"/>: surviving the reload is
    /// the entire point. Its own invalidation is the (content, symbols) key below.</para>
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, CachedExtract>> _extractsByDir =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The preprocessor symbol set every entry in <see cref="_extractsByDir"/> was
    /// extracted under. A different set makes every entry a different parse, not a stale one.</summary>
    private static string[] _extractCacheSymbols = Array.Empty<string>();

    /// <summary>
    /// Run all eight extractors over <paramref name="files"/> (every <c>.al</c> file under
    /// <paramref name="dir"/>) and publish the results, re-deriving only the files whose content
    /// moved since this dir was last registered.
    ///
    /// <para>The extractors stay SERIAL and in enumeration order, as before. They write into
    /// shared dictionaries, two of which accumulate — <c>_parsedExtensionFields</c> by
    /// base-table name, and <c>_extensionIdsByBaseTable</c>, whose lists are in AL declaration
    /// order because that is the order BC registers tableextensions and the record-trigger
    /// pipeline preserves it. Only the derivation is skipped; the publishing order is
    /// untouched.</para>
    /// </summary>
    private static void ParseSourceFilesIntoAllExtractors(string dir, IReadOnlyList<string> files)
    {
        var symbols = AlRunner.BcCompiler.GetExtraPreprocessorSymbols().ToArray();
        if (!symbols.AsSpan().SequenceEqual(_extractCacheSymbols))
        {
            // Not staleness — a genuinely different parse for every file. Drop the lot.
            _extractsByDir.Clear();
            _extractCacheSymbols = symbols;
        }

        _extractsByDir.TryGetValue(dir, out var previous);
        var current = new Dictionary<string, CachedExtract>(files.Count, StringComparer.Ordinal);
        _extractsByDir[dir] = current;

        // Reading every file to hash it REPLACES the serial read this loop used to do, in
        // parallel, and answers which files need a tree at all. Measured at 0.14 s for 7,053
        // files on the corpus above. HashSourceTree is a pure static helper with no RAD state;
        // it reports an unreadable file (a save in flight) as changed, which is the answer this
        // wants too — re-derive it rather than serve a memo entry for bytes we could not read.
        var hashes = AlRunner.Rad.RadWorkspace.HashSourceTree(files);

        for (int start = 0; start < files.Count; start += SourceFileBatchSize)
        {
            var batch = files.Skip(start).Take(SourceFileBatchSize).ToArray();

            // Null == "served from the memo": no read, and nothing for the pre-parse to build.
            var texts = new string?[batch.Length];
            for (int i = 0; i < batch.Length; i++)
            {
                if (previous != null
                    && previous.TryGetValue(batch[i], out var hit)
                    && hashes.TryGetValue(batch[i], out var hash)
                    && string.Equals(hit.Hash, hash, StringComparison.Ordinal))
                    continue;
                // Serial, so a file that cannot be read throws exactly the exception it always
                // threw rather than an AggregateException from the parallel phase.
                texts[i] = File.ReadAllText(batch[i]);
            }

            using (BeginPreParse(texts))
                for (int i = 0; i < batch.Length; i++)
                {
                    var path = batch[i];
                    var extract = texts[i] is { } text
                        ? ExtractSourceFile(text)
                        : previous![path].Extract;
                    // A file with no hash (HashSourceTree could not reach it at all) is kept out
                    // of the memo rather than cached under a fabricated key: next pass re-derives.
                    if (hashes.TryGetValue(path, out var hash))
                        current[path] = new CachedExtract(hash, extract);
                    ApplySourceFileExtract(extract);
                }
        }
    }

    /// <summary>
    /// Runs all eight extractors over ONE already-read file's text (#1903), deriving but not
    /// publishing. Each is a thin foreach over <see cref="ParseAlObjects"/>, which memoizes its
    /// most-recently-built tree keyed on (text, active preprocessor symbols) — so calling the
    /// eight back-to-back on the SAME text costs one real parse plus seven cache hits.
    /// </summary>
    private static AlSourceFileExtract ExtractSourceFile(string text)
    {
        var tables = ExtractTables(text);
        var tableExtensions = ExtractTableExtensions(text);
        var (pages, pageExtensions) = ExtractPages(text);
        var (reports, reportExtensions) = ExtractReports(text);
        var queries = ExtractQueries(text);
        var xmlPorts = ExtractXmlPorts(text);
        var objectDecls = ExtractObjectDecls(text);
        var objectCaptions = ExtractObjectCaptions(text);
        return new AlSourceFileExtract(
            tables, tableExtensions, pages, pageExtensions, reports, reportExtensions,
            queries, xmlPorts, objectDecls, objectCaptions);
    }

    /// <summary>
    /// Publishes one file's extract into the parsed dictionaries, in the same order the eight
    /// extractors used to run in. Tables before tableextensions is load-bearing:
    /// <c>ApplyTableExtensions</c> evicts a base metatable by looking the base table up in
    /// <c>_parsedTables</c>, and a base table declared in the same file must already be there.
    /// </summary>
    private static void ApplySourceFileExtract(AlSourceFileExtract extract)
    {
        ApplyTables(extract.Tables);
        ApplyTableExtensions(extract.TableExtensions);
        ApplyPages(extract.Pages, extract.PageExtensions);
        ApplyReports(extract.Reports, extract.ReportExtensions);
        ApplyQueries(extract.Queries);
        ApplyXmlPorts(extract.XmlPorts);
        ApplyObjectDecls(extract.ObjectDecls);
        ApplyObjectCaptions(extract.ObjectCaptions);
    }
}

/// <summary>One <c>tableextension</c> as parsed from source, before any merge into the base
/// table's accumulated field list — see RecordPatches.ApplyTableExtensions.</summary>
internal record ParsedTableExtension(int ExtId, string ExtName, string BaseName, List<ParsedField> Fields);

/// <summary>One object's declared <c>Caption</c> (null when it declares none), keyed per kind
/// because AL id namespaces are per-object-type.</summary>
internal record ParsedAlObjectCaption(string Kind, int Id, string? Caption);
