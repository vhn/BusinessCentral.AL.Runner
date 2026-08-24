using AlRunner.Infrastructure;
using Xunit;

namespace AlRunner.Tests;

public sealed class MicrosoftProvisioningRequirementsTests : IDisposable
{
    private readonly string _root;

    public MicrosoftProvisioningRequirementsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "al-runner-ms-provision", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static void WriteManifest(string dir, string name, string application,
        string dependencies = "")
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "app.json"), $$"""
        {
          "id": "{{Guid.NewGuid()}}",
          "name": "{{name}}",
          "publisher": "NaviPartner",
          "version": "1.0.0.0",
          "platform": "1.0.0.0",
          "application": "{{application}}",
          "dependencies": [ {{dependencies}} ],
          "runtime": "16.0",
          "idRanges": [ { "from": 60100, "to": 60199 } ]
        }
        """);
    }

    [Fact]
    public void EmptyAlpackages_RequirementsComeFromAllTargetAppJsonFiles()
    {
        WriteManifest(Path.Combine(_root, "Application"), "NP Retail", "27.0.0.0");
        WriteManifest(Path.Combine(_root, "Test"), "NP Retail Tests", "27.0.0.0", """
          {
            "id": "5d86850b-0d76-4eca-bd7b-951ad998e997",
            "publisher": "Microsoft",
            "name": "Tests-TestLibraries",
            "version": "27.0.0.0"
          },
          {
            "id": "23de40a6-dfe8-4f80-80db-d70f83ce8caf",
            "publisher": "Microsoft",
            "name": "Test Runner",
            "version": "27.0.0.0"
          }
        """);

        var requirements = ProvisioningCheck.DeriveMicrosoftRequirements(new[] { _root });

        Assert.True(requirements.PlatformAppsRequired);
        Assert.True(requirements.TestAppsRequired);
        Assert.Equal(new Version(27, 0, 0, 0), requirements.MinimumVersion);
        Assert.Equal(2, requirements.ManifestPaths.Count);
        Assert.Contains("Tests-TestLibraries", requirements.RequiredTestAppNames);
        Assert.Contains("Test Runner", requirements.RequiredTestAppNames);
        Assert.False(Directory.Exists(Path.Combine(_root, ".alpackages")));
    }

    [Fact]
    public void HiddenWorkspaceManifest_DoesNotChangeTargetRequirements()
    {
        WriteManifest(Path.Combine(_root, "Application"), "Application", "27.0.0.0");
        WriteManifest(Path.Combine(_root, ".context", "container-prep"), "Unrelated Tests", "99.0.0.0", """
          {
            "id": "5d86850b-0d76-4eca-bd7b-951ad998e997",
            "publisher": "Microsoft",
            "name": "Tests-TestLibraries",
            "version": "99.0.0.0"
          }
        """);

        var requirements = ProvisioningCheck.DeriveMicrosoftRequirements(new[] { _root });

        Assert.True(requirements.PlatformAppsRequired);
        Assert.False(requirements.TestAppsRequired);
        Assert.Equal(new Version(27, 0, 0, 0), requirements.MinimumVersion);
        Assert.Single(requirements.ManifestPaths);
    }

    [Fact]
    public void NonMicrosoftDependency_DoesNotRequestTheMicrosoftTestToolkit()
    {
        WriteManifest(_root, "Application", "28.1.0.0", """
          {
            "id": "992c2309-cca4-43cb-9e41-911f482ec088",
            "publisher": "NaviPartner",
            "name": "NP Retail",
            "version": "2700.0.0.0"
          }
        """);

        var requirements = ProvisioningCheck.DeriveMicrosoftRequirements(new[] { _root });

        Assert.True(requirements.PlatformAppsRequired);
        Assert.False(requirements.TestAppsRequired);
        Assert.Empty(requirements.RequiredTestAppNames);
    }

    [Fact]
    public void MicrosoftApplicationExtension_DoesNotRequestTheMicrosoftTestToolkit()
    {
        WriteManifest(_root, "Application", "28.1.0.0", """
          {
            "id": "c1335042-3002-4257-bf8a-75c898ccb1b8",
            "publisher": "Microsoft",
            "name": "Sales and Inventory Forecast",
            "version": "28.0.0.0"
          }
        """);

        var requirements = ProvisioningCheck.DeriveMicrosoftRequirements(new[] { _root });

        Assert.True(requirements.PlatformAppsRequired);
        Assert.False(requirements.TestAppsRequired);
        Assert.Empty(requirements.RequiredTestAppNames);
    }

    [Fact]
    public void ManifestWithoutMicrosoftRootsOrDependencies_RequiresNoProvisioningSets()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.json"), $$"""
        {
          "id": "{{Guid.NewGuid()}}",
          "name": "Standalone",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [],
          "runtime": "16.0",
          "idRanges": [ { "from": 60100, "to": 60199 } ]
        }
        """);

        var requirements = ProvisioningCheck.DeriveMicrosoftRequirements(new[] { _root });

        Assert.False(requirements.PlatformAppsRequired);
        Assert.False(requirements.TestAppsRequired);
        Assert.Null(requirements.MinimumVersion);
    }

    [Fact]
    public void NonStringDependencyFields_DoNotCrashTheProvisioningPreScan()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.json"), $$"""
        {
          "id": "{{Guid.NewGuid()}}",
          "name": "Malformed Dependency Fixture",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "application": "28.1.0.0",
          "dependencies": [
            42,
            { "publisher": 123, "name": ["not", "a", "string"], "version": false }
          ],
          "runtime": "16.0",
          "idRanges": [ { "from": 60100, "to": 60199 } ]
        }
        """);

        var requirements = ProvisioningCheck.DeriveMicrosoftRequirements(new[] { _root });

        Assert.True(requirements.PlatformAppsRequired);
        Assert.False(requirements.TestAppsRequired);
        Assert.Equal(new Version(28, 1, 0, 0), requirements.MinimumVersion);
    }

    [Fact]
    public void NonObjectManifestRoot_DoesNotCrashTheProvisioningPreScan()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.json"), "[1, 2, 3]");

        var requirements = ProvisioningCheck.DeriveMicrosoftRequirements(new[] { _root });

        Assert.False(requirements.PlatformAppsRequired);
        Assert.False(requirements.TestAppsRequired);
        Assert.Null(requirements.MinimumVersion);
    }
}
