// Managed provider for the CodeUnit Metadata (2000000137) virtual table.
//
// The service tier derives these rows from the installed application inventory. The
// standalone runner previously routed the table to an empty in-memory provider, so valid
// TableRelation checks against precompiled codeunits failed. That could abort company
// initialization before later OnCompanyInitialize subscribers had run.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using AlRunner.Infrastructure;
using Microsoft.Dynamics.Nav.Runtime;

namespace AlRunner.Patches;

public static partial class RecordPatches
{
    internal const int CodeunitMetadataVirtualTableId = 2000000137;

    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<int, byte>>
        _codeunitMetadataPopulatedByProvider = new();

    private static bool IsCodeunitMetadataVirtualTable(NCLMetaTable? table)
        => table is { TableId: CodeunitMetadataVirtualTableId };

    private static void PopulateCodeunitMetadataVirtualTable(object dataAccess, NCLMetaTable metaTable)
    {
        EnsureAllObjReflection(metaTable);
        EnsureDataAccessProviderReflection(dataAccess);

        var provider = _pDataAccessDataProvider!.GetValue(dataAccess)
            ?? throw new RunnerOutOfScopeException(
                "CodeUnit Metadata (virtual table 2000000137)",
                "codeunit-metadata-virtual-table — data access has no in-memory provider; see docs/scope.md");

        var populated = _codeunitMetadataPopulatedByProvider.GetValue(
            provider, static _ => new ConcurrentDictionary<int, byte>());

        foreach (var (kind, id, name, _) in EnumerateKnownAlObjects())
        {
            if (id <= 0 || NormalizeObjectTypeName(kind) != "codeunit") continue;
            if (!populated.TryAdd(id, 0)) continue;

            InsertVirtualRow(
                provider,
                metaTable,
                new object[] { CodeunitMetadataVirtualTableId, id, 0, 0 },
                field => BuildCodeunitMetadataValue(field, id, name));
        }
    }

    private static object? BuildCodeunitMetadataValue(NCLMetaField field, int id, string name)
    {
        return NormalizeObjectTypeName(field.FieldName ?? string.Empty) switch
        {
            "id" => _aovNavIntegerCreate!.Invoke(null, new object?[] { id }),
            "name" => _aovNavTextCreateTruncated!.Invoke(
                null, new object?[] { field.FieldDefinedLength, name ?? string.Empty }),
            _ => _aovGetDefaultNavValue!.Invoke(null, new object?[] { field, false }),
        };
    }
}
