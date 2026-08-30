// This is a runner-mechanism test. NpCore's protected payment-gateway tests provide the
// real-BC behavioral evidence: procedures marked CommitBehavior::Error reject a nested
// COMMIT. This test pins the runner wiring that was bypassed when ALDatabase.ALCommit was
// replaced with the in-memory transaction implementation.

using AlRunner.Patches;
using Microsoft.Dynamics.Nav.Types;
using Xunit;

namespace AlRunner.Tests;

public sealed class ALDatabaseCommitBehaviorTests
{
    [Fact]
    public void CommitBehaviorAllowsCommit_WhenBehaviorIsError_ThrowsCommitProhibited()
    {
        var error = Assert.ThrowsAny<Exception>(() =>
            ALDatabasePatches.CommitBehaviorAllowsCommit(CommitBehavior.Error));

        Assert.Contains("commit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommitBehaviorAllowsCommit_ReturnsServiceTierDecisions()
    {
        Assert.True(ALDatabasePatches.CommitBehaviorAllowsCommit(CommitBehavior.Ok));
        Assert.False(ALDatabasePatches.CommitBehaviorAllowsCommit(CommitBehavior.Ignore));
    }
}
