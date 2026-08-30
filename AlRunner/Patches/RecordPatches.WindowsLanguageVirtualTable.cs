using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Dynamics.Nav.Runtime;

namespace AlRunner.Patches;

public static partial class RecordPatches
{
    internal const int WindowsLanguageVirtualTableId = 2000000045;

    private static readonly ConditionalWeakTable<object, object> _windowsLanguagePopulatedProviders = new();

    private static bool IsWindowsLanguageVirtualTable(NCLMetaTable? table)
        => table is { TableId: WindowsLanguageVirtualTableId };

    private static void PopulateWindowsLanguageVirtualTable(object dataAccess, NCLMetaTable metaTable)
    {
        EnsureAllObjReflection(metaTable);
        EnsureDataAccessProviderReflection(dataAccess);

        var provider = _pDataAccessDataProvider!.GetValue(dataAccess)
            ?? throw new InvalidOperationException(
                "Windows Language data access has no in-memory provider");
        if (_windowsLanguagePopulatedProviders.TryGetValue(provider, out _))
            return;

        ClearVirtualBit(metaTable);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                     .Where(culture => culture.LCID is > 0 and not 4096)
                     .GroupBy(culture => culture.LCID)
                     .Select(group => group.First())
                     .OrderBy(culture => culture.LCID))
        {
            InsertVirtualRow(
                provider,
                metaTable,
                new object[] { WindowsLanguageVirtualTableId, culture.LCID, 0, 0 },
                field => BuildWindowsLanguageValue(field, culture));
        }

        _windowsLanguagePopulatedProviders.Add(provider, new object());
    }

    private static object? BuildWindowsLanguageValue(NCLMetaField field, CultureInfo culture)
    {
        object? Integer(int value) => _aovNavIntegerCreate!.Invoke(null, new object?[] { value });
        object? Text(string value) => _aovNavTextCreateTruncated!.Invoke(
            null,
            new object?[] { field.FieldDefinedLength, value });

        return NormalizeObjectTypeName(field.FieldName ?? string.Empty) switch
        {
            "languageid" => Integer(culture.LCID),
            "primarylanguageid" => Integer(culture.Parent.LCID),
            "name" => Text(culture.EnglishName),
            "abbreviatedname" => Text(culture.ThreeLetterWindowsLanguageName.ToUpperInvariant()),
            "primarycodepage" => Text(culture.TextInfo.ANSICodePage.ToString(CultureInfo.InvariantCulture)),
            "languagetag" => Text(culture.Name),
            _ => _aovGetDefaultNavValue!.Invoke(null, new object?[] { field, false }),
        };
    }
}
