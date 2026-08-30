using Xunit;

namespace AlRunner.Tests;

public sealed class DependencyLoaderDependencyScopeTests
{
    [Fact]
    public void ScopeResolvedDeps_RestoresAnInitiallyUnsetDependencyState()
    {
        var field = typeof(BcCompiler).GetField(
            "_resolvedDeps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var scoped = Packages(App("Scoped", "NaviPartner"));

        try
        {
            field.SetValue(null, null);
            using (BcCompiler.ScopeResolvedDeps(scoped))
                Assert.Same(scoped, field.GetValue(null));

            Assert.Null(field.GetValue(null));
        }
        finally
        {
            field.SetValue(null, previous);
        }
    }

    [Fact]
    public void SourceDependency_SeesOnlyItsOwnDeclaredClosure()
    {
        var system = App("System", "Microsoft");
        var systemApplication = App("System Application", "Microsoft", dependencies: [Ref(system)]);
        var businessFoundation = App("Business Foundation", "Microsoft", dependencies: [Ref(systemApplication)]);
        var baseApplication = App("Base Application", "Microsoft",
            dependencies: [Ref(systemApplication), Ref(businessFoundation)]);
        var application = App("Application", "Microsoft",
            dependencies: [Ref(systemApplication), Ref(businessFoundation), Ref(baseApplication)]);
        var unrelated = App("Unrelated Test Library", "Microsoft", dependencies: [Ref(application)]);
        var testRunner = App("Test Runner", "Microsoft", platform: new Version(28, 0, 0, 0));
        var ordered = Packages(system, systemApplication, businessFoundation, baseApplication,
            application, unrelated, testRunner);

        var closure = DependencyLoader.DependencyCompileClosure(testRunner, ordered);

        var dependency = Assert.Single(closure);
        Assert.Equal("System", dependency.Manifest.Name);
    }

    [Fact]
    public void ApplicationRoot_IncludesImplicitApplicationAndItsTransitiveDependencies()
    {
        var system = App("System", "Microsoft");
        var systemApplication = App("System Application", "Microsoft", dependencies: [Ref(system)]);
        var businessFoundation = App("Business Foundation", "Microsoft", dependencies: [Ref(systemApplication)]);
        var baseApplication = App("Base Application", "Microsoft",
            dependencies: [Ref(systemApplication), Ref(businessFoundation)]);
        var application = App("Application", "Microsoft",
            dependencies: [Ref(systemApplication), Ref(businessFoundation), Ref(baseApplication)]);
        var unrelated = App("Unrelated Test Library", "Microsoft");
        var retail = App("NP Retail", "NaviPartner",
            application: new Version(28, 1, 0, 0), platform: new Version(28, 0, 0, 0));
        var ordered = Packages(system, systemApplication, businessFoundation, baseApplication,
            application, unrelated, retail);

        var closure = DependencyLoader.DependencyCompileClosure(retail, ordered);

        Assert.Equal(
            ["System", "System Application", "Business Foundation", "Base Application", "Application"],
            closure.Select(d => d.Manifest.Name));
    }

    private static AppManifest App(
        string name,
        string publisher,
        IReadOnlyList<DependencyRef>? dependencies = null,
        Version? application = null,
        Version? platform = null)
        => new(publisher, name, new Version(28, 1, 0, 0), Guid.NewGuid(),
            dependencies ?? [], application, platform);

    private static DependencyRef Ref(AppManifest app)
        => new(app.AppId, app.Name, app.Publisher, app.Version);

    private static IReadOnlyList<(AppManifest Manifest, string AppPath)> Packages(
        params AppManifest[] manifests)
        => manifests.Select((manifest, index) => (manifest, $"/{index}.app")).ToArray();
}
