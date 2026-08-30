// RecordPatches.QueryProjection — project query columns from in-memory table rows.
//
// PROBLEM (single-dataitem query reads return 0):
//   NavQuery.FindDataImplAsync issues a find against
//   DataAccessSource.GetDataAccessForQuery(NCLMetaQuery).FindAsync(request) where the
//   request's MetaApplicationObject is the NCLMetaQuery. On the skeleton runtime that
//   DataAccess is backed by BC's TempTableDataProvider (the in-memory store where the
//   AL test inserted its rows). TempTableDataProvider is TABLE-shaped: it returns
//   ReadOnlyRecordBuffers whose slots are indexed by the table field ColumnIndex.
//   But NavQuery.GetColumnValue reads CurrentDataRow[queryColumn.ColumnIndex], where
//   queryColumn.ColumnIndex is the 0-based QUERY result slot. The two index spaces do
//   not line up, so every column comes back as the default (0 / '').
//
//   In real BC the SQL provider projects via a SELECT (table field -> result slot);
//   the temp provider never does because queries normally never reach it. We reproduce
//   exactly that projection here.
//
// FAITHFUL FIX (mirrors SQL SELECT projection):
//   The public TempTableDataProvider.Find / FindFromPosition entry points are
//   Cecil-redirected (NclCecilRewrite) to the two helpers below. They call the
//   provider's own private FindImplementation / FindByPositionImplementation (the
//   genuine in-memory storage + filter + sort logic, untouched), then — and only when
//   the request's MetaApplicationObject is an NCLMetaQuery — re-shape each table buffer
//   into a query-shaped ReadOnlyRecordBuffer:
//       projected[col.ColumnIndex] = tableBuffer[col.SourceTableField.ColumnIndex]
//   For non-query (ordinary Record) reads the buffers pass straight through unchanged,
//   so this is a no-op on the 99% table-read path.
//
// SCOPE: single- and multi-dataitem queries. Source rows are projected into query slots,
//   runtime and metadata filters are applied in their WHERE/HAVING phases, and aggregate
//   columns are grouped before the final TopNumberOfRows cap. Multi-dataitem execution is
//   delegated to the reflection-only AlRunner.QueryJoin assembly.
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Dynamics.Nav.Runtime;

namespace AlRunner.Patches;

public static partial class RecordPatches
{
    private static MethodInfo? _mTtdpFindImpl;
    private static MethodInfo? _mTtdpFindByPositionImpl;
    private static Type? _tFindTypeEnum;
    private static Type? _tReadOnlyRecordBuffer;
    private static ConstructorInfo? _ctorReadOnlyRecordBuffer;
    private static PropertyInfo? _pReqMetaAppObj;
    private static PropertyInfo? _pReqFindType;
    private static PropertyInfo? _pReqTopNumberOfRows;

    private static void EnsureQueryProjectionReflection(object tempProvider)
    {
        if (_mTtdpFindImpl != null) return;
        var ttdp = tempProvider.GetType(); // TempTableDataProvider
        _mTtdpFindImpl = ttdp.GetMethod("FindImplementation",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("TempTableDataProvider.FindImplementation not found");
        _mTtdpFindByPositionImpl = ttdp.GetMethod("FindByPositionImplementation",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("TempTableDataProvider.FindByPositionImplementation not found");

        var nclAsm = ttdp.Assembly;
        _tFindTypeEnum = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.FindType");
        _tReadOnlyRecordBuffer = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.ReadOnlyRecordBuffer")
            ?? throw new InvalidOperationException("ReadOnlyRecordBuffer not found");
        // public ReadOnlyRecordBuffer(NCLMetaApplicationObject metaApplicationObject, params NavValue[] immutableFields)
        var navValueArr = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NavValue")!.MakeArrayType();
        var metaAppObj = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaApplicationObject")!;
        _ctorReadOnlyRecordBuffer = _tReadOnlyRecordBuffer.GetConstructor(new[] { metaAppObj, navValueArr })
            ?? throw new InvalidOperationException("ReadOnlyRecordBuffer(NCLMetaApplicationObject, NavValue[]) ctor not found");

        var reqBase = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.DataProviderRequest")!;
        _pReqMetaAppObj = reqBase.GetProperty("MetaApplicationObject", BindingFlags.Public | BindingFlags.Instance)!;
        var findReq = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.FindProviderRequest")!;
        _pReqFindType = findReq.GetProperty("FindType", BindingFlags.Public | BindingFlags.Instance)!;
        _pReqTopNumberOfRows = findReq.GetProperty("TopNumberOfRowsToReturn", BindingFlags.Public | BindingFlags.Instance)!;
    }

    /// <summary>
    /// Replacement for TempTableDataProvider.Find(FindProviderRequest, Func&lt;bool&gt;).
    /// Mirrors the original: FindImplementation(request), Take(1) when FindType.FirstOnly,
    /// then projects query columns when the request targets a query.
    /// </summary>
    public static IEnumerable<ReadOnlyRecordBuffer> TempTableDataProvider_Find(
        object self, object request, Func<bool>? onlyCurrentKeyNeededForNextRow)
    {
        EnsureQueryProjectionReflection(self);
        var execRequest = TranslateQueryFilters(request);
        var raw = (IEnumerable<ReadOnlyRecordBuffer>)_mTtdpFindImpl!.Invoke(self, new[] { execRequest })!;
        raw = ApplyFirstOnly(request, raw);
        return ProjectIfQuery(request, raw);
    }

    /// <summary>
    /// Replacement for TempTableDataProvider.FindFromPosition(PositionedFindProviderRequest, Func&lt;bool&gt;).
    /// </summary>
    public static IEnumerable<ReadOnlyRecordBuffer> TempTableDataProvider_FindFromPosition(
        object self, object request, Func<bool>? onlyCurrentKeyNeededForNextRow)
    {
        EnsureQueryProjectionReflection(self);
        var execRequest = TranslateQueryFilters(request);
        var raw = (IEnumerable<ReadOnlyRecordBuffer>)_mTtdpFindByPositionImpl!.Invoke(self, new[] { execRequest })!;
        raw = ApplyFirstOnly(request, raw);
        return ProjectIfQuery(request, raw);
    }

    private static MethodInfo? _mGetDataAccessForTable_Orig;
    private static PropertyInfo? _pQueryDefIncludedTables;
    private static PropertyInfo? _pDataAccessDataProvider;
    private static PropertyInfo? _pNclMetaQueryQueryDefinition2;

    /// <summary>
    /// Replacement for DataAccessSource.GetDataAccessForQuery(NCLMetaQueryDefinition).
    ///
    /// Single-dataitem queries map to ONE in-memory DataAccess (the temp provider holding
    /// the inserted rows) — return it (original behaviour). Multi-dataitem (join) queries
    /// map each included table to its OWN temp DataAccess; the real engine throws
    /// QueriesBetweenDataSourcesNotSupported because an in-memory cross-provider join is not
    /// supported. The FAITHFUL result of a join over EMPTY tables is zero rows (BC's SQL
    /// join produces no rows when either side is empty). So: if every included table is
    /// empty, return the ROOT (driving) table's DataAccess — FindAsync then runs the query
    /// over an empty driving table and the projection layer yields no rows (correct). If any
    /// included table actually has rows, an in-memory join WOULD change the result, so we
    /// throw RunnerOutOfScopeException rather than silently return wrong/unjoined data.
    /// </summary>
    public static object DataAccessSource_GetDataAccessForQuery(object self, object queryDefinition)
    {
        EnsureGetDataAccessForQueryReflection(self);

        var includedTables = (System.Collections.IEnumerable)_pQueryDefIncludedTables!.GetValue(queryDefinition)!;
        var tableList = includedTables.Cast<object>().ToList();

        // Resolve each included table's DataAccess via the (already-hooked) per-table route.
        var accesses = new List<object>();
        foreach (var t in tableList)
            accesses.Add(NavDataAccessSource_GetDataAccessForTable(self, (NCLMetaTable)t, false));

        if (accesses.Count == 0)
            return NavDataAccessSource_GetDataAccessForTable(self, null!, false); // shouldn't happen; let original-style path surface

        // Single data source (single dataitem, or all tables already share one DataAccess) —
        // original behaviour: return that single instance. (A genuinely single-dataitem
        // query lands here; the projection layer reshapes the one table's rows.)
        bool singleDataItem = !IsMultiDataItemQuery(queryDefinition);
        bool allSame = accesses.All(a => ReferenceEquals(a, accesses[0]));
        if (singleDataItem && allSame)
            return accesses[0];

        // Multi-dataitem JOIN. We execute the join ourselves in the projection layer
        // (ExecuteJoinQuery), reading every dataitem's table via this DataAccessSource.
        // Stash the source keyed by the query definition so the projection layer can reach
        // the sibling tables, and return the ROOT table's DataAccess so FindAsync still has
        // a provider to call into (whose query-shaped Find we intercept and replace with the
        // joined result set). See RecordPatches.QueryJoin.cs.
        StashJoinSource(queryDefinition, self);
        QLog($"GetDataAccessForQuery: {tableList.Count}-dataitem join → in-memory join via root DataAccess");
        return accesses[0];
    }

    private static void EnsureGetDataAccessForQueryReflection(object dataAccessSource)
    {
        if (_pQueryDefIncludedTables != null) return;
        var nclAsm = dataAccessSource.GetType().Assembly;
        const string rt = "Microsoft.Dynamics.Nav.Runtime.";
        var tQueryDef = nclAsm.GetType(rt + "NCLMetaQueryDefinition")!;
        _pQueryDefIncludedTables = tQueryDef.GetProperty("IncludedTables",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("NCLMetaQueryDefinition.IncludedTables not found");
        var tDataAccess = nclAsm.GetType(rt + "DataAccess")!;
        _pDataAccessDataProvider = tDataAccess.GetProperty("DataProvider",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    }

    // Does the in-memory temp provider behind <paramref name="table"/> hold any row?
    // Uses the provider's own FindImplementation with a table-shaped (NOT query) request so
    // no projection happens — we only need to know if a single row exists.
    private static bool TableHasAnyRow(object dataAccessSource, NCLMetaTable table)
    {
        try
        {
            var dataAccess = NavDataAccessSource_GetDataAccessForTable(dataAccessSource, table, false);
            var provider = _pDataAccessDataProvider!.GetValue(dataAccess);
            if (provider == null) return false;
            EnsureQueryProjectionReflection(provider);
            var req = BuildTableFindAnyRequest(provider, table);
            if (req == null) return true; // can't build probe → assume non-empty (safer: throw OOS, not fake)
            var rows = (System.Collections.IEnumerable)_mTtdpFindImpl!.Invoke(provider, new[] { req })!;
            foreach (var _ in rows) return true;
            return false;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            QLog($"TableHasAnyRow({table?.TableName}) probe failed: {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace} → treating as non-empty");
            return true; // never silently claim empty on uncertainty
        }
    }

    private static ConstructorInfo? _ctorFindProviderRequestProbe;
    private static object? BuildTableFindAnyRequest(object provider, NCLMetaTable table)
    {
        var nclAsm = provider.GetType().Assembly;
        const string rt = "Microsoft.Dynamics.Nav.Runtime.";
        var tFindReq = nclAsm.GetType(rt + "FindProviderRequest")!;
        // Reuse the public FindProviderRequest ctor (the 13+-arg one used in QueryProjection).
        _ctorFindProviderRequestProbe ??= tFindReq.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length >= 13
                && c.GetParameters()[1].ParameterType.Name == "NCLMetaApplicationObject");
        if (_ctorFindProviderRequestProbe == null) return null;

        object? StaticMember(string typeName, string member)
        {
            var t = nclAsm.GetType(rt + typeName)!;
            return t.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                ?? t.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        }
        var emptyFam = StaticMember("FiltersAndMarks", "Empty");
        var emptyTfd = StaticMember("TableFilterDictionary", "Empty");
        var fieldListEmpty = StaticMember("FieldList", "Empty");
        var ps = _ctorFindProviderRequestProbe.GetParameters();
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            args[i] = ps[i].Name switch
            {
                "companyToken" => 0,
                "metaApplicationObject" => table,                 // table-shaped: no projection
                "lockState" => Enum.ToObject(ps[i].ParameterType, 0),
                "filtersAndMarks" => emptyFam,
                "globalAndSecurityFilters" => emptyTfd,
                "flowFieldSecurityFiltering" => ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null,
                "autoCalcFields" => null,
                "sortingFields" => null,
                "findType" => Enum.ToObject(_tFindTypeEnum!, FirstOnlyOrdinal()),
                "topNumberOfRowsToReturn" => 1,
                "skipNumberOfRows" => 0,
                "fastNumberOfRowsToReturn" => 1,
                "timeout" => null,
                "fieldLoadInfo" => null,
                _ => ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null)
            };
        }
        return _ctorFindProviderRequestProbe.Invoke(args);
    }

    // ── Query filter translation (SetRange / SetFilter on a query column) ──────────
    // NavQuery.SetRange/SetFilter store a FilterFieldDictionary keyed by the
    // NCLMetaQueryColumn, with each FilterExpression bound to that column's
    // ExpressionContext. The TempTableDataProvider filter visitor evaluates
    // `(NCLMetaField)expressionContext.Metadata` against the table buffer
    // (input[NCLMetaField.ColumnIndex]) — a query column is NOT an NCLMetaField, so the
    // raw filter never matches the table row. Real BC's SQL provider applies the filter
    // in the WHERE clause against the source column. We reproduce that: rebuild each
    // query-column-keyed filter so it targets the column's SourceTableField (the real
    // NCLMetaField) and re-key the dictionary by that field, then hand the temp provider
    // a table-shaped request it can evaluate. Single-dataitem only: every column maps to
    // one included table.
    private static Type? _tFiltersAndMarks;
    private static Type? _tFilterFieldDictionary;
    private static Type? _tUnaryFilterExpr;
    private static Type? _tBinaryFilterExpr;
    private static Type? _tRangeFilterExpr;
    private static Type? _tWildcardFilterExpr;
    private static Type? _tFilterExpr;
    private static Type? _tNavFieldMetadata;
    private static bool _filterReflectionReady;

    // For the extended-slot recomputation in ApplyJoinRuntimeFilters — mirrors
    // AlRunner.QueryJoin.JoinExecutor's own DataItems/QueryColumns/ColumnType reflection
    // (a SEPARATE, isolated assembly that cannot share these PropertyInfo handles).
    private static Type? _tNCLMetaQueryDefinition;
    private static Type? _tNCLMetaQueryDataItem;
    private static PropertyInfo? _pQueryDefDataItemsQ;
    private static PropertyInfo? _pDataItemQueryColumnsQ;
    private static PropertyInfo? _pDataItemTableFiltersAndMarksQ;
    private static PropertyInfo? _pColColumnTypeQ;
    private static PropertyInfo? _pColColumnIndexQ2;
    private static PropertyInfo? _pColIsAggregatedQ;
    private static PropertyInfo? _pNclMetaQueryColumnFilters;

    private static void EnsureFilterReflection()
    {
        if (_filterReflectionReady) return;
        var asm = _tReadOnlyRecordBuffer!.Assembly;
        const string rt = "Microsoft.Dynamics.Nav.Runtime.";
        _tFiltersAndMarks = asm.GetType(rt + "FiltersAndMarks");
        _tFilterFieldDictionary = asm.GetType(rt + "FilterFieldDictionary");
        EnsureFilterExpressionTypes(asm);
        _tNavFieldMetadata = asm.GetType(rt + "INavFieldMetadata");
        _tNCLMetaQueryColumn = asm.GetType(rt + "NCLMetaQueryColumn");
        _tNCLMetaQueryDefinition = asm.GetType(rt + "NCLMetaQueryDefinition");
        _tNCLMetaQueryDataItem = asm.GetType(rt + "NCLMetaQueryDataItem");
        const BindingFlags anyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        _pQueryDefDataItemsQ = _tNCLMetaQueryDefinition?.GetProperty("DataItems", anyInstance);
        _pDataItemQueryColumnsQ = _tNCLMetaQueryDataItem?.GetProperty("QueryColumns", anyInstance);
        _pDataItemTableFiltersAndMarksQ = _tNCLMetaQueryDataItem?.GetProperty(
            "TableFiltersAndMarks", anyInstance);
        _pColColumnTypeQ = _tNCLMetaQueryColumn?.GetProperty("ColumnType", anyInstance);
        _pColIsAggregatedQ = _tNCLMetaQueryColumn?.GetProperty("IsAggregated", anyInstance);
        _pNclMetaQueryColumnFilters = _tNCLMetaQuery?.GetProperty("ColumnFilters", anyInstance);
        _filterReflectionReady = true;
    }

    private static void EnsureFilterExpressionTypes(Assembly assembly)
    {
        const string rt = "Microsoft.Dynamics.Nav.Runtime.";
        _tUnaryFilterExpr ??= assembly.GetType(rt + "UnaryFilterExpression");
        _tBinaryFilterExpr ??= assembly.GetType(rt + "BinaryFilterExpression");
        _tRangeFilterExpr ??= assembly.GetType(rt + "RangeFilterExpression");
        _tWildcardFilterExpr ??= assembly.GetType(rt + "WildcardFilterExpression");
        _tFilterExpr ??= assembly.GetType(rt + "FilterExpression");
    }

    /// <summary>
    /// Recompute, for every QueryColumn across every DataItem of <paramref name="queryDef"/>, the
    /// slot it occupies in the join-projected row buffer AlRunner.QueryJoin.JoinExecutor
    /// produces — by reference identity, since neither assembly can hand the other a
    /// PropertyInfo/slot map directly (JoinExecutor is loaded in an isolated ALC specifically so
    /// its assembly never leaks an Ncl type into al-runner's own startup surface).
    ///
    /// MUST mirror JoinExecutor.BuildJoinProjectionPlan's two-pass algorithm EXACTLY (same
    /// dataitem enumeration order, same "normal columns first at their real ColumnIndex, then
    /// filter-only columns at sequential extra slots past the projected max" rule) — that
    /// algorithm is duplicated rather than shared for the isolation reason above, and duplicated
    /// logic that drifts is exactly the failure mode SCOPE-AUDIT-style comments warn about, so
    /// change both together.
    /// </summary>
    private static Dictionary<object, int> ComputeJoinColumnSlotMap(object queryDef)
    {
        var map = new Dictionary<object, int>();
        if (_pQueryDefDataItemsQ == null || _pDataItemQueryColumnsQ == null) return map;
        var dataItems = ((System.Collections.IEnumerable)_pQueryDefDataItemsQ.GetValue(queryDef)!).Cast<object>().ToList();

        int maxSlot = -1;
        foreach (var di in dataItems)
        {
            var cols = (_pDataItemQueryColumnsQ.GetValue(di) as System.Collections.IEnumerable)?.Cast<object>()
                ?? Enumerable.Empty<object>();
            foreach (var col in cols)
            {
                if (IsFilterOnlyColumnQ(col)) continue;
                _pColColumnIndexQ2 ??= _tNCLMetaQueryColumn!.GetProperty("ColumnIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
                var idx = (int)_pColColumnIndexQ2.GetValue(col)!;
                if (idx < 0) continue;
                map[col] = idx;
                if (idx > maxSlot) maxSlot = idx;
            }
        }

        int nextExtraSlot = maxSlot + 1;
        foreach (var di in dataItems)
        {
            var cols = (_pDataItemQueryColumnsQ.GetValue(di) as System.Collections.IEnumerable)?.Cast<object>()
                ?? Enumerable.Empty<object>();
            foreach (var col in cols)
            {
                if (!IsFilterOnlyColumnQ(col)) continue;
                map[col] = nextExtraSlot++;
            }
        }
        return map;
    }

    private static bool IsFilterOnlyColumnQ(object col)
        => _pColColumnTypeQ?.GetValue(col)?.ToString() == "FilterOnly";

    private static bool IsAggregatedColumnQ(object col)
        => _pColIsAggregatedQ?.GetValue(col) is true;

    /// <summary>
    /// If <paramref name="request"/> targets a query and carries query-column-keyed
    /// filters, returns a clone of the request with those filters re-keyed/re-targeted to
    /// the source table fields so the temp provider can evaluate them. Otherwise returns
    /// the request unchanged.
    /// </summary>
    private static object TranslateQueryFilters(object request)
    {
        var metaAppObj = _pReqMetaAppObj!.GetValue(request);
        if (metaAppObj == null || _tNCLMetaQuery == null || !_tNCLMetaQuery.IsInstanceOfType(metaAppObj))
            return request; // ordinary table read — nothing to translate.

        EnsureFilterReflection();
        var filtersAndMarks = request.GetType().GetProperty("FiltersAndMarks", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(request);
        var items = FilterItems(filtersAndMarks);

        var translatedTuples = SingleDataItemTableFilterItems(metaAppObj).ToList();
        bool anyTranslated = false;
        foreach (var item in items)
        {
            // Tuple<INavFieldMetadata, FilterExpression>
            var key = item!.GetType().GetProperty("Item1")!.GetValue(item);
            var expr = item.GetType().GetProperty("Item2")!.GetValue(item);
            if (key != null && _tNCLMetaQueryColumn != null && _tNCLMetaQueryColumn.IsInstanceOfType(key))
            {
                // Aggregate-column filters are HAVING predicates. They must not be pushed into
                // the table provider's WHERE phase; the projected result path evaluates them
                // after grouping instead.
                if (IsAggregatedColumnQ(key))
                {
                    anyTranslated = true;
                    continue;
                }
                var srcField = key.GetType().GetProperty("SourceTableField", BindingFlags.Public | BindingFlags.Instance)!.GetValue(key);
                if (srcField != null && expr != null)
                {
                    var srcCtx = srcField.GetType().GetProperty("ExpressionContext", BindingFlags.Public | BindingFlags.Instance)!.GetValue(srcField);
                    var retargeted = RetargetFilterExpression(expr, srcCtx!);
                    translatedTuples.Add(MakeFieldTuple(srcField, retargeted));
                    anyTranslated = true;
                    continue;
                }
            }
            translatedTuples.Add(item); // already table-keyed or no source — keep as-is.
        }
        if (!anyTranslated && translatedTuples.Count == items.Length) return request;

        // Build FilterFieldDictionary(IEnumerable<Tuple<INavFieldMetadata, FilterExpression>>)
        var newFilters = BuildFilterFieldDictionary(MergeFiltersOnSameField(translatedTuples));
        var markedRecords = filtersAndMarks == null
            ? null
            : _tFiltersAndMarks!.GetProperty("MarkedRecords", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(filtersAndMarks);
        var newFam = Activator.CreateInstance(_tFiltersAndMarks, newFilters, markedRecords)!;
        return CloneRequestWithFilters(request, newFam);
    }

    private static object[] FilterItems(object? filtersAndMarks)
    {
        if (filtersAndMarks == null) return Array.Empty<object>();
        var filters = _tFiltersAndMarks!.GetProperty("Filters", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(filtersAndMarks);
        if (filters == null) return Array.Empty<object>();
        return ((Array?)_tFilterFieldDictionary!.GetProperty(
                "Items", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(filters))?.Cast<object>().ToArray() ?? Array.Empty<object>();
    }

    private static IEnumerable<object> SingleDataItemTableFilterItems(object nclMetaQuery)
    {
        var queryDef = _tNCLMetaQuery!.GetProperty(
                "QueryDefinition", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(nclMetaQuery);
        if (queryDef == null || _pQueryDefDataItemsQ == null) yield break;

        var dataItems = ((IEnumerable)_pQueryDefDataItemsQ.GetValue(queryDef)!).Cast<object>().ToList();
        if (dataItems.Count != 1) yield break;

        var tableFiltersAndMarks = _pDataItemTableFiltersAndMarksQ?.GetValue(dataItems[0]);
        foreach (var item in FilterItems(tableFiltersAndMarks))
            yield return item;
    }

    private static List<object> MergeFiltersOnSameField(IEnumerable<object> tuples)
    {
        var merged = new List<object>();
        var indexByField = new Dictionary<object, int>(
            System.Collections.Generic.ReferenceEqualityComparer.Instance);
        foreach (var tuple in tuples)
        {
            var tupleType = tuple.GetType();
            var field = tupleType.GetProperty("Item1")!.GetValue(tuple);
            var expression = tupleType.GetProperty("Item2")!.GetValue(tuple);
            if (field == null || expression == null || !indexByField.TryGetValue(field, out var index))
            {
                if (field != null) indexByField[field] = merged.Count;
                merged.Add(tuple);
                continue;
            }

            var existingExpression = merged[index].GetType().GetProperty("Item2")!.GetValue(merged[index])!;
            merged[index] = MakeFieldTuple(field, AndFilterExpressions(existingExpression, expression));
        }
        return merged;
    }

    private static object AndFilterExpressions(object left, object right)
    {
        var expressionType = _tFilterExpr!.GetProperty(
            "ExpressionType", BindingFlags.Public | BindingFlags.Instance)!.PropertyType;
        var and = Enum.Parse(expressionType, "And");
        var ctor = _tBinaryFilterExpr!.GetConstructors()
            .Single(candidate => candidate.GetParameters().Length == 3
                && candidate.GetParameters()[0].ParameterType == expressionType);
        return ctor.Invoke(new[] { and, left, right });
    }

    private static Type? _tNCLMetaQueryColumn;

    private static object MakeFieldTuple(object field, object expr)
    {
        // Tuple<INavFieldMetadata, FilterExpression>
        var tupleType = typeof(Tuple<,>).MakeGenericType(_tNavFieldMetadata!, _tFilterExpr!);
        return Activator.CreateInstance(tupleType, field, expr)!;
    }

    private static object BuildFilterFieldDictionary(List<object> tuples)
    {
        var tupleType = typeof(Tuple<,>).MakeGenericType(_tNavFieldMetadata!, _tFilterExpr!);
        var arr = Array.CreateInstance(tupleType, tuples.Count);
        for (int i = 0; i < tuples.Count; i++) arr.SetValue(tuples[i], i);
        // FilterFieldDictionary(IEnumerable<Tuple<INavFieldMetadata, FilterExpression>>)
        var ienumType = typeof(IEnumerable<>).MakeGenericType(tupleType);
        var ctor = _tFilterFieldDictionary!.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == ienumType);
        return ctor.Invoke(new object[] { arr });
    }

    /// <summary>Rebuild a filter expression tree, retargeting Unary leaves to <paramref name="targetCtx"/>.</summary>
    internal static object RetargetFilterExpression(object expr, object targetCtx)
    {
        var t = expr.GetType();
        EnsureFilterExpressionTypes(t.Assembly);
        if (_tUnaryFilterExpr!.IsInstanceOfType(expr))
        {
            // new UnaryFilterExpression(FilterExpressionType, NavValue, FilterExpressionContext, valueToken, isConstInMetadata)
            var exprType = _tFilterExpr!.GetProperty("ExpressionType", BindingFlags.Public | BindingFlags.Instance)!.GetValue(expr);
            var value = t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)!.GetValue(expr);
            var ctor = _tUnaryFilterExpr.GetConstructors()
                .First(c => c.GetParameters().Length >= 3
                    && c.GetParameters()[0].ParameterType.Name == "FilterExpressionType");
            var ps = ctor.GetParameters();
            var args = new object?[ps.Length];
            args[0] = exprType; args[1] = value; args[2] = targetCtx;
            for (int i = 3; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null);
            return ctor.Invoke(args);
        }
        if (_tRangeFilterExpr!.IsInstanceOfType(expr))
        {
            var exprType = _tFilterExpr!.GetProperty("ExpressionType", BindingFlags.Public | BindingFlags.Instance)!.GetValue(expr);
            var lowValue = t.GetProperty("LowValue", BindingFlags.Public | BindingFlags.Instance)!.GetValue(expr);
            var highValue = t.GetProperty("HighValue", BindingFlags.Public | BindingFlags.Instance)!.GetValue(expr);
            var ctor = _tRangeFilterExpr.GetConstructors()
                .Single(candidate =>
                    candidate.GetParameters().Length == 4
                    && candidate.GetParameters()[0].ParameterType.Name == "FilterExpressionType"
                    && candidate.GetParameters()[3].ParameterType.Name == "FilterExpressionContext");
            return ctor.Invoke(new[] { exprType, lowValue, highValue, targetCtx });
        }
        if (_tWildcardFilterExpr!.IsInstanceOfType(expr))
        {
            var isNegated = t.GetProperty("IsNegated", BindingFlags.Public | BindingFlags.Instance)!.GetValue(expr);
            var pattern = t.GetProperty("Pattern", BindingFlags.Public | BindingFlags.Instance)!.GetValue(expr);
            var isCaseAndAccentInsensitive = t.GetProperty(
                "IsCaseAndAccentInsensitive", BindingFlags.Public | BindingFlags.Instance)!.GetValue(expr);
            var ctor = _tWildcardFilterExpr.GetConstructors()
                .Single(candidate =>
                    candidate.GetParameters().Length == 4
                    && candidate.GetParameters()[0].ParameterType == typeof(bool)
                    && candidate.GetParameters()[1].ParameterType == typeof(string)
                    && candidate.GetParameters()[2].ParameterType == typeof(bool)
                    && candidate.GetParameters()[3].ParameterType.Name == "FilterExpressionContext");
            return ctor.Invoke(new[] { isNegated, pattern, isCaseAndAccentInsensitive, targetCtx });
        }
        if (_tBinaryFilterExpr!.IsInstanceOfType(expr))
        {
            var exprType = _tFilterExpr!.GetProperty("ExpressionType", BindingFlags.Public | BindingFlags.Instance)!.GetValue(expr);
            var left = t.GetProperty("Left", BindingFlags.Public | BindingFlags.Instance)!.GetValue(expr);
            var right = t.GetProperty("Right", BindingFlags.Public | BindingFlags.Instance)!.GetValue(expr);
            var newLeft = RetargetFilterExpression(left!, targetCtx);
            var newRight = RetargetFilterExpression(right!, targetCtx);
            var ctor = _tBinaryFilterExpr.GetConstructors()
                .First(c => c.GetParameters().Length == 3 && c.GetParameters()[0].ParameterType.Name == "FilterExpressionType");
            return ctor.Invoke(new object?[] { exprType, newLeft, newRight });
        }
        // Other expression kinds (wildcard/fieldEqualsField/etc.) are not produced by
        // the currently supported single-column query filters.
        return expr;
    }

    private static object CloneRequestWithFilters(object request, object newFiltersAndMarks)
    {
        // Both FindProviderRequest and PositionedFindProviderRequest share the same field
        // set; reconstruct via the full ctor pulling every other field off the original.
        var t = request.GetType();
        object Get(string n) => t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance)!.GetValue(request)!;
        var isPositioned = t.Name == "PositionedFindProviderRequest";
        var ctor = t.GetConstructors().First(c =>
        {
            var ps = c.GetParameters();
            return ps.Length >= 13 && ps[1].ParameterType.Name == "NCLMetaApplicationObject";
        });
        var ps = ctor.GetParameters();
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            args[i] = ps[i].Name switch
            {
                "companyToken" => Get("CompanyToken"),
                "metaApplicationObject" => Get("MetaApplicationObject"),
                "lockState" => Get("LockState"),
                "filtersAndMarks" => newFiltersAndMarks,
                "globalAndSecurityFilters" => GetOrNull("GlobalAndSecurityFilters"),
                "flowFieldSecurityFiltering" => Get("FlowFieldSecurityFiltering"),
                "autoCalcFields" => GetOrNull("AutoCalcFields"),
                "sortingFields" => GetOrNull("SortingFields"),
                "findType" => Get("FindType"),
                "startingPosition" => isPositioned ? GetOrNull("StartingPosition") : null,
                "includeCurrent" => isPositioned ? Get("IncludeCurrent") : false,
                "topNumberOfRowsToReturn" => Get("TopNumberOfRowsToReturn"),
                "skipNumberOfRows" => Get("SkipNumberOfRows"),
                "fastNumberOfRowsToReturn" => Get("FastNumberOfRowsToReturn"),
                "timeout" => GetOrNull("Timeout"),
                "fieldLoadInfo" => GetOrNull("FieldLoadInfo"),
                _ => ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null)
            };
        }
        return ctor.Invoke(args);

        object? GetOrNull(string n) => t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance)?.GetValue(request);
    }

    private static IEnumerable<ReadOnlyRecordBuffer> ApplyFirstOnly(object request, IEnumerable<ReadOnlyRecordBuffer> rows)
    {
        // Original Find/FindFromPosition return enumerable.Take(1) when FindType == FirstOnly.
        var findType = _pReqFindType!.GetValue(request);
        var firstOnly = findType != null && Convert.ToInt32(findType) == FirstOnlyOrdinal();
        return firstOnly ? rows.Take(1) : rows;
    }

    private static int _firstOnlyOrdinal = -1;
    private static int FirstOnlyOrdinal()
    {
        if (_firstOnlyOrdinal < 0)
            _firstOnlyOrdinal = Convert.ToInt32(Enum.Parse(_tFindTypeEnum!, "FirstOnly"));
        return _firstOnlyOrdinal;
    }

    private static IEnumerable<ReadOnlyRecordBuffer> ProjectIfQuery(object request, IEnumerable<ReadOnlyRecordBuffer> rows)
    {
        var metaAppObj = _pReqMetaAppObj!.GetValue(request);
        if (metaAppObj == null || _tNCLMetaQuery == null || !_tNCLMetaQuery.IsInstanceOfType(metaAppObj))
            return rows; // ordinary table read — pass through unchanged.

        // Multi-dataitem JOIN: ignore the single-table `rows` (the root scan the engine
        // requested) and produce the joined+projected result set ourselves by reading
        // every dataitem's table. See RecordPatches.QueryJoin.cs.
        var queryDef = _tNCLMetaQuery.GetProperty("QueryDefinition", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(metaAppObj);
        if (queryDef != null && IsMultiDataItemQuery(queryDef))
        {
            // The isolated executor returns boxed ReadOnlyRecordBuffers (non-generic
            // IEnumerable) so its assembly carries no Ncl type in its public surface; cast
            // back here, where QueryProjection.cs already (necessarily) references the type.
            var joined = ExecuteJoinQuery(metaAppObj).Cast<ReadOnlyRecordBuffer>();
            // Apply the live NavQuery's runtime filters (SetRange/SetFilter) as a POST-projection
            // pass. The single-dataitem path pushes these into the temp provider's WHERE
            // (TranslateQueryFilters); the join executor reads each dataitem's table with only its
            // STATIC metadata filters, so runtime filters must be evaluated against the projected
            // rows here. Without this the join returns UNFILTERED rows (a correctness bug). Done
            // before Top so the cap applies to the filtered set, matching SQL TOP-after-WHERE.
            joined = ApplyProjectedQueryFilters(metaAppObj, queryDef, request, joined, aggregatePhase: false);
            joined = FinalizeQueryRows(metaAppObj, joined).Cast<ReadOnlyRecordBuffer>();
            joined = ApplyProjectedQueryFilters(metaAppObj, queryDef, request, joined, aggregatePhase: true);
            var topJ = _pReqTopNumberOfRows!.GetValue(request);
            int topNJ = topJ == null ? 0 : Convert.ToInt32(topJ);
            return topNJ > 0 ? joined.Take(topNJ) : joined;
        }

        var projected = ProjectQueryRows(metaAppObj, rows);
        projected = ApplyProjectedQueryFilters(
            metaAppObj, queryDef!, request, projected, aggregatePhase: false,
            includeRequestFilters: false);
        projected = FinalizeQueryRows(metaAppObj, projected).Cast<ReadOnlyRecordBuffer>();
        projected = ApplyProjectedQueryFilters(
            metaAppObj, queryDef!, request, projected, aggregatePhase: true);

        // Query.TopNumberOfRowsToReturn caps the finalized dataset. NavQuery passes it through
        // the request; the temp provider never enforces this query-level cap.
        var top = _pReqTopNumberOfRows!.GetValue(request);
        int topN = top == null ? 0 : Convert.ToInt32(top);
        return topN > 0 ? projected.Take(topN) : projected;
    }

    private static MethodInfo? _mFilterExprEvaluate;

    /// <summary>
    /// Apply the live NavQuery's runtime filters (the request's FiltersAndMarks, keyed by
    /// NCLMetaQueryColumn) to the already-projected join rows. Each filter's FilterExpression
    /// is evaluated — using BC's own <c>FilterExpression.Evaluate(NavValue, ISortingRulesProvider)</c>,
    /// so range / &lt;&gt; / &amp; / | semantics match real BC exactly — against the NavValue in the
    /// column's projection slot (NCLMetaQueryColumn.ColumnIndex). Rows failing any filter are
    /// dropped. If a filtered column is NOT projected (no result slot, ColumnIndex &lt; 0, or out
    /// of range) we cannot evaluate it post-projection, so we throw RunnerOutOfScopeException
    /// rather than silently return wrong rows (loud-failures rule).
    /// </summary>
    private static IEnumerable<ReadOnlyRecordBuffer> ApplyProjectedQueryFilters(
        object nclMetaQuery,
        object queryDef,
        object request,
        IEnumerable<ReadOnlyRecordBuffer> rows,
        bool aggregatePhase,
        bool includeRequestFilters = true)
    {
        EnsureFilterReflection();
        var fam = request.GetType().GetProperty("FiltersAndMarks", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(request);
        var requestItems = Array.Empty<object>();
        if (fam != null)
        {
            var filters = _tFiltersAndMarks!.GetProperty("Filters", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(fam);
            if (filters != null)
                requestItems = ((Array?)_tFilterFieldDictionary!.GetProperty(
                        "Items", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?
                    .GetValue(filters))?.Cast<object>().ToArray() ?? Array.Empty<object>();
        }

        var requestKeys = requestItems
            .Select(item => item.GetType().GetProperty("Item1")!.GetValue(item))
            .Where(key => key != null)
            .Cast<object>()
            .ToHashSet(System.Collections.Generic.ReferenceEqualityComparer.Instance);

        var staticItems = ((_pNclMetaQueryColumnFilters?.GetValue(nclMetaQuery) as System.Collections.IEnumerable)?
            .Cast<object>() ?? Enumerable.Empty<object>())
            // SetRange/SetFilter on a query column replaces its design-time ColumnFilter.
            .Where(item =>
            {
                var key = item.GetType().GetProperty("Item1")!.GetValue(item);
                return key != null && !requestKeys.Contains(key);
            });

        var items = (includeRequestFilters ? requestItems : Array.Empty<object>())
            .Concat(staticItems)
            .ToArray();
        if (items.Length == 0) return rows;

        // Build (projectionSlot, FilterExpression) pairs.
        //
        // NCLMetaQueryColumn.ColumnIndex CANNOT distinguish a filter-only column (one declared
        // only via `filter(...)`, or a bare join-key field with no declared column at all — see
        // Query 777's own "User Security ID") from a genuinely-projected column at slot 0: BC's
        // own runtime ctor only assigns ColumnIndex when the column isn't FilterOnly, so a
        // filter-only column's ColumnIndex is left at the CLR default (0) — the same value a
        // real slot-0 column has. Reading it naively made every runtime filter on a filter-only
        // column alias onto whatever real column happened to land in slot 0, so a Guid-typed
        // filter (Query 777's "User Security ID") got compared against an unrelated Integer
        // column's value and threw NavNCLInvalidComparisonException instead of ever being
        // evaluated as a filter. ComputeJoinColumnSlotMap gives filter-only columns their OWN
        // dedicated extra slots (mirroring the ones JoinExecutor.BuildJoinProjectionPlan already
        // populates them into), so this now evaluates against the real filtered value.
        var slotMap = ComputeJoinColumnSlotMap(queryDef);
        var conds = new List<(int slot, object expr)>();
        foreach (var item in items)
        {
            // Tuple<INavFieldMetadata, FilterExpression>
            var key = item.GetType().GetProperty("Item1")!.GetValue(item);
            var expr = item.GetType().GetProperty("Item2")!.GetValue(item);
            if (expr == null) continue;
            if (key == null || _tNCLMetaQueryColumn == null || !_tNCLMetaQueryColumn.IsInstanceOfType(key))
                // A non-query-column key on a query request should not occur; refuse to guess.
                throw new AlRunner.Infrastructure.RunnerOutOfScopeException(
                    "NavQuery (projected filter)",
                    "query-filter-on-nonprojected-column — a query filter is keyed by a " +
                    $"non-query-column ({key?.GetType().Name ?? "null"}); cannot evaluate post-projection; see docs/scope.md");
            if (IsAggregatedColumnQ(key) != aggregatePhase)
                continue;
            if (!slotMap.TryGetValue(key, out var slot))
                // The filtered column isn't in ANY dataitem's QueryColumns of this query
                // definition — should not occur (the filter dictionary is keyed by columns that
                // came from this same query), but refuse to guess rather than silently drop it.
                throw new AlRunner.Infrastructure.RunnerOutOfScopeException(
                    "NavQuery (projected filter)",
                    "query-filter-unresolved-column — a query filter's column could not be " +
                    "located in the query's own DataItems/QueryColumns; see docs/scope.md");
            conds.Add((slot, expr));
        }
        if (conds.Count == 0) return rows;

        var session = TryGetCurrentSession(nclMetaQuery);
        return rows.Where(row =>
        {
            foreach (var (slot, expr) in conds)
            {
                if (slot >= row.FieldCount)
                    throw new AlRunner.Infrastructure.RunnerOutOfScopeException(
                        "NavQuery (multi-dataitem join)",
                        $"query-join-runtime-filter-on-nonprojected-column — filtered slot {slot} is outside the " +
                        $"projected row (FieldCount {row.FieldCount}); cannot evaluate post-projection; see docs/scope.md");
                var navValue = row[slot];
                if (!EvaluateFilterExpression(expr, navValue, session))
                    return false;
            }
            return true;
        });
    }

    /// <summary>Invoke BC's FilterExpression.Evaluate(NavValue, ISortingRulesProvider) by reflection.</summary>
    private static bool EvaluateFilterExpression(object expr, object navValue, object? sortingRules)
    {
        _mFilterExprEvaluate ??= _tFilterExpr!.GetMethod("Evaluate", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("FilterExpression.Evaluate(NavValue, ISortingRulesProvider) not found");
        return (bool)_mFilterExprEvaluate.Invoke(expr, new[] { navValue, sortingRules })!;
    }

    private static PropertyInfo? _pNavCurrentThreadSession;
    private static bool _navCurrentThreadResolved;

    /// <summary>
    /// The current NavSession (which implements ISortingRulesProvider) — the same sorting-rules
    /// provider BC passes to FilterExpression.Evaluate on the real WHERE path. Used only by the
    /// Text/Code-collation comparison branch of FilterExpressionContext.Compare; null is tolerated
    /// for numeric/integer comparisons. Resolved via NavCurrentThread.Session.
    /// </summary>
    private static object? TryGetCurrentSession(object anyNclTyped)
    {
        if (!_navCurrentThreadResolved)
        {
            _navCurrentThreadResolved = true;
            var nclAsm = anyNclTyped.GetType().Assembly;
            var tNavCurrentThread = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NavCurrentThread");
            _pNavCurrentThreadSession = tNavCurrentThread?.GetProperty("Session",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
        }
        try { return _pNavCurrentThreadSession?.GetValue(null); }
        catch { return null; }
    }

    // Cached per NCLMetaQuery: the (queryColumnIndex -> tableFieldColumnIndex) projection map
    // and the result slot count.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ProjectionPlan> _projectionPlans = new();

    private sealed class ProjectionPlan
    {
        public int SlotCount;
        // map[i] = (queryResultSlot, tableFieldSlot); a value of -1 tableFieldSlot means
        // the column has no NCLMetaField source (aggregate/const) → leave at default.
        public (int querySlot, int tableSlot)[] Map = Array.Empty<(int, int)>();
    }

    private static IEnumerable<ReadOnlyRecordBuffer> ProjectQueryRows(object nclMetaQuery, IEnumerable<ReadOnlyRecordBuffer> rows)
    {
        var plan = _projectionPlans.GetValue(nclMetaQuery, BuildProjectionPlan);
        foreach (var row in rows)
        {
            var fields = new object?[plan.SlotCount];
            foreach (var (querySlot, tableSlot) in plan.Map)
            {
                if (tableSlot < 0 || tableSlot >= row.FieldCount) continue; // unsupported column → default
                fields[querySlot] = row[tableSlot];
            }
            // ReadOnlyRecordBuffer(NCLMetaApplicationObject, params NavValue[])
            yield return (ReadOnlyRecordBuffer)_ctorReadOnlyRecordBuffer!.Invoke(
                new object?[] { nclMetaQuery, ToNavValueArray(fields) });
        }
    }

    private static Type? _tNavValue;
    private static Array ToNavValueArray(object?[] values)
    {
        _tNavValue ??= _tReadOnlyRecordBuffer!.Assembly.GetType("Microsoft.Dynamics.Nav.Runtime.NavValue")!;
        var arr = Array.CreateInstance(_tNavValue, values.Length);
        for (int i = 0; i < values.Length; i++) arr.SetValue(values[i], i);
        return arr;
    }

    private static ProjectionPlan BuildProjectionPlan(object nclMetaQuery)
    {
        // queryDef = nclMetaQuery.QueryDefinition; columns = queryDef.Columns
        var queryDef = _tNCLMetaQuery!.GetProperty("QueryDefinition", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(nclMetaQuery)!;
        var columns = (IEnumerable)queryDef.GetType()
            .GetProperty("Columns", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(queryDef)!;

        var map = new List<(int, int)>();
        int maxSlot = -1;
        foreach (var col in columns)
        {
            var ct = col.GetType(); // NCLMetaQueryColumn
            int querySlot = (int)ct.GetProperty("ColumnIndex", BindingFlags.Public | BindingFlags.Instance)!.GetValue(col)!;
            if (querySlot > maxSlot) maxSlot = querySlot;

            int tableSlot = -1;
            // SourceTableField is the NCLMetaField backing this column (null/throws for
            // aggregate/const columns — treat those as unsupported → leave default).
            try
            {
                var srcField = ct.GetProperty("SourceTableField", BindingFlags.Public | BindingFlags.Instance)!.GetValue(col);
                if (srcField != null)
                    tableSlot = (int)srcField.GetType().GetProperty("ColumnIndex", BindingFlags.Public | BindingFlags.Instance)!.GetValue(srcField)!;
            }
            catch { tableSlot = -1; }
            map.Add((querySlot, tableSlot));
        }
        return new ProjectionPlan { SlotCount = maxSlot + 1, Map = map.ToArray() };
    }
}
