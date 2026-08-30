using System.Runtime.CompilerServices;
using AlRunner.Infrastructure;
using Microsoft.Dynamics.Nav.Runtime;

namespace AlRunner.Patches;

public static partial class RecordPatches
{
    internal const int FeatureKeySystemTableId = 2000000211;

    private static readonly ConditionalWeakTable<object, object> _featureKeyPopulatedProviders = new();

    private static bool IsFeatureKeySystemTable(NCLMetaTable? table)
        => table is { TableId: FeatureKeySystemTableId };

    private static void PopulateFeatureKeySystemTable(object dataAccess, NCLMetaTable metaTable)
    {
        EnsureAllObjReflection(metaTable);
        EnsureDataAccessProviderReflection(dataAccess);

        var provider = _pDataAccessDataProvider!.GetValue(dataAccess)
            ?? throw new RunnerOutOfScopeException(
                "Feature Key (system table 2000000211)",
                "new-company-feature-key — Feature Key data access has no in-memory provider; see docs/scope.md");

        if (_featureKeyPopulatedProviders.TryGetValue(provider, out _))
            return;

        InsertVirtualRow(
            provider,
            metaTable,
            new object[] { FeatureKeySystemTableId, 0, 0, 0 },
            BuildFeatureKeyValue);

        _featureKeyPopulatedProviders.Add(provider, new object());
    }

    private static object? BuildFeatureKeyValue(NCLMetaField field)
    {
        switch (NormalizeObjectTypeName(field.FieldName ?? string.Empty))
        {
            case "id":
                return _aovNavTextCreateTruncated!.Invoke(
                    null,
                    new object?[] { field.FieldDefinedLength, "SalesPrices" });
            case "enabled":
                var ordinal = ResolveOptionOrdinalByName(field, "All Users");
                if (ordinal < 0)
                    throw new InvalidOperationException(
                        $"Feature Key.Enabled has no 'All Users' option in this BC artifact ('{field.FieldOptionMetadata?.OptionString}')");
                return _aovNavOptionCreate!.Invoke(
                    null,
                    new object?[] { field.FieldOptionMetadata, ordinal });
            default:
                return _aovGetDefaultNavValue!.Invoke(null, new object?[] { field, false });
        }
    }
}
