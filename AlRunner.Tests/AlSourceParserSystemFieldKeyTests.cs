// AlSourceParserSystemFieldKeyTests — RED→GREEN guard for table keys naming a system field.
//
// AL lets a key reference the implicit system fields like declared ones, and real BC builds
// such keys in full (verified against the BC 28.0 compiler: each of these compiles clean as a
// SECONDARY key; a system field in the PRIMARY key is AL0580, hence no primary-key case here):
//
//     key(CreatedDateSorting; SystemCreatedAt) { }
//     key(SellToEmail; "Sell-to Email", SystemCreatedAt) { }
//     key(Replication; SystemRowVersion) { }
//
// The parser resolved key field names against DECLARED fields only and dropped the rest, so
// such a key came out truncated — or vanished when it named nothing else. Why that is fatal,
// and why SystemRowVersion is field 0 and resolvable-but-never-appended, is on
// RecordPatches.TryResolveKeyFieldId.
//
// These drive the real parser by reflection, like AlSourceParserSyntaxTreeTests.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace AlRunner.Tests;

[Collection(RecordPatchesSerialCollection.Name)]
public class AlSourceParserSystemFieldKeyTests : IDisposable
{
    private const int TableId = 61893;

    private const int EmailFieldId = 10;
    private const int TimestampFieldId = 0;
    private const int SystemIdFieldId = 2_000_000_000;
    private const int SystemCreatedAtFieldId = 2_000_000_001;
    private const int SystemCreatedByFieldId = 2_000_000_002;
    private const int SystemModifiedAtFieldId = 2_000_000_003;
    private const int SystemModifiedByFieldId = 2_000_000_004;

    private static readonly Type RecordPatchesType = typeof(AlRunner.Patches.RecordPatches);

    private static IDictionary ParsedTables =>
        (IDictionary)RecordPatchesType
            .GetField("_parsedTables", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    /// <summary>The parse statics are process-wide and this collection only serialises access to
    /// them; it does not clean them. Leaving the fixture behind would make a later test class
    /// that reuses the id order-dependent, so drop it however the test ends.</summary>
    public void Dispose() => ParsedTables.Remove(TableId);

    private static object ParseTable(string source)
    {
        var parse = RecordPatchesType.GetMethod("TryParseTableFile",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        parse.Invoke(null, new object[] { source });
        Assert.True(ParsedTables.Contains(TableId), $"table {TableId} was not parsed at all");
        return ParsedTables[TableId]!;
    }

    private static (object Table, string Stderr) ParseTableCapturingStderr(string source)
    {
        var original = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var table = ParseTable(source);
            return (table, captured.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    private static List<int>? SecondaryKeyFieldIds(object parsedTable, string keyName)
    {
        var keys = parsedTable.GetType().GetProperty("SecondaryKeys")!.GetValue(parsedTable);
        if (keys is null) return null;
        foreach (var k in (IEnumerable)keys)
        {
            var name = (string)k.GetType().GetProperty("Name")!.GetValue(k)!;
            if (string.Equals(name, keyName, StringComparison.OrdinalIgnoreCase))
                return (List<int>)k.GetType().GetProperty("FieldIds")!.GetValue(k)!;
        }
        return null;
    }

    private static IEnumerable<string> SecondaryKeyNames(object parsedTable)
    {
        var keys = parsedTable.GetType().GetProperty("SecondaryKeys")!.GetValue(parsedTable);
        if (keys is null) yield break;
        foreach (var k in (IEnumerable)keys)
            yield return (string)k.GetType().GetProperty("Name")!.GetValue(k)!;
    }

    private static string Table(string keysBody) => $$"""
        table {{TableId}} "System Field Key Fixture"
        {
            fields
            {
                field(1; "Entry No."; Integer) { }
                field(10; "Sell-to Email"; Text[80]) { }
            }
            keys
            {
                key(PK; "Entry No.") { Clustered = true; }
        {{keysBody}}
            }
        }
        """;

    [Fact]
    public void KeyNamingASystemFieldKeepsThatField()
    {
        var table = ParseTable(Table("""
                key(SellToEmail; "Sell-to Email", SystemCreatedAt) { }
        """));

        Assert.Equal(new[] { EmailFieldId, SystemCreatedAtFieldId },
            SecondaryKeyFieldIds(table, "SellToEmail"));
    }

    [Fact]
    public void KeyMadeOnlyOfASystemFieldSurvives()
    {
        var table = ParseTable(Table("""
                key(CreatedDateSorting; SystemCreatedAt) { }
        """));

        Assert.Equal(new[] { SystemCreatedAtFieldId },
            SecondaryKeyFieldIds(table, "CreatedDateSorting"));
    }

    [Fact]
    public void KeyNamingSystemRowVersionResolvesToFieldZero()
    {
        var table = ParseTable(Table("""
                key(Replication; SystemRowVersion) { }
        """));

        Assert.Equal(new[] { TimestampFieldId },
            SecondaryKeyFieldIds(table, "Replication"));
    }

    [Fact]
    public void CompositeKeyEndingInSystemRowVersionResolvesBothFields()
    {
        var table = ParseTable(Table("""
                key(EmailByRowVersion; "Sell-to Email", SystemRowVersion) { }
        """));

        Assert.Equal(new[] { EmailFieldId, TimestampFieldId },
            SecondaryKeyFieldIds(table, "EmailByRowVersion"));
    }

    [Fact]
    public void EverySystemFieldNameResolvesWhateverItsCasing()
    {
        var table = ParseTable(Table("""
                key(AllSix; systemid, SYSTEMCREATEDAT, SystemCreatedBy, sYsTeMmOdIfIeDaT, SYSTEMMODIFIEDBY, systemrowversion) { }
        """));

        Assert.Equal(
            new[]
            {
                SystemIdFieldId, SystemCreatedAtFieldId, SystemCreatedByFieldId,
                SystemModifiedAtFieldId, SystemModifiedByFieldId, TimestampFieldId,
            },
            SecondaryKeyFieldIds(table, "AllSix"));
    }

    [Fact]
    public void ASystemFieldKeyDeclaredAfterARowVersionKeySurvives()
    {
        var table = ParseTable(Table("""
                key(ByRowVersion; SystemRowVersion) { }
                key(ByCreatedAt; SystemCreatedAt) { }
                key(ByEmail; "Sell-to Email") { }
        """));

        Assert.Equal(new[] { "ByRowVersion", "ByCreatedAt", "ByEmail" }, SecondaryKeyNames(table));
        Assert.Equal(new[] { TimestampFieldId }, SecondaryKeyFieldIds(table, "ByRowVersion"));
        Assert.Equal(new[] { SystemCreatedAtFieldId }, SecondaryKeyFieldIds(table, "ByCreatedAt"));
    }

    [Fact]
    public void SystemFieldKeysDoNotShiftLaterKeyOrdinals()
    {
        var table = ParseTable(Table("""
                key(CreatedDateSorting; SystemCreatedAt) { }
                key(SellToEmail; "Sell-to Email", SystemCreatedAt) { }
                key(BySystemId; SystemId) { }
        """));

        Assert.Equal(new[] { "CreatedDateSorting", "SellToEmail", "BySystemId" },
            SecondaryKeyNames(table));
    }

    [Fact]
    public void UnknownFieldNameInAKeyIsRefusedLoudly()
    {
        var (table, stderr) = ParseTableCapturingStderr(Table("""
                key(Bogus; "Sell-to Email", NoSuchFieldAnywhere) { }
        """));

        Assert.Equal(new[] { EmailFieldId }, SecondaryKeyFieldIds(table, "Bogus"));
        Assert.Contains("REFUSED", stderr);
        Assert.Contains("NoSuchFieldAnywhere", stderr);
        Assert.Contains("Bogus", stderr);
    }

    [Fact]
    public void KeyWhoseEveryFieldIsUnresolvableIsDroppedNotKeptEmpty()
    {
        var (table, stderr) = ParseTableCapturingStderr(Table("""
                key(AllBogus; NoSuchFieldAnywhere, NorThisOne) { }
                key(ByEmail; "Sell-to Email") { }
        """));

        Assert.Null(SecondaryKeyFieldIds(table, "AllBogus"));
        Assert.Equal(new[] { "ByEmail" }, SecondaryKeyNames(table));
        Assert.Contains("REFUSED", stderr);
    }
}
