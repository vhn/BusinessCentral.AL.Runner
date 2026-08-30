using System.Reflection;
using AlRunner;
using Microsoft.Dynamics.Nav.Runtime;
using Xunit;

namespace AlRunner.Tests;

public class ExplicitSenderParameterClassificationTests
{
    private static void CodeunitEventArgumentThenSender(int eventArgument, NavCodeunitHandle sender) { }

    private static void TableEventArgumentThenSender(int eventArgument, INavRecordHandle sender) { }

    [Theory]
    [InlineData(nameof(CodeunitEventArgumentThenSender))]
    [InlineData(nameof(TableEventArgumentThenSender))]
    public void ExplicitlyNamedSender_IsRecognizedAfterEventArguments(string methodName)
    {
        var sender = typeof(ExplicitSenderParameterClassificationTests)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetParameters()[1];

        Assert.True(BcRuntime.IsSenderParameter(sender, paramIndex: 1));
    }
}
