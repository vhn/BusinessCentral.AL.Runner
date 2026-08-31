/// <summary>
/// docs/scope.md §3.6: no scheduler is available headlessly. ALTaskScheduler.CanCreateTask
/// remains false so guarded AL skips scheduling. Unguarded creation and lifecycle calls use the
/// runner's membership-only pending-id stub; no task is executed. The tests below pin that
/// runner-specific contract and the codeunit-resolution regression from #1733.
/// </summary>
codeunit 64301 "TSC Tests"
{
    Subtype = Test;

    var
        TscAssert: Codeunit "TSC Assert";

    [Test]
    procedure CanCreateTask_ReturnsFalse()
    begin
        // The headless runner has no scheduler: scope.md §3.6 documents CanCreateTask as
        // faithfully false so guarded AL (`if TaskScheduler.CanCreateTask then ...`) skips
        // creation cleanly.
        TscAssert.IsFalse(TaskScheduler.CanCreateTask(), 'CanCreateTask must be false: the runner has no scheduler.');
    end;

    [Test]
    procedure PendingTaskLifecycle_RemembersIdsWithoutExecutingTasks()
    var
        FirstTaskId: Guid;
        SecondTaskId: Guid;
        UnknownTaskId: Guid;
    begin
        UnknownTaskId := CreateGuid();
        TscAssert.IsFalse(TaskScheduler.TaskExists(UnknownTaskId), 'An unknown task id must not exist.');
        TscAssert.IsFalse(TaskScheduler.CancelTask(UnknownTaskId), 'An unknown task id cannot be cancelled.');
        TscAssert.IsFalse(TaskScheduler.SetTaskReady(UnknownTaskId), 'An unknown task id cannot be made ready.');
        TscAssert.IsFalse(
            TaskScheduler.SetTaskReady(UnknownTaskId, CurrentDateTime()),
            'An unknown task id cannot be rescheduled.');

        FirstTaskId := TaskScheduler.CreateTask(CODEUNIT::"TSC Never Run", 0, false);
        SecondTaskId := TaskScheduler.CreateTask(
            CODEUNIT::"TSC Never Run", CODEUNIT::"TSC Never Run", true);
        TscAssert.IsFalse(IsNullGuid(FirstTaskId), 'CreateTask must return an opaque non-empty task id.');
        TscAssert.IsFalse(IsNullGuid(SecondTaskId), 'CreateTask must return an opaque non-empty task id.');
        TscAssert.IsTrue(FirstTaskId <> SecondTaskId, 'Each CreateTask call must return a fresh task id.');
        TscAssert.IsTrue(TaskScheduler.TaskExists(FirstTaskId), 'A created task id must remain pending.');
        TscAssert.IsTrue(TaskScheduler.TaskExists(SecondTaskId), 'Pending tasks must be keyed independently.');

        TscAssert.IsTrue(TaskScheduler.SetTaskReady(FirstTaskId), 'A pending task can be marked ready.');
        TscAssert.IsTrue(
            TaskScheduler.SetTaskReady(FirstTaskId, CurrentDateTime()),
            'A pending task can accept a new NotBefore value.');
        TscAssert.IsTrue(TaskScheduler.TaskExists(FirstTaskId), 'SetTaskReady must not dispatch or remove the task.');

        TscAssert.IsTrue(TaskScheduler.CancelTask(FirstTaskId), 'A pending task can be cancelled.');
        TscAssert.IsFalse(TaskScheduler.TaskExists(FirstTaskId), 'A cancelled task must no longer exist.');
        TscAssert.IsFalse(TaskScheduler.CancelTask(FirstTaskId), 'A cancelled task cannot be cancelled twice.');
        TscAssert.IsFalse(TaskScheduler.SetTaskReady(FirstTaskId), 'A cancelled task cannot be made ready.');
        TscAssert.IsTrue(TaskScheduler.TaskExists(SecondTaskId), 'Cancelling one task must not remove another.');
        TscAssert.IsTrue(TaskScheduler.CancelTask(SecondTaskId), 'The second pending task can be cancelled.');

        // Regression for #1733: validation must resolve the requested target, not accidentally
        // report the calling test codeunit as missing.
        asserterror TaskScheduler.CreateTask(139999, 0, true);
        TscAssert.IsTrue(
            StrPos(GetLastErrorText(), 'Codeunit 139999 is not present') > 0,
            StrSubstNo('Expected the requested unknown codeunit id in the error, got: %1', GetLastErrorText()));

        asserterror TaskScheduler.CreateTask(CODEUNIT::"TSC Never Run", 139998, true);
        TscAssert.IsTrue(
            StrPos(GetLastErrorText(), 'Codeunit 139998 is not present') > 0,
            StrSubstNo('Expected the requested unknown failure codeunit id in the error, got: %1', GetLastErrorText()));
    end;
}
