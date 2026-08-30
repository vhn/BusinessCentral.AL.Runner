// RecordPatches.AlSourceParser — parses AL `table` / `tableextension` declarations into
// ParsedTable records keyed by table ID. The output is consumed by NclMetaTableBuilder to
// produce real NCLMetaTable instances at runtime.
//
// Syntax-level extraction runs on Microsoft.Dynamics.Nav.CodeAnalysis' own AL parser — the
// same front end BcCompiler.Emit already runs over the very same files (#1696). It replaced
// a set of regexes over raw .al text whose failure mode was a SILENT WRONG VALUE rather than
// a crash: `[^;]+` could not cross a semicolon inside a string literal (`InitValue =
// 'Open; pending review'` captured `'Open`), a comment mentioning a property name was read
// as that property (#1690), quoting was captured inconsistently (#1674), and object
// boundaries were guessed by slicing between regex matches. A syntax tree answers all four
// structurally: comments are trivia and simply are not in it.
//
// `SyntaxTree.ParseObjectText` needs only a ParseOptions — no Compilation, no reference
// closure — so this works on every input the parser takes: real files, AL extracted from
// dependency .app archives, and the table text NclMetaTableBuilder synthesizes.
//
// CalcFormula is mapped structurally too: `sum/lookup/…` and `count/exist` are two different
// node types, and each filter condition carries its own type, so `X = field(Y)` filters are
// selected BY TYPE rather than by a pattern that happened not to match `const(...)`. The one
// regex left in this file extracts a length from a type's text (`Code[10]` → 10), which has no
// structure for a tree to add.
using System.Text.RegularExpressions;
using NavCA = Microsoft.Dynamics.Nav.CodeAnalysis;
using NavSyntax = Microsoft.Dynamics.Nav.CodeAnalysis.Syntax;

namespace AlRunner.Patches;

public static partial class RecordPatches
{
    // Matches BcCompiler.Emit's options so this parse sees the same source the emit does —
    // notably the CLEANSCHEMA1..25 preprocessor symbols, which gate real field declarations
    // in the BaseApp, PLUS whatever the caller passed via --define / --preprocessor-symbols.
    // DocumentationMode.None: doc comments are trivia we never read.
    //
    // This MUST be a property recomputed on every call, not a `static readonly` field.
    // BcCompiler.SetExtraPreprocessorSymbols(...) runs at Program.cs:727, after this type
    // may already have been touched elsewhere in the same process — a `static readonly`
    // field would freeze at type-init with the empty symbol set, and a `.Concat(...)`
    // bolted onto that frozen field would look like a fix while changing nothing (#1900:
    // the compiler's two ParseOptions sites already merge `_extraPreprocessorSymbols` per
    // call; this parser was the one site that didn't). GetExtraPreprocessorSymbols() is
    // cheap (a lock plus a sorted copy of a handful of strings), so recomputing it per
    // parse call costs nothing worth caching.
    private static NavCA.ParseOptions AlParseOptions =>
        AlParseOptionsFor(AlRunner.BcCompiler.GetExtraPreprocessorSymbols());

    /// <summary>
    /// The same options against an already-taken snapshot of the extra symbols, so a batch that
    /// reads them once can hand the identical set to every parse in it — and so the memo key and
    /// the options can never disagree about which symbols a tree was built under.
    /// </summary>
    private static NavCA.ParseOptions AlParseOptionsFor(IEnumerable<string> extraSymbols) => new(
        runtimeVersion: null!,
        preprocessorSymbols: Enumerable.Range(1, 25).Select(n => $"CLEANSCHEMA{n}")
            .Concat(extraSymbols),
        documentationMode: NavCA.DocumentationMode.None);

    // Field type text still yields its length by pattern (`Code[10]` → 10). The type is one
    // token's text with no nesting, so there is nothing structural for a tree to add here.
    private static readonly Regex RxTypeLength = new(@"\[(\d+)\]", RegexOptions.Compiled);

    /// <summary>
    /// Identifiers come off the tree with AL's quoting intact — <c>"Entry No."</c>, not
    /// <c>Entry No.</c>. Every consumer (key resolution, tableextension merge, metatable
    /// lookup) matches on the bare name, so the quotes come off exactly once, here.
    /// (<c>Unquote</c> itself lives in RecordPatches.NclMetaQueryBuilder.cs — same partial
    /// class, same rule.)
    /// </summary>
    private static string IdentText(NavSyntax.IdentifierNameSyntax? id) =>
        id == null ? "" : Unquote(id.Identifier.ValueText ?? id.Identifier.Text ?? "");

    /// <summary>
    /// The caption literal declared by <c>Caption = 'text'</c>, or null when the field
    /// declares none — in which case BC's own field-name fallback is the correct answer and
    /// must stand.
    /// <para>Only the LABEL LITERAL is the caption. A label may carry trailing parts
    /// (<c>Caption = 'It''s on', Comment='x';</c>) and the property value node's text spans
    /// all of them, so reading the node wholesale would append <c>, Comment='x'</c> to the
    /// caption. <see cref="NavSyntax.LabelSyntax.LabelText"/> is just the literal.
    /// Doubled single quotes are AL's escape for an embedded quote.</para>
    /// </summary>
    private static string? CaptionFrom(NavSyntax.PropertyValueSyntax? value)
    {
        if (value is not NavSyntax.LabelPropertyValueSyntax label) return null;
        var text = label.Value?.LabelText?.ToString();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'') text = text[1..^1];
        return text.Replace("''", "'");
    }

    /// <summary>
    /// The declared object id of any AL object that has one, or null. `interface`,
    /// `controladdin` and `profile` have no object id at all — they do not derive from
    /// <c>ApplicationObjectSyntax</c> — which is the same set the id-keyed parsers already
    /// excluded, for the same reason (AllObj is keyed by (type, id); a synthetic id would be
    /// a fabrication).
    /// </summary>
    private static int? ObjectIdOf(NavCA.SyntaxNode obj) =>
        obj is NavSyntax.ApplicationObjectSyntax ao && ao.ObjectId?.Value.Value is int id ? id : null;

    /// <summary>
    /// The AL object-kind name used as the first half of the `(Kind, Id)` keys and as AllObj's
    /// "Object Type". These strings are a data contract — `XMLport`'s casing in particular is
    /// what the virtual table emits. Objects not listed are not tracked by the id-keyed parsers.
    /// </summary>
    private static string? AlObjectKindName(NavCA.SyntaxNode obj) => obj switch
    {
        NavSyntax.TableSyntax => "Table",
        NavSyntax.TableExtensionSyntax => "TableExtension",
        NavSyntax.PageSyntax => "Page",
        NavSyntax.PageExtensionSyntax => "PageExtension",
        NavSyntax.ReportSyntax => "Report",
        NavSyntax.ReportExtensionSyntax => "ReportExtension",
        NavSyntax.CodeunitSyntax => "Codeunit",
        NavSyntax.QuerySyntax => "Query",
        NavSyntax.XmlPortSyntax => "XMLport",
        NavSyntax.EnumTypeSyntax => "Enum",
        NavSyntax.EnumExtensionTypeSyntax => "EnumExtension",
        NavSyntax.PermissionSetSyntax => "PermissionSet",
        NavSyntax.PermissionSetExtensionSyntax => "PermissionSetExtension",
        _ => null,
    };

    /// <summary>
    /// An OBJECT-level <c>Caption</c>, matching what the old brace-depth-scoped
    /// <c>ReadTopLevelProperty</c> returned: the unescaped literal for <c>'…'</c>, the trimmed
    /// text for a bare value, and null when the object declares no Caption at all. Null is
    /// meaningful — "declares none", which the consumer turns into AL's name fallback — and is
    /// not the same as an empty caption.
    /// <para>Differs from <see cref="CaptionFrom"/> (field-level), which answers null for a
    /// non-label value because the field-level regex required quotes.</para>
    /// </summary>
    private static string? PropertyTextFrom(NavSyntax.PropertyValueSyntax? value)
    {
        if (value == null) return null;
        return CaptionFrom(value) ?? value.ToString()?.Trim();
    }

    /// <summary>
    /// The last name segment of a possibly-namespaced object reference:
    /// <c>Microsoft.Sales.History."Sales Invoice Header"</c> → <c>Sales Invoice Header</c>,
    /// <c>Customer</c> → <c>Customer</c>. Quote-aware, so a quoted name that itself contains a
    /// dot (<c>"Doc. No."</c>) survives intact — the old dot-collapse ran over the unquoted
    /// text and would have truncated it to <c>No.</c>.
    /// </summary>
    private static string LastNameSegment(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length >= 2 && s[^1] == '"')
        {
            var open = s.LastIndexOf('"', s.Length - 2);
            if (open >= 0) return s[(open + 1)..^1];
        }
        int dot = s.LastIndexOf('.');
        return dot >= 0 && dot < s.Length - 1 ? s[(dot + 1)..] : Unquote(s);
    }

    /// <summary>Property lookup by AL name, case-insensitive as AL itself is.</summary>
    private static NavSyntax.PropertyValueSyntax? PropValue(
        NavSyntax.PropertyListSyntax? list, string name)
    {
        if (list == null) return null;
        // Properties is a list of PropertySyntaxOrEmpty: a stray `;` in a property list is
        // legal AL and parses as an empty entry, which simply has no name to match.
        foreach (var entry in list.Properties)
        {
            if (entry is not NavSyntax.PropertySyntax p) continue;
            if (string.Equals(p.Name?.Identifier.ValueText, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        }
        return null;
    }

    /// <summary>
    /// A page-valued table property (<c>LookupPageId</c> / <c>DrillDownPageId</c>) as written:
    /// the last name segment of a page reference (<c>Microsoft.Sales."Customer List"</c> →
    /// <c>Customer List</c>), or the digits when the AL declared a bare id. Null when the
    /// property is absent — "declares none", which the Table Metadata provider turns into 0
    /// rather than a guess.
    /// </summary>
    private static string? PageRefText(NavSyntax.PropertyValueSyntax? value)
    {
        var text = value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        var segment = LastNameSegment(text);
        return string.IsNullOrWhiteSpace(segment) ? null : segment;
    }

    private static bool PropIs(NavSyntax.PropertyListSyntax? list, string name, string expected) =>
        string.Equals(PropValue(list, name)?.ToString()?.Trim(), expected,
            StringComparison.OrdinalIgnoreCase);

    // Single-slot memo of the most recently built syntax tree's object list, keyed on the
    // exact (text, active preprocessor symbols) pair that produced it. #1903: the eight
    // TryParse*File extractors (table, tableextension, page, report, query, xmlport,
    // object-decl, object-caption) each call ParseAlObjects on the SAME file text
    // back-to-back — RecordPatches.ExtractSourceFile is the shared call
    // site both AddSourceDirs and Register() route every file through — so remembering
    // only the LAST parse turns 8 identical tree builds per file into 1 real build plus 7
    // cache hits, with no change to AlParseOptions, to any TryParse*File signature, or to
    // the eight extractors' own code.
    //
    // The key is (text, symbols), never text alone. #1900 was exactly a parser that
    // silently stopped seeing --define symbols (a `static readonly` field froze the
    // preprocessor set at type-init before BcCompiler.SetExtraPreprocessorSymbols ran). A
    // memo keyed on text alone would reproduce that bug through a different door: two
    // calls for the same text under two different --define sets would incorrectly share
    // one cached tree. AlParseOptions (see above) is still recomputed on every miss:
    // caching here changes WHEN a tree is (re)built, never what determines whether it
    // must be.
    private static string? _lastParsedText;
    private static string[]? _lastParsedSymbols;
    private static IReadOnlyList<NavCA.SyntaxNode> _lastParsedObjects = Array.Empty<NavCA.SyntaxNode>();

    // A batch of trees built AHEAD of the extractors, so the one expensive step in registering
    // a source dir can use more than one core. #1903 removed the 8×-per-file waste and left the
    // floor: one real `ParseObjectText` per file, measured on npcore's 7,339 .al files (12.7 MB)
    // as ~6s of the ~7s `RecordPatches.AddSourceDir` costs in a warm --watch cycle — 84% of the
    // whole cycle, against 0.4s for the delta compile it exists to serve. Reading those files
    // is under 1s of it; the rest is the parser, at roughly 2 MB/s on one core.
    //
    // Parsing is a pure function of (text, options) — `AlParseOptions` is a fresh ParseOptions
    // per access, deliberately (see the #1900 note above), so there is no shared options object
    // to race on — and BC's own compiler parses a module's trees concurrently. So the files of
    // one batch are parsed in parallel and the eight extractors then run over the results
    // SERIALLY, in enumeration order. That order is load-bearing and not incidental:
    // `_extensionIdsByBaseTable` records tableextension ids "in AL declaration order (= the
    // order BC registers them, which the trigger pipeline preserves)", so parallelising the
    // extractors instead of the parse would reorder record-trigger dispatch.
    //
    // BATCHED rather than whole-tree, because the trees are the memory cost: holding all 7,339
    // at once is hundreds of MB on a path that is already the peak-RSS phase of a cold compile.
    // One batch is bounded, and the parallelism inside it is what matters.
    //
    // [ThreadStatic] on purpose. The single-slot memo below is process-wide and two concurrent
    // callers of AddSourceDirs would already tread on each other; this state is written by the
    // batch's own thread after its parallel phase has joined, and read by that same thread while
    // it runs the extractors, so it cannot be shared across callers at all.
    [ThreadStatic] private static Dictionary<string, IReadOnlyList<NavCA.SyntaxNode>>? _preParsedObjects;
    [ThreadStatic] private static string[]? _preParsedSymbols;

    /// <summary>
    /// Files below this many in a batch are parsed inline. The parallel phase is worth its
    /// scheduling overhead on a real app's thousands of files and not on the handful a fixture
    /// registers, and keeping the small case on the serial path keeps it exactly as it was.
    /// </summary>
    private const int PreParseParallelThreshold = 32;

    private static int _parseObjectTextCallCount;

    /// <summary>
    /// Number of times a syntax tree has actually been built (a real
    /// <c>SyntaxTree.ParseObjectText</c> call), as opposed to serving the single-slot memo
    /// above. #1903's proving test asserts THIS — a count, never a duration — to pin that N
    /// files registered through the eight extractors costs N tree builds, not 8N. Mirrors
    /// the discipline <see cref="PopulateNclMetadataCacheCallCount"/> established for #1833.
    ///
    /// <para>Interlocked because <see cref="BeginPreParse"/> builds trees on several threads: the
    /// count is the assertion the invariant rests on, so a lost increment would show up as a test
    /// that passes for the wrong reason.</para>
    ///
    /// <para>The pre-parse below counts one build per FILE it is handed, including two files that
    /// happen to hold identical text — it does not collapse those. That keeps this count equal to
    /// the number of registered files whether the batch was parsed in parallel or inline. A parse
    /// that THREW counts once and once only, like any other answer: see the negative memo in
    /// <see cref="ParseAlObjects"/>.</para>
    /// </summary>
    internal static int ParseObjectTextCallCount => Volatile.Read(ref _parseObjectTextCallCount);

    /// <summary>
    /// Build the syntax trees for <paramref name="texts"/> up front, so the extractors that follow
    /// find them ready. Returns a scope that must be disposed before the calling thread parses
    /// anything else — the trees are held only for as long as the batch is being extracted.
    /// </summary>
    private static PreParseScope BeginPreParse(IReadOnlyList<string?> texts)
    {
        var symbols = AlRunner.BcCompiler.GetExtraPreprocessorSymbols().ToArray();
        var parsed = new Dictionary<string, IReadOnlyList<NavCA.SyntaxNode>>(
            texts.Count, StringComparer.Ordinal);

        // A text whose parse THREW enters the batch as an EMPTY object list, not as an absent
        // entry. The parse is a pure function of (text, symbols), so "it threw" is that pair's
        // real answer and re-attempting it can only reach the same one — while leaving it out
        // means the eight extractors each take ParseAlObjects' miss path and re-throw, costing
        // nine attempts for the one file class least able to afford them. Same reasoning as the
        // negative memo in ParseAlObjects, and the two must agree: a batched file and a
        // directly-extracted one both cost exactly one attempt.
        if (texts.Count >= PreParseParallelThreshold)
        {
            var results = new (string Text, IReadOnlyList<NavCA.SyntaxNode> Objects)?[texts.Count];
            Parallel.For(
                0, texts.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                i =>
                {
                    if (texts[i] is not { } text || string.IsNullOrWhiteSpace(text)) return;
                    results[i] = (text, ParseObjectsUncached(text, symbols) ?? Array.Empty<NavCA.SyntaxNode>());
                });
            foreach (var result in results)
                if (result is { } entry) parsed[entry.Text] = entry.Objects;
        }
        else
        {
            foreach (var text in texts)
                if (text is { } present && !string.IsNullOrWhiteSpace(present))
                    parsed[present] = ParseObjectsUncached(present, symbols)
                        ?? Array.Empty<NavCA.SyntaxNode>();
        }

        _preParsedObjects = parsed;
        _preParsedSymbols = symbols;
        return default;
    }

    /// <summary>Releases the batch's trees. See <see cref="BeginPreParse"/>.</summary>
    private readonly struct PreParseScope : IDisposable
    {
        public void Dispose()
        {
            _preParsedObjects = null;
            _preParsedSymbols = null;
        }
    }

    /// <summary>
    /// One real parse, with no memo of any kind — the shared body of <see cref="ParseAlObjects"/>'
    /// miss path and of the pre-parse above, so both count identically. <b>Null means the parse
    /// threw</b>, which is not the same as a file that legitimately declares no object (a
    /// comment-only file parses fine and yields an empty list); the two are kept apart because
    /// only the first must be prevented from entering the memo.
    /// </summary>
    private static IReadOnlyList<NavCA.SyntaxNode>? ParseObjectsUncached(string text, string[] symbols)
    {
        Interlocked.Increment(ref _parseObjectTextCallCount);
        try
        {
            if (FailParseForTests?.Invoke(text) == true)
                throw new InvalidOperationException("FailParseForTests: injected parse failure");
            var tree = NavSyntax.SyntaxTree.ParseObjectText(
                text, path: "", encoding: null!, AlParseOptionsFor(symbols), default);
            return tree.GetRoot() is NavSyntax.CompilationUnitSyntax root
                ? root.ChildNodes().ToList()
                : Array.Empty<NavCA.SyntaxNode>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Test seam: when non-null, <see cref="ParseObjectsUncached"/> throws for any text this
    /// predicate accepts, so the failed-parse path can be driven deterministically.
    ///
    /// <para>It exists because that path cannot be reached any other way. BC's AL parser is
    /// error-tolerant by design: measured against 28.1's
    /// <c>Microsoft.Dynamics.Nav.CodeAnalysis</c>, free-form garbage, an unterminated brace,
    /// an unterminated string literal, a stray <c>#endif</c>, embedded NUL and lone-surrogate
    /// characters, and 20,000-deep parenthesis/<c>begin</c> nesting all return a tree carrying
    /// diagnostics — none of them throws. The one input that does "fail" (50,000-deep
    /// statement nesting) overflows the stack and aborts the process, which no <c>catch</c>
    /// ever sees.</para>
    ///
    /// <para>So no <c>.al</c> file can currently take the catch above. The path still has to be
    /// right for what CAN escape — an <c>OutOfMemoryException</c> on a pathological file, or a
    /// future BC parser that does throw — because getting it wrong costs eight failed re-parses
    /// per affected file instead of one.</para>
    /// </summary>
    internal static Func<string, bool>? FailParseForTests;

    /// <summary>
    /// Parses every AL object in <paramref name="text"/> with BC's own parser and returns the
    /// object declarations. Never throws: this is fed arbitrary .al text — pages, codeunits,
    /// AL sliced out of dependency .app archives, and synthesized table text — and a parse it
    /// cannot make sense of must leave the caller's state untouched, not break the run.
    /// Diagnostics are ignored on purpose: a file that fails to compile for an unrelated
    /// reason still yields a usable table declaration, which is what the regexes did too.
    /// </summary>
    private static IReadOnlyList<NavCA.SyntaxNode> ParseAlObjects(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        // GetExtraPreprocessorSymbols() is a lock plus a sorted copy of a handful of
        // strings (see AlParseOptions above) — cheap enough to call on every ParseAlObjects
        // invocation just to test the memo key, including on the 7-out-of-8 calls that end
        // up being cache hits.
        var symbols = AlRunner.BcCompiler.GetExtraPreprocessorSymbols().ToArray();

        // The batch this thread pre-parsed, if it is inside one. Checked before the single-slot
        // memo because it is the authoritative answer for every file of the current pass, and
        // under the same (text, symbols) key — a batch parsed under one --define set must not
        // answer for a call made under another.
        if (_preParsedObjects is { } preParsed && _preParsedSymbols is { } preParsedSymbols &&
            symbols.AsSpan().SequenceEqual(preParsedSymbols) &&
            preParsed.TryGetValue(text, out var preParsedObjects))
        {
            return preParsedObjects;
        }

        if (_lastParsedText == text && _lastParsedSymbols != null &&
            symbols.AsSpan().SequenceEqual(_lastParsedSymbols))
        {
            return _lastParsedObjects;
        }

        // A malformed input is not a runner gap — the AL simply is not parseable, and the caller's
        // contract is "extract what you can". Callers that need a table and do not get one already
        // report that themselves ("AL source not parsed").
        if (ParseObjectsUncached(text, symbols) is not { } objects)
        {
            // The empty result is MEMOIZED, not discarded. `[]` is this (text, symbols) pair's
            // real answer — the parse is a pure function of the pair, so a throw for it is as
            // stable a fact as a tree would have been, and re-attempting cannot produce a
            // different one. Clearing the slot instead (what this used to do) meant each of the
            // eight extractors re-parsed the same doomed text and cleared it again for the next:
            // one file cost eight failed parses rather than one, silently undoing #1903's
            // "N files cost N tree builds, not 8N" invariant for exactly the files least able
            // to afford it. Keyed on ITS OWN text like any other entry, so the next file is
            // still parsed fresh rather than served this one's empty answer.
            _lastParsedText = text;
            _lastParsedSymbols = symbols;
            _lastParsedObjects = [];
            return [];
        }
        _lastParsedText = text;
        _lastParsedSymbols = symbols;
        _lastParsedObjects = objects;
        return objects;
    }

    /// <summary>
    /// Builds a <see cref="ParsedField"/> from one <c>field(...)</c> declaration.
    /// <para>Identical for a `table` field and a `tableextension` field: AL declares them the
    /// same way and BC gives them the same metadata. They used to differ — extension fields
    /// were parsed without OptionMembers and without AutoIncrement (#1711), which left an
    /// Option field added by a tableextension with no option string, so NCLOptionMetadata saw
    /// the wrong member count (#1674's defect class), and an AutoIncrement field added by a
    /// tableextension with no autoincrement semantics at all.</para>
    /// </summary>
    private static ParsedField? ParseFieldSyntax(NavSyntax.FieldSyntax f)
    {
        if (f.No.Value is not int fid) return null;
        var fname = IdentText(f.Name);
        var ftype = f.Type?.ToString()?.Trim() ?? "";
        int length = 0;
        var lm = RxTypeLength.Match(ftype);
        if (lm.Success) int.TryParse(lm.Groups[1].Value, out length);

        var props = f.PropertyList;
        bool isFlowField = PropIs(props, "FieldClass", "FlowField");
        // #1716 — FlowFilter is its own FieldClass, not a Normal field that happens to be
        // named "...Filter". BC keys two behaviours off it: DataHelper.PassesFieldFilters
        // SKIPS filters on FlowFilter fields (so `SetRange("Date Filter", ...)` never
        // excludes rows of the table declaring it), and FlowFieldsHelper dispatches
        // `field(...)` where-conditions on the value field's FieldClass — FlowFilter reads
        // the caller's FILTER, Normal reads the stored value. Leaving it Normal produced
        // both failures at once: the parent row vanished under its own flow filter, and the
        // FlowField compared the source column against a blank.
        bool isFlowFilter = PropIs(props, "FieldClass", "FlowFilter");

        ParsedCalcFormula? calcFormula = null;
        if (isFlowField)
            calcFormula = CalcFormulaFrom(PropValue(props, "CalcFormula"));

        // Option-type fields: OptionMembers is the comma-separated list BC's
        // NCLOptionMetadata constructor expects. AL quotes members that contain spaces, but
        // the runtime metadata stores their names without identifier quotes. Split only on
        // commas outside quoted identifiers, unquote each token, and retain empty entries
        // (BC allows blank members, and #1674 depends on that).
        string? optionMembers = null;
        if (ftype.Equals("Option", StringComparison.OrdinalIgnoreCase)
            && PropValue(props, "OptionMembers") is { } om)
        {
            optionMembers = string.Join(",",
                SplitOutsideQuotes(om.ToString(), ',').Select(token => Unquote(token.Trim())));
        }

        // InitValue is passed to MetaField.initValue as RAW AL TEXT, quotes and all, because
        // NclMetaTableBuilder does the type-aware unquoting downstream — that split is what
        // #1674's blank-enum fix depends on. Do not "clean" it here without deleting the
        // stripping there in the same change.
        string? initValueText = PropValue(props, "InitValue")?.ToString()?.Trim();

        bool isAutoIncrement = PropIs(props, "AutoIncrement", "true");
        var caption = CaptionFrom(PropValue(props, "Caption"));
        var optionCaption = ftype.Equals("Option", StringComparison.OrdinalIgnoreCase)
            ? CaptionFrom(PropValue(props, "OptionCaption"))
            : null;

        // ObsoleteState / ObsoleteReason (#1780): the Field virtual table (2000000041) reports
        // these via BC's own FieldDataProvider.GetFieldRecordBuffer, which reads them off the
        // NCLMetaField that CreateFromMetaTable builds from OUR MetaField — so capturing the AL
        // declaration here and passing it to MetaField's obsoleteState/obsoleteReason ctor
        // params (see BuildMetaField) is the whole fix; BC's own factory does the rest.
        // ObsoleteState is an EnumPropertyValueSyntax whose text IS the member name ("Removed",
        // "Pending", "PendingMove", "Moved") — undeclared leaves it null, which the builder
        // treats as the AL/BC default "No". ObsoleteReason is a plain (non-multilanguage)
        // single-quoted string — ConstValueText's quote-stripping (shared with const(...)
        // conditions and InitValue) applies unchanged.
        var obsoleteStateText = PropValue(props, "ObsoleteState")?.ToString()?.Trim();
        var obsoleteState = string.IsNullOrEmpty(obsoleteStateText) ? "No" : obsoleteStateText;
        var obsoleteReasonRaw = PropValue(props, "ObsoleteReason")?.ToString();
        var obsoleteReason = obsoleteReasonRaw == null ? null : ConstValueText(obsoleteReasonRaw);

        // TableRelation: captured as a list of ARMS — the plain `Table` / `Table.Field`
        // shape is one condition-less arm, an `if (...) ... else ...` chain is one arm per
        // link (#1737, extending #1730's unconditional capture). Each arm carries its
        // if-conditions (fields of THIS table) and its where(...) filters (fields of the
        // related table); NavRecord.UpdateReferencesOnRenameAsync evaluates both exactly as
        // real BC does. A shape this code cannot carry faithfully refuses the WHOLE
        // relation: half-capturing (an arm without its conditions) would make Rename
        // rewrite rows real BC leaves alone — a silent wrong write, worse than the old
        // behaviour (no propagation).
        List<ParsedRelationArm>? relationArms = null;
        bool relationValidate = !PropIs(props, "ValidateTableRelation", "false");
        if (!isFlowField && !isFlowFilter
            && PropValue(props, "TableRelation") is NavSyntax.TableRelationPropertyValueSyntax tr)
        {
            relationArms = ParseRelationArms(tr, fname);
        }

        return new ParsedField(fid, fname, ftype, length, isFlowField, calcFormula,
            optionMembers, initValueText, isAutoIncrement, caption,
            relationArms, relationValidate, isFlowFilter, obsoleteState, obsoleteReason,
            optionCaption);
    }

    /// <summary>
    /// Walks a TableRelation's if/else chain into its arms. Each link of the chain is a
    /// <c>TableRelationPropertyValueSyntax</c>; the terminal <c>else</c> (and the plain,
    /// unconditional shape) is simply a link with no <c>IfExpression</c> — which is also
    /// exactly how real BC treats it: the else arm carries NO condition, not the complement
    /// of the earlier arms' conditions (verified against a real service tier; see corpus
    /// codeunit 60239, Record_Rename_ConditionalRelation_ElseTableRename_UpdatesIfArmRowsToo).
    /// Returns null — refusing the whole relation — on any arm this representation cannot
    /// carry faithfully.
    /// </summary>
    private static List<ParsedRelationArm>? ParseRelationArms(
        NavSyntax.TableRelationPropertyValueSyntax tr, string fieldName)
    {
        var arms = new List<ParsedRelationArm>();
        for (var node = tr; node != null; node = node.ElseExpression?.ElseTableRelationCondition)
        {
            var parts = NameParts(node.RelatedTableField);
            // 1 part = table; 2 parts = table + field. A 3+-part (namespace-qualified) name
            // is ambiguous without symbol resolution, so the relation stays uncaptured.
            if (parts.Count is not (1 or 2))
            {
                Console.Error.WriteLine(
                    $"[TableRelation] REFUSED {fieldName}: {parts.Count}-part related-table name '{node.RelatedTableField}'");
                return null;
            }
            var conditions = RelationConditionList(node.IfExpression?.IfTableRelationCondition, fieldName);
            var filters = RelationConditionList(node.TableFilter?.Filter, fieldName, allowFieldLinks: true);
            if (conditions == null || filters == null) return null;
            arms.Add(new ParsedRelationArm(parts[0], parts.Count == 2 ? parts[1] : null,
                conditions, filters));
        }
        return arms;
    }

    /// <summary>
    /// The conditions of an <c>if (...)</c> arm, or the entries of a <c>where(...)</c>
    /// filter — the same <c>TableFilterExpressionSyntax</c> node, and the same shapes as a
    /// CalcFormula's where, so they reuse <see cref="ParsedCalcFilter"/>. Conditional
    /// <c>if (...)</c> clauses carry <c>const(...)</c> and <c>filter(...)</c>. A relation's
    /// <c>where(...)</c> clause may also carry <c>field(...)</c>, which is resolved against
    /// the record declaring the relation at evaluation time. Other shapes refuse the whole
    /// relation rather than emitting a comparison BC never wrote.
    /// </summary>
    private static List<ParsedCalcFilter>? RelationConditionList(
        NavSyntax.TableFilterExpressionSyntax? filter, string fieldName, bool allowFieldLinks = false)
    {
        var list = new List<ParsedCalcFilter>();
        if (filter == null) return list;
        foreach (var cond in filter.Conditions)
        {
            switch (cond)
            {
                // "Item No." = field("Item No.") in a where(...) clause. The target field
                // is constrained by a field on the record declaring the relation, exactly
                // like a CalcFormula FIELD filter. Conditional-relation if(...) clauses do
                // not accept this shape, so callers must opt in for where(...) only.
                case NavSyntax.SimpleFieldExpressionSyntax sfe when allowFieldLinks:
                    list.Add(new ParsedCalcFilter(
                        Unquote(sfe.LeftHandSide?.ToString()?.Trim() ?? ""),
                        ParsedCalcFilterKind.Field,
                        ParentFieldName: Unquote(sfe.Identifier?.ToString()?.Trim() ?? "")));
                    break;

                // Kind = const(A)
                case NavSyntax.ConstExpressionSyntax ce:
                    list.Add(new ParsedCalcFilter(
                        Unquote(ce.LeftHandSide?.ToString()?.Trim() ?? ""),
                        ParsedCalcFilterKind.Const,
                        Value: ConstValueText(ce.Identifier?.ToString())));
                    break;

                // Status = filter(Open|Released)
                case NavSyntax.FilterExpressionSyntax fe:
                    list.Add(new ParsedCalcFilter(
                        Unquote(fe.LeftHandSide?.ToString()?.Trim() ?? ""),
                        ParsedCalcFilterKind.Filter,
                        Value: fe.Filter?.ToString()?.Trim() ?? ""));
                    break;

                default:
                    Console.Error.WriteLine(
                        $"[TableRelation] REFUSED {fieldName}: unsupported condition " +
                        $"{cond?.GetType().Name} ({cond})");
                    return null;
            }
        }
        return list;
    }

    /// <summary>Flattens a (possibly qualified) name into its unquoted identifier parts:
    /// <c>"ALT Relation Parent"."Code"</c> → ["ALT Relation Parent", "Code"].</summary>
    private static List<string> NameParts(NavSyntax.NameSyntax? name)
    {
        var parts = new List<string>();
        void Walk(NavSyntax.NameSyntax? n)
        {
            switch (n)
            {
                case NavSyntax.QualifiedNameSyntax q:
                    Walk(q.Left);
                    if (q.Right != null)
                        parts.Add(Unquote(q.Right.Identifier.ValueText ?? q.Right.Identifier.Text ?? ""));
                    break;
                case NavSyntax.SimpleNameSyntax s:
                    parts.Add(Unquote(s.Identifier.ValueText ?? s.Identifier.Text ?? ""));
                    break;
            }
        }
        Walk(name);
        return parts;
    }

    private static void TryParseTableFile(string text) => ApplyTables(ExtractTables(text));

    /// <summary>
    /// The pure half: every <c>table</c> declared in <paramref name="text"/>, in declaration
    /// order. Reads nothing but the syntax tree, so the result is a function of
    /// (text, preprocessor symbols) alone — which is what lets a warm reload replay it from a
    /// per-file memo instead of re-deriving it. See RecordPatches.SourceFileExtracts.cs.
    /// </summary>
    private static List<ParsedTable> ExtractTables(string text)
    {
        var result = new List<ParsedTable>();
        foreach (var obj in ParseAlObjects(text))
        {
            if (obj is not NavSyntax.TableSyntax table) continue;
            if (table.ObjectId?.Value.Value is not int tableId) continue;
            var tableName = IdentText(table.Name);

            var fields = new List<ParsedField>();
            if (table.Fields != null)
                foreach (var f in table.Fields.Fields)
                    if (ParseFieldSyntax(f) is { } pf)
                        fields.Add(pf);

            // First key is the PK; all subsequent keys are secondary.
            var pkFieldIds = new List<int>();
            var secondaryKeys = new List<ParsedKey>();
            bool firstKey = true;
            if (table.Keys != null)
            {
                foreach (var k in table.Keys.Keys)
                {
                    var keyName = IdentText(k.Name);
                    var keyFieldIds = new List<int>();
                    foreach (var kf in k.Fields)
                    {
                        var kn = IdentText(kf as NavSyntax.IdentifierNameSyntax);
                        var f = fields.FirstOrDefault(x =>
                            string.Equals(x.FieldName, kn, StringComparison.OrdinalIgnoreCase));
                        if (f != null) keyFieldIds.Add(f.FieldId);
                    }
                    if (firstKey)
                    {
                        pkFieldIds.AddRange(keyFieldIds);
                        firstKey = false;
                    }
                    else if (keyFieldIds.Count > 0)
                    {
                        secondaryKeys.Add(new ParsedKey(keyName, keyFieldIds));
                    }
                }
            }
            // Fallback: first field is PK
            if (pkFieldIds.Count == 0 && fields.Count > 0)
                pkFieldIds.Add(fields[0].FieldId);

            // DataPerCompany: AL's default is TRUE, so only the explicit opt-out is parsed.
            // MetaTable's own ctor default for isDataPerCompany is false — the opposite of
            // AL's — and BC's RecordImplementation.ChangeCompany returns true immediately for
            // a table that is not per-company.
            var isTableTypeTemporary = PropIs(table.PropertyList, "TableType", "Temporary");
            var dataPerCompany = !PropIs(table.PropertyList, "DataPerCompany", "false");
            // LookupPageId / DrillDownPageId feed the Table Metadata (2000000136) virtual
            // table. Kept as the written reference and resolved later: a page declared after
            // this table in compile order is not in the page inventory yet.
            var lookupPage = PageRefText(PropValue(table.PropertyList, "LookupPageId"));
            var drillDownPage = PageRefText(PropValue(table.PropertyList, "DrillDownPageId"));
            result.Add(new ParsedTable(tableId, tableName, fields, pkFieldIds,
                secondaryKeys, isTableTypeTemporary, dataPerCompany, lookupPage, drillDownPage));
        }
        return result;
    }

    private static void ApplyTables(IReadOnlyList<ParsedTable> tables)
    {
        foreach (var table in tables) _parsedTables[table.TableId] = table;
    }

    private static void TryParseTableExtensionFile(string text) =>
        ApplyTableExtensions(ExtractTableExtensions(text));

    /// <summary>
    /// The pure half — see <see cref="ExtractTables"/>. Every <c>tableextension</c> in
    /// <paramref name="text"/> with the fields it declares, in declaration order. All of the
    /// merge logic (dedup, metatable eviction, the ordered extension-id registry) is
    /// <see cref="ApplyTableExtensions"/>'s, because all of it reads or writes state outside
    /// this file.
    /// </summary>
    private static List<ParsedTableExtension> ExtractTableExtensions(string text)
    {
        var result = new List<ParsedTableExtension>();
        foreach (var obj in ParseAlObjects(text))
        {
            if (obj is not NavSyntax.TableExtensionSyntax ext) continue;
            if (ext.ObjectId?.Value.Value is not int extId) continue;
            var extName = IdentText(ext.Name);
            var baseName = Unquote(ext.BaseObject?.ToString()?.Trim() ?? "");

            // Extension fields are parsed exactly like base-table fields — see
            // ParseFieldSyntax for what they used to lose (#1711).
            var fields = new List<ParsedField>();
            // OfType<FieldSyntax>: a tableextension's field list also holds `modify(...)`
            // entries, which declare no new field. The regex only ever matched
            // `field(N; Name; Type)` either, so this keeps the same set.
            if (ext.Fields != null)
                foreach (var f in ext.Fields.Fields.OfType<NavSyntax.FieldSyntax>())
                    if (ParseFieldSyntax(f) is { } pf)
                        fields.Add(pf);

            result.Add(new ParsedTableExtension(extId, extName, baseName, fields));
        }
        return result;
    }

    /// <summary>
    /// The stateful half of the tableextension parse: merges each extension's fields into the
    /// base table's accumulated list, evicts a base metatable built before them, and records
    /// the extension object id.
    /// </summary>
    private static void ApplyTableExtensions(IReadOnlyList<ParsedTableExtension> extensions)
    {
        foreach (var (extId, extName, baseName, fields) in extensions)
        {
            Console.Error.WriteLine($"[TableExt] parsed extension {extId} '{extName}' extends '{baseName}' with {fields.Count} fields");

            // Merge into _parsedExtensionFields, record the extension id (so its emitted
            // TableExtension{extId} CLR type can be instantiated and registered on each
            // record of the base table — record-level triggers + field-validate dispatch),
            // and evict any already-built NCLMetaTable for the base table so a rebuild picks
            // up these fields. All three steps — including the eviction, whose necessity is
            // explained on MergeExtensionFields itself (#2126) — happen atomically in the
            // shared helper so a second writer (RecordPatches.BcAppFallback.cs's
            // EnsureBcSymbolExtensionIndex) can't repeat this file's own former omission of it.
            MergeExtensionFields(baseName, extId, fields);
        }
    }

    /// <summary>
    /// The tableextension fields accumulated for <paramref name="baseTableName"/> so far, as
    /// <see cref="ApplyTableExtensions"/> and EnsureBcSymbolExtensionIndex have merged them.
    /// <para>Read-only view of the accumulate itself, deliberately: the merged NCLMetaTable is
    /// the other way to observe this, but BC's skeleton metadata cache is NOT cleared by a
    /// reload (see <see cref="BcRuntime.ResetForNewBundleReload"/>), so it can still answer for
    /// a field the source has since dropped. This is the state the source parse actually
    /// owns.</para>
    /// </summary>
    internal static IReadOnlyList<ParsedField> ExtensionFieldsForBaseTable(string baseTableName) =>
        _parsedExtensionFields.TryGetValue(baseTableName.ToLowerInvariant(), out var fields)
            ? fields : Array.Empty<ParsedField>();

    /// <summary>
    /// Drop any cached NCLMetaTable built for <paramref name="baseTableName"/> before its
    /// tableextension fields were known, so the next lookup rebuilds it with them merged.
    /// No-op when the table has not been built yet (the common, in-order case).
    /// </summary>
    private static void EvictCachedMetaTableForBaseTable(string baseTableName)
    {
        foreach (var kvp in _parsedTables)
        {
            if (!string.Equals(kvp.Value.TableName, baseTableName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (_metaTableCache.TryRemove(kvp.Key, out _))
                Console.Error.WriteLine(
                    $"[TableExt] evicted stale NCLMetaTable {kvp.Key} '{baseTableName}' " +
                    $"(built before its tableextension fields were parsed)");
        }
    }

    /// <summary>
    /// Builds a <see cref="ParsedCalcFormula"/> from a CalcFormula property value node.
    /// <para>AL has two shapes and the parser gives them two node types:
    /// <c>sum/average/min/max/lookup</c> carry a qualified <c>Table.Field</c>
    /// (<see cref="NavSyntax.FieldCalculationFormulaSyntax"/>), while <c>count/exist</c> carry a
    /// table alone (<see cref="NavSyntax.TableCalculationFormulaSyntax"/>) and no field.</para>
    /// </summary>
    private static ParsedCalcFormula? CalcFormulaFrom(NavSyntax.PropertyValueSyntax? value)
    {
        string formulaType;
        string sourceTableName;
        string? sourceFieldName;
        NavSyntax.WhereExpressionSyntax? where;
        string signText;

        switch (value)
        {
            case NavSyntax.FieldCalculationFormulaSyntax f:
                formulaType = f.FormulaKeywordToken.ValueText;
                sourceTableName = Unquote(f.Field?.Left?.ToString()?.Trim() ?? "");
                sourceFieldName = f.Field?.Right == null ? null : Unquote(f.Field.Right.ToString().Trim());
                where = f.WhereExpression;
                signText = f.Sign.ValueText ?? "";
                break;
            case NavSyntax.TableCalculationFormulaSyntax t:
                formulaType = t.FormulaKeywordToken.ValueText;
                sourceTableName = Unquote(t.Table?.ToString()?.Trim() ?? "");
                sourceFieldName = null; // count/exist have no field part
                where = t.WhereExpression;
                signText = t.Sign.ValueText ?? "";
                break;
            default:
                return null;
        }

        // #1708 — the sign. `-sum(...)` is a negated formula; AL also accepts the no-op `+`.
        // The sign is now carried on ParsedCalcFormula and honoured by NclMetaTableBuilder
        // (MetaCalcFormula.reverseSign) and FlowFieldPatches (NegateResult), so parsing it is
        // no longer a silent lie about the value. A sign token this code has never seen is
        // still refused rather than guessed at.
        bool negated;
        if (signText.Length == 0 || signText == "+") negated = false;
        else if (signText == "-") negated = true;
        else
        {
            Console.Error.WriteLine($"[CalcFormula] REFUSED {sourceTableName}: unrecognised sign '{signText}'");
            return null;
        }

        if (string.IsNullOrEmpty(sourceTableName)) return null;

        // #1709 — every condition shape, selected BY NODE TYPE. Dropping `const(...)` and
        // `filter(...)` made the FlowField aggregate rows AL had excluded: a plausible wrong
        // number, silently (the Base Application writes 1215 const and 285 filter conditions).
        var filters = new List<ParsedCalcFilter>();
        if (where?.Filter != null)
        {
            foreach (var cond in where.Filter.Conditions)
            {
                switch (cond)
                {
                    // "Document No." = field("Code")
                    case NavSyntax.SimpleFieldExpressionSyntax sfe:
                        filters.Add(new ParsedCalcFilter(
                            Unquote(sfe.LeftHandSide?.ToString()?.Trim() ?? ""),
                            ParsedCalcFilterKind.Field,
                            ParentFieldName: Unquote(sfe.Identifier?.ToString()?.Trim() ?? "")));
                        break;

                    // Open = const(true)
                    case NavSyntax.ConstExpressionSyntax ce:
                        filters.Add(new ParsedCalcFilter(
                            Unquote(ce.LeftHandSide?.ToString()?.Trim() ?? ""),
                            ParsedCalcFilterKind.Const,
                            Value: ConstValueText(ce.Identifier?.ToString())));
                        break;

                    // Status = filter(Open|Released)
                    case NavSyntax.FilterExpressionSyntax fe:
                        filters.Add(new ParsedCalcFilter(
                            Unquote(fe.LeftHandSide?.ToString()?.Trim() ?? ""),
                            ParsedCalcFilterKind.Filter,
                            Value: fe.Filter?.ToString()?.Trim() ?? ""));
                        break;

                    // #1716 — the three flow-filter forms. All of them are FIELD links in
                    // BC's metadata; what distinguishes them is MetaFilter's two mode flags,
                    // which NCLMetaFilterField.CreateFromMetaFilter turns into
                    // NCLMetaFilterModes.ValueIsFilter / .OnlyMaxLimit. They are NOT a
                    // separate condition kind — modelling them as one is what left them
                    // unapplied — so they are carried as Field plus the flags.
                    //
                    //   "Account No." = field(filter(Totaling))                → ValueIsFilter
                    //   "Posting Date" = field(upperlimit("Date Filter"))      → OnlyMaxLimit
                    //   "Posting Date" = field(upperlimit(filter("Date Filter"))) → both
                    case NavSyntax.FieldFilterExpressionSyntax ffe:
                        filters.Add(new ParsedCalcFilter(
                            Unquote(ffe.LeftHandSide?.ToString()?.Trim() ?? ""),
                            ParsedCalcFilterKind.Field,
                            ParentFieldName: Unquote(ffe.Identifier?.ToString()?.Trim() ?? ""),
                            ValueIsFilter: true));
                        break;
                    case NavSyntax.FieldUpperLimitExpressionSyntax ule:
                        filters.Add(new ParsedCalcFilter(
                            Unquote(ule.LeftHandSide?.ToString()?.Trim() ?? ""),
                            ParsedCalcFilterKind.Field,
                            ParentFieldName: Unquote(ule.Identifier?.ToString()?.Trim() ?? ""),
                            OnlyMaxLimit: true));
                        break;
                    case NavSyntax.FieldUpperLimitFilterExpressionSyntax ulf:
                        filters.Add(new ParsedCalcFilter(
                            Unquote(ulf.LeftHandSide?.ToString()?.Trim() ?? ""),
                            ParsedCalcFilterKind.Field,
                            ParentFieldName: Unquote(ulf.Identifier?.ToString()?.Trim() ?? ""),
                            ValueIsFilter: true, OnlyMaxLimit: true));
                        break;

                    default:
                        // A condition shape this code has never seen. Refuse the WHOLE formula:
                        // aggregating over only the conditions we did understand silently
                        // widens the row set.
                        Console.Error.WriteLine(
                            $"[CalcFormula] REFUSED {sourceTableName}: unsupported where-condition " +
                            $"{cond?.GetType().Name} ({cond})");
                        return null;
                }
            }
        }

        Console.Error.WriteLine($"[CalcFormula] parsed {sourceTableName}.{sourceFieldName ?? "*"} type={formulaType} negated={negated} filters={filters.Count}");
        return new ParsedCalcFormula(formulaType, sourceTableName, sourceFieldName, filters, negated);
    }

    /// <summary>
    /// The literal of a <c>const(...)</c> condition, as text.
    /// <para>Quotes come off for the same reason they do on InitValue (#1674):
    /// <c>NCLMetaFilterConst</c> evaluates this text against the SOURCE field's own type, and
    /// an option member named <c>On Hold</c> is never matched by the 9-character
    /// <c>"On Hold"</c>. AL's doubled-quote escape is resolved with it.</para>
    /// </summary>
    private static string ConstValueText(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') return s[1..^1].Replace("\"\"", "\"");
        if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'') return s[1..^1].Replace("''", "'");
        return s;
    }

    /// <summary>
    /// Text overload, kept for <c>BcAppSymbolCache</c>, which reconstructs a CalcFormula from
    /// <c>SymbolReference.json</c> and so has text rather than a node. The text is wrapped in a
    /// minimal table and run through the same parser, so both callers share one implementation.
    /// </summary>
    internal static ParsedCalcFormula? TryParseCalcFormula(string fieldBody)
    {
        if (string.IsNullOrWhiteSpace(fieldBody)) return null;
        // The wrapper id is irrelevant — nothing is registered, the tree is read and dropped.
        var wrapped = "table 50000 __CalcFormulaProbe { fields { field(1; __F; Decimal) { "
                    + fieldBody + " } } }";
        foreach (var obj in ParseAlObjects(wrapped))
        {
            if (obj is not NavSyntax.TableSyntax table || table.Fields == null) continue;
            foreach (var f in table.Fields.Fields)
                if (CalcFormulaFrom(PropValue(f.PropertyList, "CalcFormula")) is { } parsed)
                    return parsed;
        }
        return null;
    }

    /// <summary>
    /// Parses a TableRelation value recovered from a dependency's SymbolReference.json by
    /// wrapping it in a minimal field declaration and using the same syntax-tree parser as
    /// source-authored tables.
    /// </summary>
    internal static List<ParsedRelationArm>? TryParseTableRelation(string relationText, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(relationText)) return null;
        var wrapped = "table 50000 __TableRelationProbe { fields { field(1; __F; Code[20]) { "
                    + "TableRelation = " + relationText + "; } } }";
        foreach (var obj in ParseAlObjects(wrapped))
        {
            if (obj is not NavSyntax.TableSyntax table || table.Fields == null) continue;
            foreach (var f in table.Fields.Fields)
                if (PropValue(f.PropertyList, "TableRelation") is NavSyntax.TableRelationPropertyValueSyntax relation)
                    return ParseRelationArms(relation, fieldName);
        }
        return null;
    }
}

// ─── Data holders ────────────────────────────────────────────────────────────

/// <summary>
/// Which shape of <c>where(...)</c> condition a <see cref="ParsedCalcFilter"/> carries. AL
/// writes three, they are NOT interchangeable, and reading one as another is a silent wrong
/// value (#1709). The flow-filter forms are <see cref="Field"/> plus the mode flags on
/// <see cref="ParsedCalcFilter"/>, exactly as BC's <c>MetaFilter</c> models them (#1716).
/// </summary>
internal enum ParsedCalcFilterKind
{
    /// <summary><c>"Document No." = field("No.")</c> — link to a field of the PARENT record.
    /// Becomes a <c>MetaFilter</c> of FilterType FIELD whose filterValue is the parent field
    /// id.</summary>
    Field,
    /// <summary><c>Open = const(true)</c> — compare against a literal. Becomes FilterType
    /// CONST, filterValue = the literal's text, which <c>NCLMetaFilterConst</c> evaluates
    /// against the SOURCE field's own type.</summary>
    Const,
    /// <summary><c>Status = filter(Open|Released)</c> — a filter EXPRESSION. Becomes
    /// FilterType FILTER, filterValue = the expression text, parsed by BC's own filter parser
    /// (<c>NCLMetaFilterExpression</c>).</summary>
    Filter,
}

/// <param name="SourceFieldName">Field of the FlowField's SOURCE table being constrained.</param>
/// <param name="Kind">Which of AL's condition shapes this is.</param>
/// <param name="ParentFieldName">Set only for <see cref="ParsedCalcFilterKind.Field"/>.</param>
/// <param name="Value">Const literal / filter expression text — set for
/// <see cref="ParsedCalcFilterKind.Const"/> and <see cref="ParsedCalcFilterKind.Filter"/>.</param>
/// <param name="ValueIsFilter">AL's <c>field(filter(X))</c> — the parent field's value is a
/// filter EXPRESSION over the source field, not a value to compare against
/// (<c>MetaFilter.ValueIsFilter</c>). #1716.</param>
/// <param name="OnlyMaxLimit">AL's <c>field(upperlimit(X))</c> — only the upper bound of the
/// resolved filter constrains the source field (<c>MetaFilter.OnlyMaxLimit</c>). #1716.</param>
internal record ParsedCalcFilter(
    string SourceFieldName,
    ParsedCalcFilterKind Kind = ParsedCalcFilterKind.Field,
    string? ParentFieldName = null,
    string? Value = null,
    bool ValueIsFilter = false,
    bool OnlyMaxLimit = false);

/// <param name="Negated">The formula's leading <c>-</c> (#1708), carried through to
/// <c>MetaCalcFormula.reverseSign</c> → <c>NCLMetaCalculationFormula.NegateResult</c>.</param>
internal record ParsedCalcFormula(string FormulaType, string SourceTableName, string? SourceFieldName, List<ParsedCalcFilter> Filters, bool Negated = false);

/// <summary>One arm of a field's TableRelation — the plain shape is a single arm with no
/// conditions. <paramref name="Conditions"/> constrain fields of the REFERENCING table (the
/// one declaring the relation); <paramref name="Filters"/> (from <c>where(...)</c>) constrain
/// fields of the related source table. Both reuse the <see cref="ParsedCalcFilter"/> shapes;
/// conditions support Const/Filter, while filters additionally support Field links.</summary>
internal record ParsedRelationArm(string TableName, string? FieldName, List<ParsedCalcFilter> Conditions, List<ParsedCalcFilter> Filters);

/// <param name="ObsoleteState">The AL member name as written — "No" (also the default when
/// the field declares no ObsoleteState at all), "Pending", "Removed", "PendingMove", or
/// "Moved" — matching <c>Microsoft.Dynamics.Nav.Types.Metadata.ObsoleteState</c>'s member
/// names exactly, so <c>Enum.Parse</c> in BuildMetaField needs no translation table (#1780).</param>
/// <param name="ObsoleteReason">The declared reason text, unquoted/unescaped, or null when the
/// field declares no ObsoleteReason (distinct from an explicit empty string).</param>
internal record ParsedField(int FieldId, string FieldName, string TypeName, int Length, bool IsFlowField = false, ParsedCalcFormula? CalcFormula = null, string? OptionMembers = null, string? InitValueText = null, bool IsAutoIncrement = false, string? Caption = null, List<ParsedRelationArm>? RelationArms = null, bool RelationValidate = true, bool IsFlowFilter = false, string ObsoleteState = "No", string? ObsoleteReason = null, string? OptionCaption = null);
internal record ParsedKey(string Name, List<int> FieldIds);
/// <param name="LookupPageName">The table's declared <c>LookupPageId</c> as WRITTEN — a page
/// name (<c>"Customer List"</c>) or a bare id in text form. Both sources state it by name:
/// AL source writes the reference, and a dependency's SymbolReference.json records
/// <c>LookupPageID</c>/<c>LookupPageId</c> as the page's NAME, never its number (measured
/// against Base Application 28.1). Resolution to an id is therefore deferred to row-build
/// time, where the full page inventory is known. Null means the table declares none, which
/// is not the same as 0 — see <c>RecordPatches.TableMetadataVirtualTable.cs</c>.</param>
/// <param name="DrillDownPageName">Same, for <c>DrillDownPageId</c>.</param>
internal record ParsedTable(int TableId, string TableName,
    List<ParsedField> Fields, List<int> PkFieldIds, List<ParsedKey>? SecondaryKeys = null,
    bool IsTableTypeTemporary = false, bool DataPerCompany = true,
    string? LookupPageName = null, string? DrillDownPageName = null);
