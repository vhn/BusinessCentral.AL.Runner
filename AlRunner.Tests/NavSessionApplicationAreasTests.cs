using AlRunner;
using Microsoft.Dynamics.Nav.Runtime;
using System.Runtime.CompilerServices;
using Xunit;

#pragma warning disable CA1416 // The standalone runner deliberately executes BC's platform-annotated runtime cross-platform.

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class NavSessionApplicationAreasTests
{
    [Fact]
    public void SeedSkeletonApplicationAreaCache_AllowsApplicationAreasToBeSet()
    {
        var session = (NavSession)RuntimeHelpers.GetUninitializedObject(typeof(NavSession));

        BcRuntime.SeedSkeletonApplicationAreaCache(typeof(NavSession), session);

        session.ApplicationAreas = "All";

        Assert.Equal("All", session.ApplicationAreas);
    }
}

#pragma warning restore CA1416
