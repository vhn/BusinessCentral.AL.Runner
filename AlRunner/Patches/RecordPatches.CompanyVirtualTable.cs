using System.Runtime.CompilerServices;
using AlRunner.Infrastructure;
using Microsoft.Dynamics.Nav.Runtime;

namespace AlRunner.Patches;

public static partial class RecordPatches
{
    internal const int CompanyVirtualTableId = 2000000006;

    private static readonly ConditionalWeakTable<object, object> _companyPopulatedProviders = new();

    private static bool IsCompanyVirtualTable(NCLMetaTable? table)
        => table is { TableId: CompanyVirtualTableId };

    private static void PopulateCompanyVirtualTable(
        object dataAccess,
        NCLMetaTable metaTable,
        object session)
    {
        EnsureAllObjReflection(metaTable);
        EnsureDataAccessProviderReflection(dataAccess);

        var provider = _pDataAccessDataProvider!.GetValue(dataAccess)
            ?? throw new RunnerOutOfScopeException(
                "Company (virtual table 2000000006)",
                "company-virtual-table — Company data access has no in-memory provider; see docs/scope.md");

        if (_companyPopulatedProviders.TryGetValue(provider, out _))
            return;

        var companyName = CompanyAccessPatches.SessionCompanyName(session);
        if (string.IsNullOrEmpty(companyName))
            throw new InvalidOperationException("The runner session has no current company name");

        ClearVirtualBit(metaTable);
        InsertVirtualRow(
            provider,
            metaTable,
            new object[] { CompanyVirtualTableId, 0, 0, 0 },
            field => BuildCompanyValue(field, companyName));

        _companyPopulatedProviders.Add(provider, new object());
    }

    private static object? BuildCompanyValue(NCLMetaField field, string companyName)
    {
        object? Text(string value) => _aovNavTextCreateTruncated!.Invoke(
            null,
            new object?[] { field.FieldDefinedLength, value });

        return NormalizeObjectTypeName(field.FieldName ?? string.Empty) switch
        {
            "name" or "displayname" => Text(companyName),
            _ => _aovGetDefaultNavValue!.Invoke(null, new object?[] { field, false }),
        };
    }
}
