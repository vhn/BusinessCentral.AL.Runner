using Xunit;

namespace AlRunner.Tests;

public sealed class BlankEnumOptionNormalizationTests
{
    [Fact]
    public void EmptyFilterLiteral_UsesTheBlankEnumMemberName()
    {
        Assert.Equal(
            " ",
            BcRuntime.NormalizeQuotedOptionValueForMetadata(
                string.Empty,
                ",Customer,Vendor",
                [" ", "Customer", "Vendor"],
                isEnum: true));
    }

    [Fact]
    public void EmptyFilterLiteral_RemainsEmptyForClassicOptionMetadata()
    {
        Assert.Equal(
            string.Empty,
            BcRuntime.NormalizeQuotedOptionValueForMetadata(
                string.Empty,
                ",Customer,Vendor",
                [string.Empty, "Customer", "Vendor"],
                isEnum: false));
    }
}
