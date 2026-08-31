using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace AlRunner.Tests;

public sealed class TestExecutorSourceOrderTests
{
    [Theory]
    [InlineData("_Scope_123", true)]
    [InlineData("_Scope__123", true)]
    [InlineData("Extended_Scope_123", false)]
    [InlineData("_Suffixed_Scope_123", false)]
    public void ScopeTypeSuffix_MatchesOnlyTheRequestedMethodsScope(
        string remainder,
        bool expected)
    {
        var field = typeof(TestExecutor).GetField(
            "_scopeTypeSuffix",
            BindingFlags.NonPublic | BindingFlags.Static);
        var matcher = Assert.IsType<Regex>(field?.GetValue(null));

        Assert.Equal(expected, matcher.IsMatch(remainder));
    }
}
