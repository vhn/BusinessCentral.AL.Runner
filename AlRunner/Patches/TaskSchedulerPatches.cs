using System.Runtime.CompilerServices;
using Microsoft.Dynamics.Nav.Runtime;

namespace AlRunner;

public static partial class BcRuntime
{
    private static readonly object _taskSchedulerSync = new();
    private static readonly HashSet<Guid> _pendingTaskIds = new();

    /// <summary>
    /// Replacement for the synchronous ALTaskScheduler.ALCreateTask entry point.
    /// </summary>
    /// <remarks>
    /// Business Central validates the main codeunit id and any nonzero failure-codeunit id, then
    /// returns a fresh opaque id without executing the background session before CreateTask
    /// returns. The standalone runner has no scheduler, so it retains only pending-id membership.
    /// Lifecycle calls can observe or cancel that membership. SetTaskReady acknowledges a known id
    /// without retaining readiness, and no codeunit is ever dispatched. This deliberately narrow
    /// stub contract is documented under docs/scope.md §3.6.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Guid ALTaskScheduler_ALCreateTask(
        int codeunitId,
        int failureCodeunitId,
        bool isReady,
        string companyName,
        NavDateTime notBefore,
        NavRecordId recordId,
        NavDuration timeout)
    {
        EnsureTaskCodeunitExists(codeunitId);
        if (failureCodeunitId != 0)
            EnsureTaskCodeunitExists(failureCodeunitId);

        // With no dispatcher or Scheduled Task table, no observable runner behavior can consume
        // readiness, company, timing, record, or timeout data. Retaining those values would imply
        // a scheduler model that this membership-only stub intentionally does not provide.

        lock (_taskSchedulerSync)
        {
            Guid taskId;
            do
            {
                taskId = Guid.NewGuid();
            }
            while (!_pendingTaskIds.Add(taskId));

            return taskId;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ALTaskScheduler_ALTaskExists(Guid taskId)
    {
        lock (_taskSchedulerSync)
            return _pendingTaskIds.Contains(taskId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ALTaskScheduler_ALCancelTask(Guid taskId)
    {
        lock (_taskSchedulerSync)
            return _pendingTaskIds.Remove(taskId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ALTaskScheduler_ALSetTaskReady(Guid taskId, NavDateTime notBefore)
    {
        // Readiness and NotBefore cannot affect execution because this stub has no dispatcher.
        lock (_taskSchedulerSync)
            return _pendingTaskIds.Contains(taskId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ValueTask<bool> ALTaskScheduler_ALTaskExistsAsync(NavSession _, Guid taskId) =>
        ValueTask.FromResult(ALTaskScheduler_ALTaskExists(taskId));

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ValueTask<bool> ALTaskScheduler_ALCancelTaskAsync(NavSession _, Guid taskId) =>
        ValueTask.FromResult(ALTaskScheduler_ALCancelTask(taskId));

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ValueTask<bool> ALTaskScheduler_ALSetTaskReadyAsync(
        NavSession _,
        Guid taskId,
        NavDateTime notBefore) =>
        ValueTask.FromResult(ALTaskScheduler_ALSetTaskReady(taskId, notBefore));

    internal static void TaskScheduler_ResetForTest()
    {
        lock (_taskSchedulerSync)
            _pendingTaskIds.Clear();
    }

    internal static IReadOnlyList<Guid> TaskScheduler_CaptureInstallBaseline()
    {
        lock (_taskSchedulerSync)
            return _pendingTaskIds.ToArray();
    }

    internal static void TaskScheduler_RestoreInstallBaseline(IReadOnlyList<Guid> taskIds)
    {
        lock (_taskSchedulerSync)
        {
            _pendingTaskIds.Clear();
            foreach (var taskId in taskIds)
                _pendingTaskIds.Add(taskId);
        }
    }

    private static void EnsureTaskCodeunitExists(int codeunitId)
    {
        if (FindCodeunitTypePublic(codeunitId) != null)
            return;

        throw new InvalidOperationException(BuildMissingCodeunitMessage(codeunitId));
    }
}
