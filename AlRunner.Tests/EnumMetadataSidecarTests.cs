using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class EnumMetadataSidecarTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "al-runner-enum-sidecar-tests", Guid.NewGuid().ToString("N"));

    public EnumMetadataSidecarTests()
    {
        Directory.CreateDirectory(_root);
        AlEnumMetadataRegistry.Clear();
    }

    public void Dispose()
    {
        AlEnumMetadataRegistry.Clear();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void DependencySidecar_ReplaysExtensionWithoutReplacingRegisteredBaseEnum()
    {
        var sidecarPath = Path.Combine(_root, "dependency.enum-registry.json");

        AlEnumMetadataRegistry.RegisterExtension(
            7000,
            "Price Calc. Method - Test",
            ["Test Price", "Test Price (not Implemented)"],
            [130514, 130515]);
        AlEnumMetadataRegistry.SaveSidecar(sidecarPath, [7000]);

        AlEnumMetadataRegistry.Clear();
        AlEnumMetadataRegistry.Register(
            7000,
            "Price Calculation Method",
            [" ", "Lowest Price"],
            [0, 1]);

        AlEnumMetadataRegistry.LoadSidecar(sidecarPath);

        Assert.True(AlEnumMetadataRegistry.TryGet(7000, out var merged));
        Assert.Equal("Price Calculation Method", merged.Name);
        Assert.Equal(
            [" ", "Lowest Price", "Test Price", "Test Price (not Implemented)"],
            merged.Options);
        Assert.Equal([0, 1, 130514, 130515], merged.Indexes);
    }
}
