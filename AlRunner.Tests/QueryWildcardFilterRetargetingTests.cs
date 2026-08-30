using AlRunner.Patches;
using Microsoft.Dynamics.Nav.Runtime;
using Xunit;

#pragma warning disable CA1416 // The standalone runner deliberately executes BC's platform-annotated runtime cross-platform.

namespace AlRunner.Tests;

public sealed class QueryWildcardFilterRetargetingTests
{
    [Fact]
    public void WildcardExpression_IsRetargetedToSourceFieldContext()
    {
        var queryColumnContext = FilterExpressionContext.CreateDefaultForType(NavNclType.NavCode);
        var sourceFieldContext = FilterExpressionContext.CreateDefaultForType(NavNclType.NavText);
        var expression = new WildcardFilterExpression(
            isNegated: true,
            pattern: "T1*",
            isCaseAndAccentInsensitive: false,
            queryColumnContext);

        var retargeted = Assert.IsType<WildcardFilterExpression>(
            RecordPatches.RetargetFilterExpression(expression, sourceFieldContext));

        Assert.NotSame(expression, retargeted);
        Assert.Same(sourceFieldContext, retargeted.ExpressionContext);
        Assert.Equal("T1*", retargeted.Pattern);
        Assert.True(retargeted.IsNegated);
        Assert.False(retargeted.IsCaseAndAccentInsensitive);
    }
}

#pragma warning restore CA1416
