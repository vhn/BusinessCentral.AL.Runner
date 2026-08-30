using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

public sealed class BcAppSymbolCacheQueryTests
{
    [Fact]
    public void SourceLessCountColumn_PreservesAggregationMethod()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "Queries": [
                    {
                      "Id": 6014491,
                      "Name": "Wallets",
                      "Elements": [
                        {
                          "Id": 1,
                          "Name": "Wallet",
                          "RelatedTable": "Wallet Header",
                          "Columns": [
                            {
                              "Id": 2,
                              "Name": "Count",
                              "Properties": [
                                { "Name": "Method", "Value": "Count" }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
                """);

            var query = Assert.Single(BcAppSymbolCache.GetFromJson(path).Queries);
            var column = Assert.Single(Assert.Single(query.DataItems).Columns);

            Assert.Equal(string.Empty, column.SourceColumn);
            Assert.Equal("Count", column.Method);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void QueryFieldMap_IncludesBusinessCentralSystemFields()
    {
        var fields = RecordPatches.BuildFieldNameToNoMap(int.MaxValue);

        Assert.Equal(2_000_000_000, fields["SystemId"]);
        Assert.Equal(2_000_000_001, fields["SystemCreatedAt"]);
        Assert.Equal(2_000_000_002, fields["SystemCreatedBy"]);
        Assert.Equal(2_000_000_003, fields["SystemModifiedAt"]);
        Assert.Equal(2_000_000_004, fields["SystemModifiedBy"]);
    }

    [Fact]
    public void QueryDataItem_StripsCrossModuleQualifierFromRelatedTable()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "Queries": [
                    {
                      "Id": 6014405,
                      "Name": "NPR Item Variants",
                      "Elements": [
                        {
                          "Id": 1,
                          "Name": "Item_Variant",
                          "RelatedTable": "#437dbf0e84ff417a965ded2bb9650972#Item Variant",
                          "Columns": []
                        }
                      ]
                    }
                  ]
                }
                """);

            var query = Assert.Single(BcAppSymbolCache.GetFromJson(path).Queries);
            var dataItem = Assert.Single(query.DataItems);

            Assert.Equal("Item Variant", dataItem.RelatedTable);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void QueryStaticFiltersAndReverseSign_ArePreserved()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "Queries": [
                    {
                      "Id": 6014426,
                      "Name": "Department Item Category",
                      "Elements": [
                        {
                          "Id": 1,
                          "Name": "Item_Ledger_Entry",
                          "RelatedTable": "Item Ledger Entry",
                          "Columns": [
                            {
                              "Id": 2,
                              "Name": "Sales_Amount_Actual",
                              "SourceColumn": "Sales Amount (Actual)",
                              "Properties": [
                                { "Name": "ColumnFilter", "Value": "Sales_Amount_Actual = filter(> 0)" },
                                { "Name": "Method", "Value": "Sum" },
                                { "Name": "ReverseSign", "Value": "1" }
                              ]
                            }
                          ],
                          "Properties": [
                            { "Name": "DataItemTableFilter", "Value": "\"Entry Type\" = const(Sale)" }
                          ]
                        }
                      ]
                    }
                  ]
                }
                """);

            var query = Assert.Single(BcAppSymbolCache.GetFromJson(path).Queries);
            var dataItem = Assert.Single(query.DataItems);
            var column = Assert.Single(dataItem.Columns);

            Assert.Equal("\"Entry Type\" = const(Sale)", dataItem.DataItemTableFilter);
            Assert.Equal("Sales_Amount_Actual = filter(> 0)", column.ColumnFilter);
            Assert.True(column.ReverseSign);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
