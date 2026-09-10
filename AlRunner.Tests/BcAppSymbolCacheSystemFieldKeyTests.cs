// BcAppSymbolCacheSystemFieldKeyTests — the SECOND key-parsing path.
//
// A precompiled dependency's table metadata comes from its SymbolReference.json, and that
// reader had the same declared-fields-only key lookup as the AL-source parser. Real packages
// carry such keys: Base Application 28.0 has 23, one ISV package 58. It had no test at all,
// so reverting the fix left every AL-source key test green.
using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

public sealed class BcAppSymbolCacheSystemFieldKeyTests
{
    private const int TableId = 61895;

    private const int EntryNoFieldId = 1;
    private const int EmailFieldId = 10;
    private const int TimestampFieldId = 0;
    private const int SystemIdFieldId = 2_000_000_000;
    private const int SystemCreatedAtFieldId = 2_000_000_001;
    private const int SystemModifiedAtFieldId = 2_000_000_003;

    private static ParsedTable ReadTable(string keysJson)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, $$"""
                {
                  "Tables": [
                    {
                      "Id": {{TableId}},
                      "Name": "Sym System Field Key Fixture",
                      "Fields": [
                        { "Id": 1,  "Name": "Entry No.",     "TypeDefinition": { "Name": "Integer" } },
                        { "Id": 10, "Name": "Sell-to Email", "TypeDefinition": { "Name": "Text", "Length": 80 } }
                      ],
                      "Keys": {{keysJson}}
                    }
                  ]
                }
                """);
            return BcAppSymbolCache.GetFromJson(path).Tables.Single(t => t.TableId == TableId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static List<int>? SecondaryKeyFieldIds(ParsedTable table, string keyName) =>
        table.SecondaryKeys?.FirstOrDefault(k =>
            string.Equals(k.Name, keyName, StringComparison.OrdinalIgnoreCase))?.FieldIds;

    [Fact]
    public void SymbolKeyNamingASystemFieldKeepsThatField()
    {
        var table = ReadTable("""
            [
              { "Name": "PK",        "FieldNames": [ "Entry No." ] },
              { "Name": "ByCreated", "FieldNames": [ "Sell-to Email", "SystemCreatedAt" ] }
            ]
            """);

        Assert.Equal(new[] { EmailFieldId, SystemCreatedAtFieldId },
            SecondaryKeyFieldIds(table, "ByCreated"));
    }

    [Fact]
    public void SymbolKeyMadeOnlyOfASystemFieldSurvives()
    {
        var table = ReadTable("""
            [
              { "Name": "PK",         "FieldNames": [ "Entry No." ] },
              { "Name": "ByModified", "FieldNames": [ "SystemModifiedAt" ] },
              { "Name": "ByEmail",    "FieldNames": [ "Sell-to Email" ] }
            ]
            """);

        Assert.Equal(new[] { SystemModifiedAtFieldId }, SecondaryKeyFieldIds(table, "ByModified"));
        Assert.Equal(new[] { "ByModified", "ByEmail" }, table.SecondaryKeys!.Select(k => k.Name));
    }

    [Fact]
    public void SymbolKeyNamingSystemRowVersionResolvesToFieldZero()
    {
        // Symbol metadata spells it `SystemRowVersion`, never the layout name `timestamp`
        // (zero occurrences of the latter across six real symbol files).
        var table = ReadTable("""
            [
              { "Name": "PK",           "FieldNames": [ "Entry No." ] },
              { "Name": "ByRowVersion", "FieldNames": [ "SystemRowVersion" ] }
            ]
            """);

        Assert.Equal(new[] { TimestampFieldId }, SecondaryKeyFieldIds(table, "ByRowVersion"));
    }

    [Fact]
    public void SymbolPrimaryKeyStillResolvesItsDeclaredFields()
    {
        var table = ReadTable("""
            [
              { "Name": "PK", "FieldNames": [ "Entry No.", "Sell-to Email" ] }
            ]
            """);

        Assert.Equal(new[] { EntryNoFieldId, EmailFieldId }, table.PkFieldIds);
    }

    [Fact]
    public void SymbolKeyResolvesEverySystemFieldNameWhateverItsCasing()
    {
        var table = ReadTable("""
            [
              { "Name": "PK",     "FieldNames": [ "Entry No." ] },
              { "Name": "Mixed",  "FieldNames": [ "systemid", "SYSTEMCREATEDAT", "systemrowversion" ] }
            ]
            """);

        Assert.Equal(new[] { SystemIdFieldId, SystemCreatedAtFieldId, TimestampFieldId },
            SecondaryKeyFieldIds(table, "Mixed"));
    }

    [Fact]
    public void SymbolKeyWithAnUnknownFieldNameIsRefusedNotSilentlyShortened()
    {
        var savedError = Console.Error;
        var captured = new StringWriter();
        ParsedTable table;
        try
        {
            Console.SetError(captured);
            table = ReadTable("""
                [
                  { "Name": "PK",    "FieldNames": [ "Entry No." ] },
                  { "Name": "Bogus", "FieldNames": [ "Sell-to Email", "NoSuchFieldAnywhere" ] }
                ]
                """);
        }
        finally
        {
            Console.SetError(savedError);
        }

        Assert.Equal(new[] { EmailFieldId }, SecondaryKeyFieldIds(table, "Bogus"));
        var stderr = captured.ToString();
        Assert.Contains("REFUSED", stderr);
        Assert.Contains("NoSuchFieldAnywhere", stderr);
    }

    [Fact]
    public void SymbolKeysThatAllResolveProduceNoRefusal()
    {
        var savedError = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            ReadTable("""
                [
                  { "Name": "PK",           "FieldNames": [ "Entry No." ] },
                  { "Name": "ByCreated",    "FieldNames": [ "Sell-to Email", "SystemCreatedAt" ] },
                  { "Name": "ByRowVersion", "FieldNames": [ "SystemRowVersion" ] }
                ]
                """);
        }
        finally
        {
            Console.SetError(savedError);
        }

        Assert.DoesNotContain("REFUSED", captured.ToString());
    }
}
