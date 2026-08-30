using System.Reflection;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class AlCompilerStreamPatchesTests
{
    private readonly BcEngineFixture _engine;

    public AlCompilerStreamPatchesTests(BcEngineFixture engine) => _engine = engine;

    private sealed class DotNetValue(object value)
    {
        public object Value { get; } = value;
    }

    private static T WithIsolatedRoot<T>(Func<object, T> action)
    {
        Assembly.Load("Microsoft.Dynamics.Nav.Ncl");
        var rootType = typeof(BcRuntime).Assembly.GetType("AlRunner.Infrastructure.RootTreeObject")
            ?? throw new InvalidOperationException("RootTreeObject not found.");
        var root = Activator.CreateInstance(rootType, nonPublic: true)
            ?? throw new InvalidOperationException("RootTreeObject could not be constructed.");
        var rootField = typeof(BcRuntime).GetField(
            nameof(BcRuntime.RootTreeStub), BindingFlags.Public | BindingFlags.Static)!;
        var containerField = typeof(BcRuntime).GetField(
            "_skeletonSharedObjectContainer", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousRoot = rootField.GetValue(null);
        var previousContainer = containerField.GetValue(null);
        try
        {
            rootField.SetValue(null, root);
            containerField.SetValue(null, null);
            return action(root);
        }
        finally
        {
            if (containerField.GetValue(null) is IDisposable container)
                container.Dispose();
            containerField.SetValue(null, previousContainer);
            rootField.SetValue(null, previousRoot);
            if (root is IDisposable disposableRoot)
                disposableRoot.Dispose();
        }
    }

    [SkippableFact]
    public void DotNetToNavInStream_UsesSkeletonSharedObjectContainer()
    {
        TestArtifacts.SkipIf(
            !_engine.Ready,
            _engine.SkipReason ?? "the in-process BC engine is not ready (see BcEngineCollection).");

        byte[] payload = [0x11, 0x22, 0x33, 0x44];
        using var source = new MemoryStream(payload);

        WithIsolatedRoot(root =>
        {
            var input = BcRuntime.ALCompiler_DotNetToNavInStream(
                root,
                new DotNetValue(source));
            Assert.Equal("NavInStream", input.GetType().Name);
            Assert.Equal(
                payload.Length,
                Convert.ToInt32(input.GetType().GetProperty("ALLength")!.GetValue(input)));
            (input as IDisposable)?.Dispose();
            return true;
        });
    }

    [Fact]
    public void DotNetToNavInStream_RejectsNonStreamValue()
    {
        WithIsolatedRoot(root =>
        {
            var exception = Assert.ThrowsAny<Exception>(() =>
                BcRuntime.ALCompiler_DotNetToNavInStream(
                    root,
                    new DotNetValue(new object())));
            Assert.Equal("NavNCLConversionException", exception.GetType().Name);
            return true;
        });
    }
}
