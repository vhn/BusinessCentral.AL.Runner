using System.Reflection;
using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

[Collection(RecordPatchesSerialCollection.Name)]
public sealed class RecordPatchesTableExtensionFormulaMergeTests : IDisposable
{
    public RecordPatchesTableExtensionFormulaMergeTests() => RecordPatches.ResetForReload();

    public void Dispose() => RecordPatches.ResetForReload();

    [Fact]
    public void SourceFieldUpgradesEarlierSymbolFieldThatLostItsCalcFormula()
    {
        var merge = typeof(RecordPatches).GetMethod(
            "MergeExtensionFields",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var formula = new ParsedCalcFormula(
            "Exist",
            "Item Variant",
            null,
            [new ParsedCalcFilter("Item No.", ParentFieldName: "No.")]);
        var symbolField = new ParsedField(
            6_014_609,
            "NPR Has Variants",
            "Boolean",
            0,
            IsFlowField: true);
        var sourceField = symbolField with { CalcFormula = formula };

        merge.Invoke(null, ["Item", 6_014_427, new[] { symbolField }]);
        merge.Invoke(null, ["Item", 6_014_427, new[] { sourceField }]);

        var merged = Assert.Single(RecordPatches.ExtensionFieldsForBaseTable("Item"));
        Assert.Same(formula, merged.CalcFormula);
    }
}
