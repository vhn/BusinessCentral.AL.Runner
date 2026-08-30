using AlRunner.Patches;
using Microsoft.Dynamics.Nav.Types;
using System.Collections;
using System.Reflection;
using Xunit;

namespace AlRunner.Tests;

public sealed class RunnerModalDispatchTests
{
    [Fact]
    public void DispatchNonModalPage_OpensBeforeHandlerAndClosesAfterwards()
    {
        var events = new List<string>();
        var form = new FakeForm(events);

        RunnerModalDispatch.DispatchNonModalPage(form, () =>
        {
            Assert.True(form.IsOpen);
            events.Add("handler");
        });

        Assert.False(form.IsOpen);
        Assert.Equal(["open", "handler", "close"], events);
    }

    [Fact]
    public void DispatchNonModalPage_DoesNotCloseFormItDidNotOpen()
    {
        var events = new List<string>();
        var form = new FakeForm(events, initiallyOpen: true);

        RunnerModalDispatch.DispatchNonModalPage(form, () => events.Add("handler"));

        Assert.True(form.IsOpen);
        Assert.Equal(["handler"], events);
    }

    [Fact]
    public void DispatchNonModalPage_ClosesNewFormWithoutMaskingHandlerError()
    {
        var events = new List<string>();
        var form = new FakeForm(events, closeError: "close failed");

        var error = Assert.Throws<InvalidOperationException>(() =>
            RunnerModalDispatch.DispatchNonModalPage(form, () =>
            {
                events.Add("handler");
                throw new InvalidOperationException("handler failed");
            }));

        Assert.Equal("handler failed", error.Message);
        Assert.True(form.IsOpen);
        Assert.Equal(["open", "handler", "close"], events);
    }

    [Fact]
    public void DispatchNonModalPage_CloseErrorAfterSuccessfulHandlerIsPrimary()
    {
        var events = new List<string>();
        var form = new FakeForm(events, closeError: "close failed");

        var error = Assert.Throws<InvalidOperationException>(() =>
            RunnerModalDispatch.DispatchNonModalPage(form, () => events.Add("handler")));

        Assert.Equal("close failed", error.Message);
        Assert.Equal(["open", "handler", "close"], events);
    }

    [Fact]
    public void TrappedPage_TransfersCloseToLiveTestPageLifetime()
    {
        var events = new List<string>();
        var form = new FakeForm(events);
        var handle = Guid.NewGuid();
        Action? closeOnRelease = null;

        using var transfer = RunnerModalDispatch.BeginTrappedFormClose(handle, form);
        RunnerModalDispatch.DispatchNonModalPage(
            form,
            () => closeOnRelease = RunnerModalDispatch.TakeTrappedFormClose(handle),
            () => transfer.OwnershipTransferred);

        Assert.True(form.IsOpen);
        Assert.NotNull(closeOnRelease);
        Assert.Equal(["open"], events);

        var page = new LiveNavTestPage(null, new Dictionary<int, int>());
        page.SetClientFormClose(closeOnRelease!);
        page.Close();
        page.Dispose();
        page.Dispose();

        Assert.False(form.IsOpen);
        Assert.Equal(["open", "close"], events);
    }

    [Fact]
    public void TrappedPage_DispatchErrorClosesTransferredFormOnlyOnce()
    {
        var events = new List<string>();
        var form = new FakeForm(events);
        var handle = Guid.NewGuid();
        Action? closeOnRelease = null;

        using var transfer = RunnerModalDispatch.BeginTrappedFormClose(handle, form);
        var error = Assert.Throws<InvalidOperationException>(() =>
            RunnerModalDispatch.DispatchNonModalPage(
                form,
                () =>
                {
                    closeOnRelease = RunnerModalDispatch.TakeTrappedFormClose(handle);
                    throw new InvalidOperationException("handler failed");
                },
                () => transfer.OwnershipTransferred));

        Assert.Equal("handler failed", error.Message);
        Assert.False(form.IsOpen);
        Assert.NotNull(closeOnRelease);
        closeOnRelease!();
        Assert.Equal(["open", "close"], events);
    }

    [Fact]
    public void LiveTestPage_ClientCloseRunsQueryAndCloseTriggersOnce()
    {
        var form = new FakeTriggerForm();
        var pageInstance = CreatePageInstance(form);
        var page = new LiveNavTestPage(
            null, new Dictionary<int, int>(), creatable: true, pageInstance);
        page.SetClientFormClose(form.CloseThroughClient);

        page.Close();
        page.Dispose();

        Assert.Equal(1, form.QueryCloseCount);
        Assert.Equal(1, form.CloseCount);
    }

    [Fact]
    public void LiveTestPage_DeferredClientCloseDoesNotRaiseOnCloseEarly()
    {
        var form = new FakeTriggerForm();
        var page = new LiveNavTestPage(
            null,
            new Dictionary<int, int>(),
            creatable: true,
            CreatePageInstance(form));
        page.SetClientFormClose(closeOnRelease: null);

        page.Close();

        Assert.Equal(1, form.QueryCloseCount);
        Assert.Equal(0, form.CloseCount);
        form.CloseThroughClient();
        Assert.Equal(1, form.CloseCount);
    }

    [Fact]
    public void LiveTestPage_ClientCloseErrorAfterSuccessfulFlushIsPrimary()
    {
        var page = new LiveNavTestPage(null, new Dictionary<int, int>());
        page.SetClientFormClose(() => throw new InvalidOperationException("close failed"));

        var error = Assert.Throws<InvalidOperationException>(page.Close);

        Assert.Equal("close failed", error.Message);
    }

    [Fact]
    public void UnhandledNonModalForm_PreservesStableOosContract()
    {
        var error = Assert.Throws<AlRunner.Infrastructure.RunnerOutOfScopeException>(
            RunnerModalDispatch.ThrowUnhandledNonModalForm);

        Assert.Equal("NavForm.RunAsync", error.Api);
        Assert.Equal("non-modal-ui", error.Reason);
        Assert.Equal("ui", error.DocAnchor);
    }

    private sealed class FakeForm
    {
        private readonly List<string> _events;
        private readonly string? _closeError;

        public FakeForm(
            List<string> events,
            bool initiallyOpen = false,
            string? closeError = null)
        {
            _events = events;
            _closeError = closeError;
            IsOpen = initiallyOpen;
        }

        public bool IsOpen { get; private set; }

        public void OpenForm()
        {
            _events.Add("open");
            IsOpen = true;
        }

        public void CloseForm(FakeFormResult result)
        {
            _events.Add("close");
            if (_closeError != null)
                throw new InvalidOperationException(_closeError);
            IsOpen = false;
        }
    }

    private sealed class FakeTriggerForm
    {
        public int QueryCloseCount { get; private set; }
        public int CloseCount { get; private set; }

        private bool OnQueryClosePage(FormResult result)
        {
            QueryCloseCount++;
            return true;
        }

        private void OnClosePage() => CloseCount++;

        public void CloseThroughClient() => OnClosePage();
    }

    private static RunnerPageInstance CreatePageInstance(object form)
    {
        var constructor = typeof(RunnerPageInstance)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single();
        return (RunnerPageInstance)constructor.Invoke([
            form,
            form,
            null,
            1,
            new Hashtable(),
        ]);
    }

    private enum FakeFormResult
    {
        None
    }
}
