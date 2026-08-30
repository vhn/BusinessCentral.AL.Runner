// RunnerModalDispatch — stand in for the CLIENT half of BC's page-handler round-trips.
//
// HOW BC DOES IT
//   NavTestExecution finds the test's page handler or trap, then asks the CLIENT to run the
//   form. The modal path pushes a delegate onto dialogHandlerStack before calling:
//       TestClientProxy<IClientCallbackHandler>.Proxy(ServiceConnection.CallbackHandler)
//           .FormRunModal(runRequest);
//   A real client opens the page and calls back into the server, which lands in
//   NavTestExecution.ShowDialog(handle) — that pops the delegate, runs the handler, and the
//   resulting FormResult is stored with SetLastFormResult for TestHandleModalForm to return.
//
// WHY THIS EXISTS
//   The runner has no client, so that call reached BC's HeadlessClientCallback, whose whole
//   purpose is to refuse ("Client callbacks are not supported on {0}"). Implementing the
//   real IService/IClientCallbackHandler surface to satisfy one call means ~130 members that
//   exist only to throw, so NclCecilRewrite instead redirects that single call site here.
//
//   This is not a shortcut around BC's logic — it performs the callback the client would have
//   caused through BC's own ShowForm/ShowDialog methods. Handler selection, trap attachment,
//   AL invocation, and modal results remain in BC's code.
using System.Reflection;
using System.Threading;

namespace AlRunner.Patches;

public static class RunnerModalDispatch
{
    private static readonly AsyncLocal<NonModalCloseTransfer?> PendingNonModalClose = new();

    /// <summary>
    /// Called from the rewritten NavTestExecution.TestHandleForm in place of the client
    /// callback. BC's ShowForm performs the non-modal PageHandler/TestPage dispatch.
    /// </summary>
    public static void FormRun(object testExecution, object runRequest)
    {
        if (testExecution == null || runRequest == null)
            throw new InvalidOperationException(
                "TestPage non-modal dispatch requires a test-execution context and request");

        var handle = FormHandleOf(runRequest);
        var form = RegisteredForm(handle)
            ?? throw new InvalidOperationException(
                $"No form is registered under non-modal handle {handle} — Ncl shape changed; do not commit");
        var showForm = testExecution.GetType().GetMethod(
            "ShowForm",
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(Guid) },
            modifiers: null)
            ?? throw new InvalidOperationException(
                "NavTestExecution.ShowForm(Guid) not found — Ncl shape changed; do not commit");
        using var closeTransfer = HasTrap(testExecution, form)
            ? BeginTrappedFormClose(handle, form)
            : null;
        DispatchNonModalPage(
            form,
            () => Invoke(showForm, testExecution, new object?[] { handle }),
            () => closeTransfer?.OwnershipTransferred == true);
    }

    /// <summary>
    /// Open the server form before BC builds the TestPage and invokes its PageHandler, then
    /// close only the registration created here. A real client's TestPage proxy performs this
    /// close after the handler returns; without it the form remains registered with NavCompany.
    /// </summary>
    internal static void DispatchNonModalPage(
        object form,
        Action dispatch,
        Func<bool>? keepOpenAfterDispatch = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(dispatch);

        var opened = TryOpenForm(form);
        var completed = false;
        try
        {
            dispatch();
            completed = true;
        }
        finally
        {
            if (opened && (!completed || keepOpenAfterDispatch?.Invoke() != true))
                CloseForm(form, result: null, suppressErrors: !completed);
        }
    }

    /// <summary>
    /// Marks a trapped non-modal page so the ITestPage created during ShowForm can take over
    /// closing it. The marker follows the synchronous client callback and nests safely when an
    /// OnOpenPage trigger opens another trapped page.
    /// </summary>
    internal static NonModalCloseTransfer BeginTrappedFormClose(Guid handle, object form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new NonModalCloseTransfer(handle, form, PendingNonModalClose.Value);
    }

    /// <summary>
    /// Transfers closing the trapped form to the LiveNavTestPage that BC attaches to the AL
    /// TestPage variable. Returning null means this GetPage call is not the trapped non-modal
    /// callback currently being dispatched.
    /// </summary>
    internal static Action? TakeTrappedFormClose(Guid handle)
    {
        for (var transfer = PendingNonModalClose.Value; transfer != null; transfer = transfer.Parent)
        {
            if (transfer.Handle != handle) continue;
            if (transfer.OwnershipTransferred)
                throw new InvalidOperationException(
                    $"Non-modal form handle {handle} transferred close ownership more than once");

            transfer.OwnershipTransferred = true;
            return () => CloseForm(transfer.Form, result: null, suppressErrors: false);
        }

        return null;
    }

    internal sealed class NonModalCloseTransfer : IDisposable
    {
        private bool _disposed;

        internal NonModalCloseTransfer(Guid handle, object form, NonModalCloseTransfer? parent)
        {
            Handle = handle;
            Form = form;
            Parent = parent;
            PendingNonModalClose.Value = this;
        }

        internal Guid Handle { get; }
        internal object Form { get; }
        internal NonModalCloseTransfer? Parent { get; }
        internal bool OwnershipTransferred { get; set; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(PendingNonModalClose.Value, this))
                PendingNonModalClose.Value = Parent;
        }
    }

    /// <summary>Preserves the existing loud boundary for Page.Run without a handler.</summary>
    public static void ThrowUnhandledNonModalForm()
        => AlRunner.Infrastructure.RunnerScope.ThrowOutOfScope(
            "NavForm.RunAsync", "non-modal-ui", "ui");

    /// <summary>
    /// Called from the rewritten NavTestExecution.TestHandleModalForm in place of the client
    /// callback. Signature matches the call site's stack shape: the NavTestExecution (left by
    /// the original `ldarg.0`) and the FormRunModalRequest.
    /// </summary>
    public static void FormRunModal(object testExecution, object runRequest)
    {
        if (testExecution == null || runRequest == null)
            throw new AlRunner.Infrastructure.RunnerOutOfScopeException(
                "TestPage modal dispatch",
                "testpage-modal — the runner was asked to run a modal page with no test-execution "
                + "context or no request. See docs/scope.md");

        var handle = FormHandleOf(runRequest);
        var type = testExecution.GetType();

        // Open the page before handing it to the handler. A real client opens the form on the
        // server as part of this round trip, and opening is what raises the page's OnOpenPage
        // trigger — the single place an AL page is allowed to initialise the state its
        // controls and actions then read. Skipping it meant every handler drove a page whose
        // OnOpenPage had never run, so anything that trigger sets up simply was not there.
        //
        // Deliberately NOT wrapped in a catch: OnOpenPage is AL, and an Error() raised there
        // is a real test failure that must reach the test, not a runner detail to absorb.
        var form = RegisteredForm(handle);
        var opened = TryOpenForm(form);

        // BC's own ShowDialog: pops dialogHandlerStack and runs the pushed handler delegate,
        // which builds the NavTestPage and invokes the AL [ModalPageHandler].
        var showDialog = type.GetMethod("ShowDialog", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "NavTestExecution.ShowDialog not found — Ncl shape changed; do not commit");
        object? result = null;
        var completed = false;
        try
        {
            result = Invoke(showDialog, testExecution, new object?[] { handle });
            completed = true;
        }
        finally
        {
            // Close only what this method opened, and only through BC's own CloseForm, so the
            // form leaves the company's registry exactly the way BC would have left it.
            if (opened) CloseForm(form!, result: null, suppressErrors: !completed);
        }

        // The handler's outcome (OK/Cancel) is what the AL that called RunModal receives.
        // TestHandleModalForm reads it off formResultStack, which SetLastFormResult writes.
        var setLastFormResult = type.GetMethod("SetLastFormResult",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "NavTestExecution.SetLastFormResult not found — Ncl shape changed; do not commit");
        if (result != null && setLastFormResult.GetParameters()[0].ParameterType.IsInstanceOfType(result))
            Invoke(setLastFormResult, testExecution, new[] { result });
    }

    /// <summary>
    /// Replacement for NavSession.SetServerFormRequestData, whose real body throws
    /// NotSupportedException outright when there is no service connection — which there never
    /// is here — before the modal dispatch above is ever reached.
    ///
    /// The real body delegates to the server connection to fill the request the CLIENT will
    /// act on: which page, its parameters, its handle. Of that, the only field anything in
    /// this process reads is FormHandle: BC's pushed dialog delegate uses it to fetch the
    /// page, and NavCompany.GetRegisteredForm is keyed by exactly that value. So set it from
    /// the form and leave the client-facing rest alone — there is no client to render it.
    /// </summary>
    public static void SetServerFormRequestData(object session, object form, object parameters, object data)
    {
        if (form == null || data == null) return;

        var handle = form.GetType()
            .GetProperty("Handle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(form)
            ?? throw new InvalidOperationException(
                "NavForm.Handle not found — Ncl shape changed; do not commit");

        var formHandle = data.GetType()
            .GetProperty("FormHandle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "FormRunData.FormHandle not found — Ncl shape changed; do not commit");
        formHandle.SetValue(data, handle);
    }

    /// <summary>The NavForm BC registered under <paramref name="handle"/>, or null.</summary>
    private static object? RegisteredForm(Guid handle)
    {
        try
        {
            var session = AlRunner.BcRuntime.SkeletonSession;
            var company = session?.GetType()
                .GetProperty("Company", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?
                .GetValue(session);
            var get = company?.GetType().GetMethod("GetRegisteredForm",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null, types: new[] { typeof(Guid) }, modifiers: null);
            return get?.Invoke(company, new object[] { handle });
        }
        catch (TargetInvocationException) { return null; }
    }

    /// <summary>
    /// Open <paramref name="form"/> through BC's own OpenForm (which raises OnOpenPage).
    /// Returns whether this call is the one that opened it — a form BC already opened must
    /// not be opened again (NavNCLFormAlreadyOpenedException) nor closed by us.
    /// </summary>
    private static bool TryOpenForm(object? form)
    {
        if (form == null) return false;
        var isOpen = form.GetType().GetProperty("IsOpen",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (isOpen?.GetValue(form) is true) return false;

        var openForm = form.GetType().GetMethod("OpenForm",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, types: Type.EmptyTypes, modifiers: null);
        if (openForm == null) return false;
        Invoke(openForm, form, Array.Empty<object?>());
        return true;
    }

    private static bool HasTrap(object testExecution, object form)
    {
        var hasTrap = testExecution.GetType().GetMethod(
            "HasTrap",
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(int) },
            modifiers: null)
            ?? throw new InvalidOperationException(
                "NavTestExecution.HasTrap(Int32) not found — Ncl shape changed; do not commit");
        return Invoke(hasTrap, testExecution, new object?[] { PageIdOf(form) }) is true;
    }

    private static int PageIdOf(object form)
    {
        var objectId = form.GetType()
            .GetProperty("ObjectId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(form)
            ?? throw new InvalidOperationException(
                "NavForm.ObjectId not found — Ncl shape changed; do not commit");
        return objectId.GetType()
            .GetProperty("ObjectNumber", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(objectId) is int pageId
            ? pageId
            : throw new InvalidOperationException(
                "NavForm.ObjectId.ObjectNumber not found — Ncl shape changed; do not commit");
    }

    private static void CloseForm(object form, object? result, bool suppressErrors)
    {
        try
        {
            var isOpen = form.GetType().GetProperty(
                "IsOpen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (isOpen?.GetValue(form) is false) return;

            var closeForm = form.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "CloseForm" && m.GetParameters().Length == 1);
            if (closeForm == null)
                throw new InvalidOperationException(
                    "NavForm.CloseForm(FormResult) not found — Ncl shape changed; do not commit");
            var formResultType = closeForm.GetParameters()[0].ParameterType;
            var arg = result != null && formResultType.IsInstanceOfType(result)
                ? result
                : Enum.ToObject(formResultType, 0);
            Invoke(closeForm, form, new[] { arg });
        }
        catch (Exception ex) when (suppressErrors)
        {
            // Closing is cleanup, not the test's subject: a failure here must not replace the
            // handler's own outcome, but it must not vanish either.
            Console.Error.WriteLine($"[RunnerModalDispatch] CloseForm failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Guid FormHandleOf(object runRequest)
    {
        var data = runRequest.GetType()
            .GetProperty("Data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(runRequest)
            ?? throw new InvalidOperationException(
                "FormRunRequest.Data not found — Ncl shape changed; do not commit");
        return data.GetType()
            .GetProperty("FormHandle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(data) is Guid handle
            ? handle
            : throw new InvalidOperationException(
                "FormRunData.FormHandle not found — Ncl shape changed; do not commit");
    }

    /// <summary>Invoke, surfacing the AL handler's own Error() rather than a reflection wrapper.</summary>
    private static object? Invoke(MethodInfo method, object target, object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw; // unreachable
        }
    }
}
