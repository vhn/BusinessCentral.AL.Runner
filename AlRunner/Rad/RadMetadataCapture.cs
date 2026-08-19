namespace AlRunner.Rad;

/// <summary>
/// Compiler metadata captured while preparing a RAD generation. The AL emitter runs
/// before Roslyn and Assembly.Load; buffering its registry writes here keeps a rejected
/// generation from leaking enum/report/page/xmlport metadata into the live runtime.
/// </summary>
internal sealed class RadMetadataCapture : IDisposable
{
    private static readonly AsyncLocal<RadMetadataCapture?> _current = new();
    private readonly RadMetadataCapture? _previous;
    private readonly List<Action> _registrations = new();
    private bool _disposed;

    private RadMetadataCapture()
    {
        _previous = _current.Value;
        _current.Value = this;
    }

    internal static RadMetadataCapture Begin() => new();

    /// <summary>
    /// The generation being prepared on this execution context, or null when there is none.
    ///
    /// <para>Exposed so a component that is entered on one thread but calls back on others —
    /// Microsoft's emitter, under <c>ConcurrentEmit</c> — can bind the capture once, on the way
    /// in. An <see cref="AsyncLocal{T}"/> does not flow onto a thread the runtime did not start
    /// for us, so reading it at callback time would silently answer "no generation" and apply
    /// the write to the live runtime instead of holding it.</para>
    /// </summary>
    internal static RadMetadataCapture? Current => _current.Value;

    /// <summary>Hold a registry write until this generation is accepted.</summary>
    internal void Defer(Action registration)
    {
        lock (_registrations) _registrations.Add(registration);
    }

    internal static void ApplyOrDefer(Action registration)
    {
        var capture = _current.Value;
        if (capture == null)
        {
            registration();
            return;
        }

        capture.Defer(registration);
    }

    internal void Apply(RadWorkspace workspace, RadChangeSet changes)
    {
        // Clear the previous identity before replaying the capture — using the object map
        // as it stands NOW, which is why this must run before workspace.Commit.
        foreach (var item in changes.Modified)
            if (workspace.Object(item.Key) is { } previous)
                DropStaleIdentity(workspace, previous);
        foreach (var item in changes.Removed)
            Drop(workspace, item);

        Action[] registrations;
        lock (_registrations) registrations = _registrations.ToArray();
        foreach (var registration in registrations) registration();
    }

    /// <summary>
    /// Everything one object contributed. For an object that is GONE — no re-registration
    /// is coming, so the entry has to be taken out by hand.
    ///
    /// Also used by the full-compile path, which cannot buffer its writes (the AL-output
    /// cache sidecar is serialized straight off these registries) but still has to
    /// unregister what the source tree no longer declares.
    /// </summary>
    internal static void Drop(RadWorkspace workspace, RadObjectRef item)
    {
        switch (item.Key.Kind)
        {
            case "Enum":
                AlEnumMetadataRegistry.Remove(item.Key.Id);
                break;
            case "Report":
                AlReportMetadataRegistry.Remove(item.Key.Id);
                break;
            case "Page":
                AlPageMetadataRegistry.Remove(item.Key.Id);
                break;
            case "XmlPort":
                AlXmlPortMetadataRegistry.Remove(item.Key.Id);
                break;
        }
        DropStaleIdentity(workspace, item);
    }

    /// <summary>
    /// Only what a re-registration would NOT overwrite. An object that survives the cycle
    /// re-registers itself, and every id-keyed registry is last-writer-wins — so removing
    /// those first would be pure risk: a re-emit that yields no metadata XML would leave
    /// the object with none at all rather than with its previous entry.
    ///
    /// Two registrations are not id-keyed and do need clearing:
    /// an enumextension's values live under (base enum id, the extension's own NAME), so a
    /// rename or a change of target adds a second entry beside the first; and report
    /// layouts accumulate into a list per report, so a layout the report no longer declares
    /// would survive its own removal.
    /// </summary>
    private static void DropStaleIdentity(RadWorkspace workspace, RadObjectRef item)
    {
        switch (item.Key.Kind)
        {
            case "EnumExtension":
                if (workspace.TryGetExtensionTarget(item.Key, out var target))
                    AlEnumMetadataRegistry.RemoveExtension(target.Id, item.Name);
                break;
            case "Report":
                AlReportLayoutRegistry.Remove(item.Key.Id);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (ReferenceEquals(_current.Value, this)) _current.Value = _previous;
    }
}
