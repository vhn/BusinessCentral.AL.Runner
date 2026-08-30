using System.Runtime.CompilerServices;
using AlRunner.Infrastructure;
using Microsoft.Dynamics.Nav.Runtime;

namespace AlRunner.Patches;

public static partial class RecordPatches
{
    internal const int DateVirtualTableId = 2000000007;
    internal const int TimeZoneVirtualTableId = 2000000164;

    // Time Zone."No." is persisted by BC applications even though the table is virtual.
    // The service tier assigns it by enumerating Windows time zones in this order. Unix
    // returns IANA zones instead, so keep the Windows sequence stable on non-Windows hosts.
    // Keep retired compatibility entries in place: existing AL data can refer to these
    // sequence values even when the host's current ICU database no longer exposes the ID.
    private static readonly string[] WindowsTimeZoneIds =
    [
        "Dateline Standard Time",
        "UTC-11",
        "Aleutian Standard Time",
        "Hawaiian Standard Time",
        "Marquesas Standard Time",
        "Alaskan Standard Time",
        "UTC-09",
        "Pacific Standard Time (Mexico)",
        "UTC-08",
        "Pacific Standard Time",
        "US Mountain Standard Time",
        "Mountain Standard Time (Mexico)",
        "Mountain Standard Time",
        "Yukon Standard Time",
        "Central America Standard Time",
        "Central Standard Time",
        "Easter Island Standard Time",
        "Central Standard Time (Mexico)",
        "Canada Central Standard Time",
        "SA Pacific Standard Time",
        "Eastern Standard Time (Mexico)",
        "Eastern Standard Time",
        "Haiti Standard Time",
        "Cuba Standard Time",
        "US Eastern Standard Time",
        "Turks And Caicos Standard Time",
        "Paraguay Standard Time",
        "Atlantic Standard Time",
        "Venezuela Standard Time",
        "Central Brazilian Standard Time",
        "SA Western Standard Time",
        "Pacific SA Standard Time",
        "Newfoundland Standard Time",
        "Tocantins Standard Time",
        "E. South America Standard Time",
        "SA Eastern Standard Time",
        "Argentina Standard Time",
        "Greenland Standard Time",
        "Montevideo Standard Time",
        "Magallanes Standard Time",
        "Saint Pierre Standard Time",
        "Bahia Standard Time",
        "UTC-02",
        "Mid-Atlantic Standard Time",
        "Azores Standard Time",
        "Cape Verde Standard Time",
        "UTC",
        "GMT Standard Time",
        "Greenwich Standard Time",
        "Sao Tome Standard Time",
        "Morocco Standard Time",
        "W. Europe Standard Time",
        "Central Europe Standard Time",
        "Romance Standard Time",
        "Central European Standard Time",
        "W. Central Africa Standard Time",
        "GTB Standard Time",
        "Middle East Standard Time",
        "Egypt Standard Time",
        "E. Europe Standard Time",
        "Syria Standard Time",
        "West Bank Standard Time",
        "South Africa Standard Time",
        "FLE Standard Time",
        "Israel Standard Time",
        "South Sudan Standard Time",
        "Kaliningrad Standard Time",
        "Sudan Standard Time",
        "Libya Standard Time",
        "Namibia Standard Time",
        "Jordan Standard Time",
        "Arabic Standard Time",
        "Turkey Standard Time",
        "Arab Standard Time",
        "Belarus Standard Time",
        "Russian Standard Time",
        "E. Africa Standard Time",
        "Volgograd Standard Time",
        "Iran Standard Time",
        "Arabian Standard Time",
        "Astrakhan Standard Time",
        "Azerbaijan Standard Time",
        "Russia Time Zone 3",
        "Mauritius Standard Time",
        "Saratov Standard Time",
        "Georgian Standard Time",
        "Caucasus Standard Time",
        "Afghanistan Standard Time",
        "West Asia Standard Time",
        "Ekaterinburg Standard Time",
        "Pakistan Standard Time",
        "Qyzylorda Standard Time",
        "India Standard Time",
        "Sri Lanka Standard Time",
        "Nepal Standard Time",
        "Central Asia Standard Time",
        "Bangladesh Standard Time",
        "Omsk Standard Time",
        "Myanmar Standard Time",
        "SE Asia Standard Time",
        "Altai Standard Time",
        "W. Mongolia Standard Time",
        "North Asia Standard Time",
        "N. Central Asia Standard Time",
        "Tomsk Standard Time",
        "China Standard Time",
        "North Asia East Standard Time",
        "Singapore Standard Time",
        "W. Australia Standard Time",
        "Taipei Standard Time",
        "Ulaanbaatar Standard Time",
        "Aus Central W. Standard Time",
        "Transbaikal Standard Time",
        "Tokyo Standard Time",
        "North Korea Standard Time",
        "Korea Standard Time",
        "Yakutsk Standard Time",
        "Cen. Australia Standard Time",
        "AUS Central Standard Time",
        "E. Australia Standard Time",
        "AUS Eastern Standard Time",
        "West Pacific Standard Time",
        "Tasmania Standard Time",
        "Vladivostok Standard Time",
        "Lord Howe Standard Time",
        "Bougainville Standard Time",
        "Russia Time Zone 10",
        "Magadan Standard Time",
        "Norfolk Standard Time",
        "Sakhalin Standard Time",
        "Central Pacific Standard Time",
        "Russia Time Zone 11",
        "New Zealand Standard Time",
        "UTC+12",
        "Fiji Standard Time",
        "Kamchatka Standard Time",
        "Chatham Islands Standard Time",
        "UTC+13",
        "Tonga Standard Time",
        "Samoa Standard Time",
        "Line Islands Standard Time",
    ];

    private static readonly ConditionalWeakTable<object, object> _timeZonePopulatedProviders = new();

    private static bool IsNativeDateTimeVirtualTable(NCLMetaTable? table)
        => table is { TableId: DateVirtualTableId }
            || OperatingSystem.IsWindows() && table is { TableId: TimeZoneVirtualTableId };

    private static bool IsManagedTimeZoneVirtualTable(NCLMetaTable? table)
        => !OperatingSystem.IsWindows() && table is { TableId: TimeZoneVirtualTableId };

    private static void PopulateWindowsTimeZoneVirtualTable(object dataAccess, NCLMetaTable metaTable)
    {
        EnsureAllObjReflection(metaTable);
        EnsureDataAccessProviderReflection(dataAccess);

        var provider = _pDataAccessDataProvider!.GetValue(dataAccess)
            ?? throw new RunnerOutOfScopeException(
                "Time Zone (virtual table 2000000164)",
                "time-zone-virtual-table — Time Zone data access has no in-memory provider; see docs/scope.md");

        if (_timeZonePopulatedProviders.TryGetValue(provider, out _))
            return;

        ClearVirtualBit(metaTable);

        for (var index = 0; index < WindowsTimeZoneIds.Length; index++)
        {
            var number = index + 1;
            var id = WindowsTimeZoneIds[index];
            var displayName = GetWindowsTimeZoneDisplayName(id);
            InsertVirtualRow(
                provider,
                metaTable,
                new object[] { TimeZoneVirtualTableId, number, 0, 0 },
                field => BuildTimeZoneValue(field, number, id, displayName));
        }

        _timeZonePopulatedProviders.Add(provider, new object());
    }

    private static object? BuildTimeZoneValue(
        NCLMetaField field,
        int number,
        string id,
        string displayName)
    {
        object? Text(string value) => _aovNavTextCreateTruncated!.Invoke(
            null,
            new object?[] { field.FieldDefinedLength, value });

        return NormalizeObjectTypeName(field.FieldName ?? string.Empty) switch
        {
            "no." or "no" => _aovNavIntegerCreate!.Invoke(null, new object?[] { number }),
            "id" => Text(id),
            "displayname" => Text(displayName),
            _ => _aovGetDefaultNavValue!.Invoke(null, new object?[] { field, false }),
        };
    }

    private static string GetWindowsTimeZoneDisplayName(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id).DisplayName;
        }
        catch (TimeZoneNotFoundException)
        {
            return id switch
            {
                "Mid-Atlantic Standard Time" => "(UTC-02:00) Mid-Atlantic - Old",
                "Kamchatka Standard Time" => "(UTC+12:00) Petropavlovsk-Kamchatsky - Old",
                _ => id,
            };
        }
        catch (InvalidTimeZoneException)
        {
            return id;
        }
    }
}
