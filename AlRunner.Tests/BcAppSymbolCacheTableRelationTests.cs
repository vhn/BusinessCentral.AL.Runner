using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

public sealed class BcAppSymbolCacheTableRelationTests
{
    [Fact]
    public void TableFields_CaptureRelationsAndValidationSetting()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "Tables": [
                    {
                      "Id": 5741,
                      "Name": "Transfer Line",
                      "Fields": [
                        {
                          "Id": 3,
                          "Name": "Item No.",
                          "TypeDefinition": { "Name": "Code", "Length": 20 },
                          "Properties": [
                            {
                              "Name": "TableRelation",
                              "Value": "Item where(Type = const(Inventory), Blocked = const(false))"
                            }
                          ]
                        },
                        {
                          "Id": 30,
                          "Name": "Variant Code",
                          "TypeDefinition": { "Name": "Code", "Length": 10 },
                          "Properties": [
                            {
                              "Name": "TableRelation",
                              "Value": "\"Item Variant\".Code where(\"Item No.\" = field(\"Item No.\"), Blocked = const(false))"
                            },
                            { "Name": "ValidateTableRelation", "Value": "0" }
                          ]
                        }
                      ],
                      "Keys": [
                        { "Name": "PK", "FieldNames": ["Item No."] }
                      ]
                    }
                  ]
                }
                """);

            var table = Assert.Single(BcAppSymbolCache.GetFromJson(path).Tables);

            var itemNo = Assert.Single(table.Fields, field => field.FieldId == 3);
            Assert.True(itemNo.RelationValidate);
            var itemRelation = Assert.Single(itemNo.RelationArms!);
            Assert.Equal("Item", itemRelation.TableName);
            Assert.Null(itemRelation.FieldName);
            Assert.Collection(
                itemRelation.Filters,
                filter =>
                {
                    Assert.Equal("Type", filter.SourceFieldName);
                    Assert.Equal(ParsedCalcFilterKind.Const, filter.Kind);
                    Assert.Equal("Inventory", filter.Value);
                },
                filter =>
                {
                    Assert.Equal("Blocked", filter.SourceFieldName);
                    Assert.Equal(ParsedCalcFilterKind.Const, filter.Kind);
                    Assert.Equal("false", filter.Value);
                });

            var variantCode = Assert.Single(table.Fields, field => field.FieldId == 30);
            Assert.False(variantCode.RelationValidate);
            var variantRelation = Assert.Single(variantCode.RelationArms!);
            Assert.Equal("Item Variant", variantRelation.TableName);
            Assert.Equal("Code", variantRelation.FieldName);
            Assert.Collection(
                variantRelation.Filters,
                filter =>
                {
                    Assert.Equal("Item No.", filter.SourceFieldName);
                    Assert.Equal(ParsedCalcFilterKind.Field, filter.Kind);
                    Assert.Equal("Item No.", filter.ParentFieldName);
                },
                filter =>
                {
                    Assert.Equal("Blocked", filter.SourceFieldName);
                    Assert.Equal(ParsedCalcFilterKind.Const, filter.Kind);
                    Assert.Equal("false", filter.Value);
                });
        }
        finally
        {
            File.Delete(path);
        }
    }
}
