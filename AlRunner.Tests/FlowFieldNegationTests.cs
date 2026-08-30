using AlRunner.Patches;
using Microsoft.Dynamics.Nav.Runtime;
using Xunit;

#pragma warning disable CA1416 // The standalone runner deliberately executes BC's platform-annotated runtime cross-platform.

namespace AlRunner.Tests;

public sealed class FlowFieldNegationTests
{
    [Fact]
    public void NegateFlowFieldValue_NegatesInteger()
    {
        var result = Assert.IsType<NavInteger>(
            FlowFieldPatches.NegateFlowFieldValue(NavInteger.Create(17)));

        Assert.Equal(-17, result.ToInt32());
    }

    [Fact]
    public void NegateFlowFieldValue_NegatesBigInteger()
    {
        var result = Assert.IsType<NavBigInteger>(
            FlowFieldPatches.NegateFlowFieldValue(NavBigInteger.Create(9_000_000_000L)));

        Assert.Equal(-9_000_000_000L, result.Value);
    }

    [Fact]
    public void NegateFlowFieldValue_NegatesDecimal()
    {
        var result = Assert.IsType<NavDecimal>(
            FlowFieldPatches.NegateFlowFieldValue(NavDecimal.Create(12.5m)));

        Assert.Equal(-12.5m, result.Value);
    }

    [Fact]
    public void NegateFlowFieldValue_LeavesNonNumericValueUnchanged()
    {
        var value = NavBoolean.Create(true);

        Assert.Same(value, FlowFieldPatches.NegateFlowFieldValue(value));
    }
}

#pragma warning restore CA1416
