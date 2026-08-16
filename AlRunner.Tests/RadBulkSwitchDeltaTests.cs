// RadBulkSwitchDeltaTests — the delta contract when a whole VERSION of the tree lands at
// once, not one edit at a time.
//
// The other RAD suites all move a single object, because that is the inner dev loop. But
// the change a developer actually makes most violently is a branch switch: one command
// rewrites, adds and deletes dozens of files before the runner gets a say. Everything that
// is only ever exercised one object at a time gets its first real test there —
// modifications and additions and deletions arriving in the SAME cycle, an enum gaining a
// value the same cycle a codeunit starts referencing it, and objects disappearing while
// their neighbours change.
//
// The fixture is two complete versions of one app (AlRunner.Tests/Fixtures/RadBulkSwitch),
// v1 and v2, differing in 12 of 15 files: 8 modified, 2 only in v1, 2 only in v2.
// "Switching" mirrors one version onto the working tree exactly as a checkout would.
//
// Every value the test codeunit asserts is produced by a DIFFERENT modified file, and the
// test codeunit is itself one of the modified files. That is deliberate: a tree that mixes
// v1 and v2 files cannot satisfy either version's assertions, so a half-applied switch is
// not merely slow — it is observable. That property is what
// RadBulkSwitchWatchTests leans on.

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadBulkSwitchDeltaTests(BcEngineFixture engine)
{
    private const string ScenarioDir = "al-runner-rad-bulk-switch";

    /// <summary>Objects each version declares — the "all of it" every delta is measured against.</summary>
    private const int ObjectCount = 13;

    private const string ModuleName = "RAD Bulk Switch Fixture";
    private const string Publisher = "AlRunner Tests";
    private static readonly Guid AppId = Guid.Parse("b41f9c22-6d3e-4a70-9c81-5f2a7e6d3b04");
    private static readonly Version AppVersion = new(1, 0, 0, 0);

    internal static readonly string FixtureRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "RadBulkSwitch"));

    /// <summary>The eight objects whose source differs between v1 and v2.</summary>
    private static readonly string[] Modified =
    [
        "Bulk Switch Header",
        "Bulk Switch Header Card",
        "Bulk Switch Helper A",
        "Bulk Switch Helper B",
        "Bulk Switch Line",
        "Bulk Switch Service",
        "Bulk Switch Status",
        "Bulk Switch Tests",
    ];

    /// <summary>
    /// CLR types the eight modified objects own. The enum is absent on purpose: BC emits
    /// enum members into their consumers rather than a top-level type, so
    /// <see cref="RadObjectKey.ClrTypeName"/> has nothing to reload for it — its delta is
    /// visible in the metadata registry instead.
    /// </summary>
    private static readonly string[] ModifiedTypes =
    [
        "Codeunit71203", "Codeunit71204", "Codeunit71205", "Codeunit71206",
        "Page71200", "Record71200", "Record71201",
    ];

    private static readonly string[] OnlyInV1 = ["Bulk Switch Only In V1 A", "Bulk Switch Only In V1 B"];
    private static readonly string[] OnlyInV2 = ["Bulk Switch Only In V2 A", "Bulk Switch Only In V2 B"];
    private static readonly string[] OnlyInV1Types = ["Codeunit71207", "Codeunit71208"];
    private static readonly string[] OnlyInV2Types = ["Codeunit71209", "Codeunit71210"];

    /// <summary>The three files a switch does NOT touch — the control group.</summary>
    private static readonly string[] UntouchedTypes = ["Codeunit71200", "Codeunit71201", "Codeunit71202"];

    /// <summary>
    /// Switching v1 → v2 must cost the twelve objects that differ, not the whole app, and
    /// must land all three kinds of change in ONE cycle: eight recompiled, two added, two
    /// gone. Doing any one of them correctly in isolation (which the other suites prove)
    /// says nothing about doing all three at once — additions and deletions share the
    /// baseline-merge step that a modification also rewrites.
    /// </summary>
    [SkippableFact]
    public void SwitchingToTheOtherVersion_RecompilesOnlyTheFilesThatDiffer()
    {
        RunSwitch("v1", "v2", expectedEmitted: [.. Modified, .. OnlyInV2],
            expectedRemoved: OnlyInV1,
            expectedReloadedTypes: [.. ModifiedTypes, .. OnlyInV2Types],
            expectedGoneTypes: OnlyInV1Types);
    }

    /// <summary>
    /// The reverse switch, which is the one a developer does when the branch turns out to
    /// be wrong. It is not symmetric with the forward case: here the two objects that
    /// COME BACK were tombstoned by the previous cycle, and the two that leave were added
    /// by it — so this exercises resurrection over a tombstone, a state the forward
    /// direction never reaches.
    /// </summary>
    [SkippableFact]
    public void SwitchingBack_RestoresTheOriginalVersion_JustAsProportionally()
    {
        RunSwitch("v2", "v1", expectedEmitted: [.. Modified, .. OnlyInV1],
            expectedRemoved: OnlyInV2,
            expectedReloadedTypes: [.. ModifiedTypes, .. OnlyInV1Types],
            expectedGoneTypes: OnlyInV2Types);
    }

    /// <summary>
    /// Switching out and straight back must return the tree to a state the workspace calls
    /// settled. A cycle that leaves residue — a tombstone that never cleared, a hash that
    /// never re-committed — shows up as a third cycle believing there is still work to do,
    /// which on a real app is a phantom recompile after every branch switch.
    /// </summary>
    [SkippableFact]
    public void SwitchingOutAndBack_LeavesTheWorkspaceSettled()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = CopyVersion("v1");
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(AppId, Publisher, AppVersion);
            var (workspace, compiler, _) = Seed(tempRoot);

            foreach (var version in new[] { "v2", "v1" })
            {
                Mirror(version, tempRoot);
                var cycle = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
                Assert.False(cycle.FullRebuild, $"switching to {version} rebuilt the whole module");
                Assert.True(cycle.Emit.Diagnostics.Count == 0,
                    string.Join(Environment.NewLine, cycle.Emit.Diagnostics));
                cycle.Commit(workspace, RadFixture.AssembleAndLoad(workspace, cycle.Emit.Sources));
            }

            var settled = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(settled.NoChange, "a round-trip switch left the workspace dirty");
            Assert.Empty(settled.Emit.Sources);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private void RunSwitch(
        string from, string to,
        string[] expectedEmitted,
        string[] expectedRemoved,
        string[] expectedReloadedTypes,
        string[] expectedGoneTypes)
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = CopyVersion(from);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(AppId, Publisher, AppVersion);
            var (workspace, compiler, seeded) = Seed(tempRoot);
            var baselineTypes = RadFixture.GeneratedObjectTypes(seeded)
                .ToDictionary(type => type.Name, StringComparer.Ordinal);

            Mirror(to, tempRoot);

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild,
                $"{from}->{to} rebuilt the whole module instead of the {expectedEmitted.Length} changed objects");
            Assert.False(delta.NoChange);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Empty(delta.Emit.ExcludedObjects);

            Assert.Equal(
                expectedEmitted.Order(StringComparer.Ordinal).ToArray(),
                RadFixture.EmittedNames(delta));
            Assert.Equal(
                expectedRemoved.Order(StringComparer.Ordinal).ToArray(),
                delta.Changes.Removed.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.True(delta.Emit.Sources.Count < ObjectCount,
                $"{from}->{to} re-emitted the complete app ({delta.Emit.Sources.Count} objects)");

            var overlay = RadFixture.AssembleAndLoad(workspace, delta.Emit.Sources);
            delta.Commit(workspace, overlay);

            Assert.Equal(
                expectedReloadedTypes.Order(StringComparer.Ordinal).ToArray(),
                RadFixture.ReloadedTypeNames(overlay));

            // The switched-in objects resolve out of the new overlay …
            foreach (var name in expectedReloadedTypes)
                Assert.Same(overlay, AlObjectResolution.FindOwned(name, requiredBase: null)?.Assembly);
            // … the switched-out ones resolve to nothing at all …
            foreach (var name in expectedGoneTypes)
                Assert.Null(AlObjectResolution.FindOwned(name, requiredBase: null));
            // … and the three files the switch never touched still resolve to the
            // baseline's own type instances, not to fresh copies of themselves.
            foreach (var name in UntouchedTypes)
            {
                var current = AlObjectResolution.FindOwned(name, requiredBase: null);
                Assert.NotNull(current);
                Assert.Same(baselineTypes[name], current);
                Assert.NotSame(overlay, current!.Assembly);
            }

            var settled = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(settled.NoChange);
            Assert.Empty(settled.Emit.Sources);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>One full compile of the starting version, committed as the RAD baseline.</summary>
    private static (RadWorkspace Workspace, BcCompiler Compiler, System.Reflection.Assembly Seeded)
        Seed(string tempRoot)
    {
        var workspace = new RadWorkspace(ModuleName, tempRoot);
        var compiler = new BcCompiler();
        var result = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
        Assert.True(result.FullRebuild);
        Assert.True(result.Emit.Diagnostics.Count == 0,
            string.Join(Environment.NewLine, result.Emit.Diagnostics));
        Assert.Empty(result.Emit.ExcludedObjects);
        Assert.Equal(ObjectCount, result.Emit.Sources.Count);

        var seeded = RadFixture.AssembleAndLoad(workspace, result.Emit.Sources);
        result.Commit(workspace, seeded);
        Assert.True(workspace.HasBaseline);
        return (workspace, compiler, seeded);
    }

    /// <summary>Copy one version of the fixture into a private temp root the test may edit.</summary>
    private static string CopyVersion(string version)
    {
        var destination = Path.Combine(
            Path.GetTempPath(), ScenarioDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        Mirror(version, destination);
        return destination;
    }

    /// <summary>
    /// Make <paramref name="destination"/> hold exactly what <paramref name="version"/>
    /// holds — every file written, every file the version does not have removed. This is
    /// what `git checkout` does to a working tree, minus the ordering guarantees.
    /// </summary>
    internal static void Mirror(string version, string destination)
    {
        var source = Path.Combine(FixtureRoot, version);
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            wanted.Add(relative);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        foreach (var file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
            if (!wanted.Contains(Path.GetRelativePath(destination, file)))
                File.Delete(file);
    }
}
