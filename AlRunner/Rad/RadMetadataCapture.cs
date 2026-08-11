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

    internal static void ApplyOrDefer(Action registration)
    {
        var capture = _current.Value;
        if (capture == null)
        {
            registration();
            return;
        }

        lock (capture._registrations) capture._registrations.Add(registration);
    }

    internal void Apply(RadWorkspace workspace, RadChangeSet changes)
    {
        // Remove the previous identity before applying the new capture. This matters for
        // report layouts that disappeared and enumextensions that moved to another target.
        foreach (var item in changes.Modified)
            if (workspace.Object(item.Key) is { } previous)
                Remove(workspace, previous);
        foreach (var item in changes.Removed)
            Remove(workspace, item);

        Action[] registrations;
        lock (_registrations) registrations = _registrations.ToArray();
        foreach (var registration in registrations) registration();
    }

    private static void Remove(RadWorkspace workspace, RadObjectRef item)
    {
        switch (item.Key.Kind)
        {
            case "Enum":
                AlEnumMetadataRegistry.Remove(item.Key.Id);
                break;
            case "EnumExtension":
                if (workspace.TryGetExtensionTarget(item.Key, out var target))
                    AlEnumMetadataRegistry.RemoveExtension(target.Id, item.Name);
                break;
            case "Report":
                AlReportMetadataRegistry.Remove(item.Key.Id);
                AlReportLayoutRegistry.Remove(item.Key.Id);
                break;
            case "Page":
                AlPageMetadataRegistry.Remove(item.Key.Id);
                break;
            case "XmlPort":
                AlXmlPortMetadataRegistry.Remove(item.Key.Id);
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
