// RadIdlessObjectTests — the delta path's identity assumption, tested against the AL
// object kinds that break it.
//
// RadObjectKey is (Kind, Id). The design already knows some application objects have no
// id and falls back to a full compile for the files that declare them — but the test for
// that used a controladdin, and a controladdin is not the dangerous shape. A `profile`
// IS an ISymbolWithId: it satisfies every "does this have an id?" check and then reports
// id 0, so every profile in an app keys as `Profile:0`. An app with two of them therefore
// produced two objects with one key, which threw out of the baseline snapshot and left the
// app with no baseline at all — silently, since that failure is caught and logged. Measured
// on NP Retail (7 profiles): every watch cycle was a full compile and no delta ever ran.
//
// So the claim here is not "profiles are supported". It is: an app that declares them still
// gets a working baseline, its ordinary objects still delta, and touching a profile takes
// the documented full-compile path.

using System.Reflection;
using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadIdlessObjectTests(BcEngineFixture engine)
{
    private const string ModuleName = "RAD Profile Fixture";
    private static readonly Guid AppId = Guid.Parse("5a1d0f27-7c64-4b53-9f2e-3d8b6c41a907");
    private static readonly Version AppVersion = new(1, 0, 0, 0);

    /// <summary>Page, codeunit and two profiles — four declared objects, two of them id-less.</summary>
    private const int EmittedObjectCount = 2;

    private static readonly string Source = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "RadProfileApp"));

    [Fact]
    public void TwoProfiles_StillProduceABaseline_AndOrdinaryObjectsStillDelta()
    {
        if (!engine.Ready)
        {
            Console.Error.WriteLine($"[skip] {engine.SkipReason}");
            return;
        }

        var tempRoot = Copy();
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(AppId, "AlRunner Tests", AppVersion);
            var workspace = new RadWorkspace(ModuleName, tempRoot);
            var compiler = new BcCompiler();

            var seed = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(seed.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, seed.Emit.Diagnostics));
            Assert.True(seed.FullRebuild);
            // The profiles emit no code of their own; the page and the codeunit do.
            Assert.Equal(EmittedObjectCount, seed.Emit.Sources.Count);
            // The point of the test: a duplicate key must not cost the app its baseline.
            Assert.True(seed.CanCommit,
                "the first compile produced no committable baseline — the snapshot threw");

            seed.Commit(workspace, Load(workspace, seed.Emit.Sources));
            Assert.True(workspace.HasBaseline);

            // With a baseline, an ordinary edit is an ordinary delta: one object, not four.
            Replace(Path.Combine(tempRoot, "src", "RadProfileService.Codeunit.al"),
                "exit(140);", "exit(141);");
            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal(["RAD Profile Service"],
                delta.Emit.Sources.Select(source => source.Name).ToArray());
            delta.Commit(workspace, Load(workspace, delta.Emit.Sources));

            // Negative direction: the profiles themselves are NOT tracked, so editing one
            // must take the documented full-compile path rather than a partial delta whose
            // change model cannot name the object that moved.
            Replace(Path.Combine(tempRoot, "src", "RadProfileB.Profile.al"),
                "Enabled = true;", "Enabled = false;");
            var fallback = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(fallback.FullRebuild,
                "editing an id-less object must force a full compile, not a partial delta");
            Assert.True(fallback.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, fallback.Emit.Diagnostics));
            Assert.Equal(EmittedObjectCount, fallback.Emit.Sources.Count);
            Assert.True(fallback.CanCommit, "the fallback compile produced no baseline either");
            fallback.Commit(workspace, Load(workspace, fallback.Emit.Sources));

            var settled = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(settled.NoChange);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string Copy()
    {
        var destination = Path.Combine(
            Path.GetTempPath(), "al-runner-rad-profile", Guid.NewGuid().ToString("N"));
        foreach (var source in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(Source, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        return destination;
    }

    private static void Replace(string path, string before, string after)
    {
        var source = File.ReadAllText(path);
        Assert.Equal(1, source.Split(before, StringSplitOptions.None).Length - 1);
        File.WriteAllText(path, source.Replace(before, after, StringComparison.Ordinal));
    }

    private static Assembly Load(RadWorkspace workspace, IReadOnlyList<EmittedSource> sources)
    {
        var compiled = new BcAssembler().Compile(workspace.NextAssemblyName(), sources);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Errors));
        return Assembly.Load(compiled.AssemblyBytes!);
    }
}
