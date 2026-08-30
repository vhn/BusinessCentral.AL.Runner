using System.Runtime.CompilerServices;
using AlRunner.Patches;
using Microsoft.Dynamics.Nav.Runtime;
using Xunit;

namespace AlRunner.Tests;

public sealed class FieldTriggerWiringTrackerTests
{
    [Fact]
    public void RebuiltMetadataForSameTableRequiresRewiring()
    {
        var tracker = new FieldTriggerWiringTracker();
        var first = (NCLMetaTable)RuntimeHelpers.GetUninitializedObject(typeof(NCLMetaTable));
        var rebuilt = (NCLMetaTable)RuntimeHelpers.GetUninitializedObject(typeof(NCLMetaTable));

        tracker.MarkCurrent(36, first);

        Assert.True(tracker.IsCurrent(36, first));
        Assert.False(tracker.IsCurrent(36, rebuilt));

        tracker.MarkCurrent(36, rebuilt);

        Assert.False(tracker.IsCurrent(36, first));
        Assert.True(tracker.IsCurrent(36, rebuilt));
    }
}
