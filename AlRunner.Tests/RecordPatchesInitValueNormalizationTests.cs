using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

public class RecordPatchesInitValueNormalizationTests
{
    [Theory]
    [InlineData("Option", "\"Per POS Entry\"", "Per POS Entry")]
    [InlineData("Option", "Open", "Open")]
    [InlineData("Enum \"NPR Status\"", "\"Needs Review\"", "Needs Review")]
    [InlineData("Code[20]", "'A''B'", "A'B")]
    public void NormalizeInitValueText_MatchesRuntimeMetadata(
        string typeName,
        string sourceValue,
        string expected)
    {
        Assert.Equal(expected, RecordPatches.NormalizeInitValueText(typeName, sourceValue));
    }
}
