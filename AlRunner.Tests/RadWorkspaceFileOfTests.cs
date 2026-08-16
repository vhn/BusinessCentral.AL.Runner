using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// Pins <see cref="RadWorkspace.FileOf"/> against an oracle computed from the source tree,
/// so that indexing it cannot change what it answers.
///
/// <para>This is a guard, not a driver: the index is a behaviour-preserving refactor of a
/// linear scan, and what needs proving is that "behaviour-preserving" is true for every key
/// the fixture declares — not just the one a delta test happens to touch.</para>
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class RadWorkspaceFileOfTests(BcEngineFixture engine)
{
    private const string ScenarioDir = "al-runner-rad-fileof";

    /// <summary>
    /// For every object the fixture declares, <c>FileOf</c> names the file that actually
    /// declares it — where "actually" is read off the tree, not off the workspace.
    /// </summary>
    [SkippableFact]
    public void FileOf_NamesTheDeclaringFile_ForEveryObjectInTheBaseline()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            var workspace = baseline.Workspace;

            var declared = workspace.AllObjects();
            Assert.Equal(RadFixture.ObjectCount, declared.Count);

            // The oracle: read the tree, not the workspace. A file declares a key when the
            // objects the workspace recorded for that path contain it — established per path
            // through the public accessor, so the index under test is not its own witness.
            var files = Directory
                .EnumerateFiles(tempRoot, "*.al", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .ToList();
            Assert.NotEmpty(files);

            foreach (var obj in declared)
            {
                var declaring = files
                    .Where(file => workspace.ObjectsIn(file).Any(item => item.Key == obj.Key))
                    .ToList();
                Assert.Single(declaring);
                Assert.Equal(declaring[0], workspace.FileOf(obj.Key));
                Assert.True(File.Exists(workspace.FileOf(obj.Key)));
            }
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>A key the app never declared has no file — null, not an arbitrary one.</summary>
    [SkippableFact]
    public void FileOf_ReturnsNull_ForAKeyTheAppNeverDeclared()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var workspace = RadFixture.Seed(tempRoot).Workspace;

            // Absent by id, by kind, and by name-keyed identity — the three ways a key is built.
            // The ids are outside the fixture's 71000–71199 range on purpose: RadObjectKey.For
            // DISCARDS the name for an id-bearing kind, so ("XmlPort", 71000, "Absent") is the
            // XmlPort this fixture really declares, not an absent one.
            Assert.Null(workspace.FileOf(RadObjectKey.For("Codeunit", 999999, "Absent")));
            Assert.Null(workspace.FileOf(RadObjectKey.For("XmlPort", 799999, "Absent")));
            Assert.Null(workspace.FileOf(RadObjectKey.For("Interface", 0, "No Such Contract")));
            // A kind the fixture does declare, at an id it does not.
            Assert.Null(workspace.FileOf(RadObjectKey.For("Table", 799999, "Absent")));

            // …and a key that IS declared still resolves, so the null above is not "always null".
            var present = workspace.AllObjects()[0];
            Assert.NotNull(workspace.FileOf(present.Key));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The index tracks a delta: an object that moves to a different file resolves to the new
    /// one afterwards, and a deleted object stops resolving at all. A rebuild-on-commit answers
    /// this for free; an incrementally maintained map is where it would go wrong.
    /// </summary>
    [SkippableFact]
    public void FileOf_FollowsAnObjectAcrossACommit()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            var workspace = baseline.Workspace;

            var servicePath = RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al");
            var serviceKey = Assert.Single(workspace.ObjectsIn(servicePath)).Key;
            Assert.Equal(servicePath, workspace.FileOf(serviceKey));

            // Move the declaration to a new file, leaving the old one empty of objects.
            var source = File.ReadAllText(servicePath);
            var movedPath = RadFixture.SourceFile(tempRoot, "RadPerfServiceMoved.Codeunit.al");
            File.WriteAllText(movedPath, source);
            File.WriteAllText(servicePath, "// declaration moved to RadPerfServiceMoved.Codeunit.al\n");

            var delta = baseline.Cycle(tempRoot);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            delta.Commit(
                workspace,
                delta.Emit.Sources.Count > 0
                    ? RadFixture.AssembleAndLoad(workspace, delta.Emit.Sources)
                    : null);

            Assert.Equal(movedPath, workspace.FileOf(serviceKey));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
