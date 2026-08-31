using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class TaskSchedulerInstallBaselineTests
{
    [Fact]
    public async Task AsyncLifecycleHelpersSharePendingMembershipWithSyncCalls()
    {
        var taskId = Guid.NewGuid();
        BcRuntime.TaskScheduler_ResetForTest();

        try
        {
            BcRuntime.TaskScheduler_RestoreInstallBaseline(new[] { taskId });

            Assert.True(await BcRuntime.ALTaskScheduler_ALTaskExistsAsync(null!, taskId));
            Assert.True(await BcRuntime.ALTaskScheduler_ALSetTaskReadyAsync(null!, taskId, null!));
            Assert.True(BcRuntime.ALTaskScheduler_ALTaskExists(taskId));
            Assert.True(await BcRuntime.ALTaskScheduler_ALCancelTaskAsync(null!, taskId));
            Assert.False(BcRuntime.ALTaskScheduler_ALTaskExists(taskId));
        }
        finally
        {
            BcRuntime.TaskScheduler_ResetForTest();
        }
    }

    [Fact]
    public void PerTestStateReset_ClearsPendingTaskMembership()
    {
        var taskId = Guid.NewGuid();
        BcRuntime.TaskScheduler_ResetForTest();

        try
        {
            BcRuntime.TaskScheduler_RestoreInstallBaseline(new[] { taskId });

            RecordPatches.ResetPerTestState();

            Assert.False(BcRuntime.ALTaskScheduler_ALTaskExists(taskId));
        }
        finally
        {
            BcRuntime.TaskScheduler_ResetForTest();
        }
    }

    [Fact]
    public void InstallBaseline_RestoresPendingTaskMembership_InMemoryAndFromDisk()
    {
        var taskId = Guid.NewGuid();
        BcRuntime.TaskScheduler_ResetForTest();

        try
        {
            BcRuntime.TaskScheduler_RestoreInstallBaseline(new[] { taskId });
            var captured = RecordPatches.CaptureInstallBaselineSnapshot();
            Assert.Equal(new[] { taskId }, captured.PendingTaskIds);

            var source = new object();
            var snapshot = EmptySnapshot(source, captured.PendingTaskIds);
            BcRuntime.TaskScheduler_ResetForTest();
            RecordPatches.RestoreInstallBaselineSnapshot(snapshot, resetFirst: false);
            Assert.True(BcRuntime.ALTaskScheduler_ALTaskExists(taskId));

            const string cacheKey = "task-scheduler-install-baseline-test";
            var payload = RecordPatches.TrySerializeInstallBaselineSnapshot(
                snapshot,
                cacheKey,
                source);
            Assert.NotNull(payload);
            var decoded = RecordPatches.TryDeserializeInstallBaselineSnapshot(
                payload,
                cacheKey,
                source);
            Assert.NotNull(decoded);
            Assert.Equal(
                RecordPatches.ComputeRoundTripDigest(snapshot),
                RecordPatches.ComputeRoundTripDigest(decoded));

            BcRuntime.TaskScheduler_ResetForTest();
            RecordPatches.RestoreInstallBaselineSnapshot(decoded, resetFirst: false);
            Assert.True(BcRuntime.ALTaskScheduler_ALTaskExists(taskId));
        }
        finally
        {
            BcRuntime.TaskScheduler_ResetForTest();
        }
    }

    [Fact]
    public void DiskBaseline_RejectsTruncatedPendingTaskId()
    {
        const string cacheKey = "task-scheduler-truncated-pending-id-test";
        var source = new object();
        var snapshot = EmptySnapshot(source, new[] { Guid.NewGuid() });
        var payload = RecordPatches.TrySerializeInstallBaselineSnapshot(snapshot, cacheKey, source);

        Assert.NotNull(payload);
        Assert.Null(RecordPatches.TryDeserializeInstallBaselineSnapshot(
            payload[..^1],
            cacheKey,
            source));
    }

    [Fact]
    public void RoundTripDigest_IncludesPendingTaskMembership()
    {
        var source = new object();
        var withoutTask = EmptySnapshot(source, Array.Empty<Guid>());
        var withTask = EmptySnapshot(source, new[] { Guid.NewGuid() });

        Assert.NotEqual(
            RecordPatches.ComputeRoundTripDigest(withoutTask),
            RecordPatches.ComputeRoundTripDigest(withTask));
    }

    private static RecordPatches.InstallBaselineSnapshot EmptySnapshot(
        object source,
        IReadOnlyList<Guid> pendingTaskIds) =>
        new(
            new[]
            {
                new RecordPatches.BaselineSource(
                    source,
                    Array.Empty<RecordPatches.BaselineTable>()),
            },
            IsolatedStorage: null,
            AutoIncrement: new Dictionary<int, long>(),
            pendingTaskIds);
}
