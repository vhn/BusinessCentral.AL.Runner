using AlRunner.Patches;
using Microsoft.Dynamics.Nav.Runtime;
using Xunit;

#pragma warning disable CA1416 // The standalone runner deliberately executes BC's platform-annotated runtime cross-platform.

namespace AlRunner.Tests;

public sealed class QueryRangeFilterRetargetingTests
{
    [Fact]
    public void RangeExpression_IsRetargetedToSourceFieldContext()
    {
        var queryColumnContext = FilterExpressionContext.CreateDefaultForType(NavNclType.NavInteger);
        var sourceFieldContext = FilterExpressionContext.CreateDefaultForType(NavNclType.NavBigInteger);
        var expression = new RangeFilterExpression(
            FilterExpressionType.RangeBetweenInclusive,
            NavInteger.Create(10),
            NavInteger.Create(20),
            queryColumnContext);

        var retargeted = Assert.IsType<RangeFilterExpression>(
            RecordPatches.RetargetFilterExpression(expression, sourceFieldContext));

        Assert.Same(sourceFieldContext, retargeted.ExpressionContext);
        Assert.Equal(10, retargeted.LowValue.ToInt32());
        Assert.Equal(20, retargeted.HighValue.ToInt32());
    }
}

#pragma warning restore CA1416
