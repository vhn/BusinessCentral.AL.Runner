using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

public sealed class RunnerPageInstanceBooleanPropertyBindingTests
{
    [Theory]
    [InlineData("p6248222p6248222_IsSkipped", "p6248222p6248222_IsSkipped", false)]
    [InlineData("not p6248222p6248222_IsSkipped", "p6248222p6248222_IsSkipped", true)]
    [InlineData("NOT p6248222p6248222_IsSkipped", "p6248222p6248222_IsSkipped", true)]
    [InlineData("notebook", "notebook", false)]
    public void ParseBooleanPropertyBinding_SeparatesUnaryNot(
        string raw, string expectedName, bool expectedNegate)
    {
        var actual = RunnerPageInstance.ParseBooleanPropertyBinding(raw);

        Assert.Equal(expectedName, actual.ExpressionName);
        Assert.Equal(expectedNegate, actual.Negate);
    }
}
