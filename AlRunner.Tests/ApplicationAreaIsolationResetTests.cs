using System.Reflection;
using System.Runtime.CompilerServices;
using AlRunner;
using AlRunner.Patches;
using Microsoft.Dynamics.Nav.Runtime;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class ApplicationAreaIsolationResetTests
{
    [Fact]
    public void ResetPerTestState_RestoresApplicationAreasCapturedAfterInstall()
    {
        var session = (NavSession)RuntimeHelpers.GetUninitializedObject(typeof(NavSession));
        BcRuntime.SeedSkeletonApplicationAreaCache(typeof(NavSession), session);
        session.ApplicationAreas = "VAT";

        var sessionField = typeof(BcRuntime).GetField(
            "_skeletonSession", BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalSession = sessionField.GetValue(null);
        try
        {
            sessionField.SetValue(null, session);
            BcRuntime.CaptureSkeletonApplicationAreaBaseline();
            session.ApplicationAreas = "Sales Tax";

            RecordPatches.ResetPerTestState();

            Assert.Equal("VAT", session.ApplicationAreas);
        }
        finally
        {
            BcRuntime.ClearSkeletonApplicationAreaBaseline();
            sessionField.SetValue(null, originalSession);
        }
    }
}
