using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

public sealed class NclMetaQueryBuilderParsingTests
{
    [Fact]
    public void ParseDataItemLinkClauses_ReturnsEveryEquality()
    {
        var clauses = RecordPatches.ParseDataItemLinkClauses("""
            "Variant Code" = Item_Variant.Code,
            "Item No." = Item_Variant."Item No."
            """);

        Assert.Collection(clauses,
            first =>
            {
                Assert.Equal("Variant Code", first.DestinationField);
                Assert.Equal("Item_Variant", first.SourceDataItem);
                Assert.Equal("Code", first.SourceField);
            },
            second =>
            {
                Assert.Equal("Item No.", second.DestinationField);
                Assert.Equal("Item_Variant", second.SourceDataItem);
                Assert.Equal("Item No.", second.SourceField);
            });
    }

    [Fact]
    public void ParseStaticQueryFilters_PreservesFilterKindsAndValues()
    {
        var filters = RecordPatches.ParseStaticQueryFilters("""
            "Amount Excl. VAT" = filter(> 0), Type = const(Item), Description = filter('A,B'|"C,D")
            """);

        Assert.Collection(filters,
            amount =>
            {
                Assert.Equal("Amount Excl. VAT", amount.FieldOrColumnName);
                Assert.Equal("FILTER", amount.FilterType);
                Assert.Equal("> 0", amount.Value);
            },
            type =>
            {
                Assert.Equal("Type", type.FieldOrColumnName);
                Assert.Equal("CONST", type.FilterType);
                Assert.Equal("Item", type.Value);
            },
            description =>
            {
                Assert.Equal("Description", description.FieldOrColumnName);
                Assert.Equal("FILTER", description.FilterType);
                Assert.Equal("'A,B'|\"C,D\"", description.Value);
            });
    }

    [Theory]
    [InlineData("MissingEquals")]
    [InlineData("Amount = field(Other)")]
    [InlineData("Amount = filter(> 0")]
    public void ParseStaticQueryFilters_InvalidShapeReturnsEmpty(string text)
    {
        Assert.Empty(RecordPatches.ParseStaticQueryFilters(text));
    }
}
