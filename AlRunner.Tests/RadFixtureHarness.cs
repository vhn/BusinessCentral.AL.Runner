// RadFixtureHarness — the shared seam every RAD proportionality suite compiles against.
//
// The performance claim `--watch --rad` makes is not "it felt fast": it is that ONE edit
// costs ONE object of compiler work and replaces ONE runtime object. That claim is only
// falsifiable against a baseline big enough for "all of it" and "the changed one" to be
// different numbers, so all of these suites share one real 20-object AL app
// (AlRunner.Tests/Fixtures/RadTwentyObject) and assert exact identities — never wall-clock
// times, which would be flaky on CI and would not say WHICH objects were rebuilt.
//
// The three suites that use it:
//   RadObjectDeltaTests    — one edit → which objects re-emit and which CLR types reload
//   RadDeletionDeltaTests  — one deletion → which objects vanish, which tombstone
//   RadMetadataDeltaTests  — one edit → which runtime metadata entries move
//
// Fixtures are read-only: every scenario copies the app into a temp dir and mutates only
// the copy, so a failed assertion can never leave the checked-in fixture edited.

using System.Reflection;
using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The 20-object fixture app and the primitives for driving one RAD cycle over it.
/// </summary>
internal static class RadFixture
{
    /// <summary>
    /// Objects the fixture declares. Every proportionality assertion is ultimately
    /// "fewer than this", so it is a constant rather than a computed count.
    /// </summary>
    internal const int ObjectCount = 20;
    internal const string ModuleName = "RAD Twenty Object Fixture";
    internal const string Publisher = "AlRunner Tests";
    internal static readonly Guid AppId = Guid.Parse("e23cd601-abba-46f2-8d5e-d1ca75615f9e");
    internal static readonly Version AppVersion = new(1, 0, 0, 0);

    internal static readonly string Source = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "RadTwentyObject"));

    /// <summary>Copy the fixture to a private temp root the caller may edit and delete.</summary>
    internal static string Copy(string scenarioDir)
    {
        var destination = Path.Combine(
            Path.GetTempPath(), scenarioDir, Guid.NewGuid().ToString("N"));
        foreach (var source in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(Source, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        return destination;
    }

    /// <summary>
    /// One full compile of the fixture, committed as the RAD baseline — the state a
    /// `--watch --rad` process reaches at the end of its first cycle. Every later
    /// assertion is relative to this.
    /// </summary>
    internal static SeededBaseline Seed(string tempRoot)
    {
        var workspace = new RadWorkspace(ModuleName, tempRoot);
        var compiler = new BcCompiler();
        var result = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
        Assert.True(result.FullRebuild);
        Assert.False(result.NoChange);
        Assert.True(result.Emit.Diagnostics.Count == 0,
            string.Join(Environment.NewLine, result.Emit.Diagnostics));
        Assert.Empty(result.Emit.ExcludedObjects);
        Assert.Equal(ObjectCount, result.Emit.Sources.Count);
        Assert.Equal(ObjectCount, result.Emit.Sources.Select(source => source.Name).Distinct().Count());

        var assembly = AssembleAndLoad(workspace, result.Emit.Sources);
        result.Commit(workspace, assembly);
        Assert.True(workspace.HasBaseline);
        Assert.Single(workspace.Generations);

        var types = GeneratedObjectTypes(assembly)
            .ToDictionary(type => type.Name, StringComparer.Ordinal);
        return new SeededBaseline(workspace, compiler, types, MetadataSnapshot.Take());
    }

    /// <summary>Roslyn-compile and load one generation's worth of emitted C#.</summary>
    internal static Assembly AssembleAndLoad(
        RadWorkspace workspace, IReadOnlyList<EmittedSource> sources)
    {
        var compiled = TryAssemble(workspace, sources);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Errors));
        return Assembly.Load(compiled.AssemblyBytes!);
    }

    /// <summary>
    /// Same compile, without asserting success — for the scenarios whose whole point is a
    /// generation the C# backend rejects.
    /// </summary>
    internal static CompileResult TryAssemble(
        RadWorkspace workspace, IReadOnlyList<EmittedSource> sources) =>
        new BcAssembler().Compile(workspace.NextAssemblyName(), sources);

    /// <summary>
    /// The AL object types in one generated assembly: BC names them
    /// <c>&lt;Kind&gt;&lt;id&gt;</c> at namespace top level, so this is exactly the set
    /// whose runtime ownership a reload moves.
    /// </summary>
    internal static IReadOnlyList<Type> GeneratedObjectTypes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(type => type.DeclaringType == null)
            .Where(type => type.Namespace == "Microsoft.Dynamics.Nav.BusinessApplication")
            .Where(type => IsObjectTypeName(type.Name))
            .ToArray();

    private static bool IsObjectTypeName(string name)
    {
        var firstDigit = 0;
        while (firstDigit < name.Length && !char.IsAsciiDigit(name[firstDigit])) firstDigit++;
        if (firstDigit == 0 || firstDigit == name.Length) return false;
        return name[firstDigit..].All(char.IsAsciiDigit);
    }

    /// <summary>Emitted AL object names, ordered — the compiler-side delta.</summary>
    internal static string[] EmittedNames(RadEmitResult result) =>
        result.Emit.Sources.Select(source => source.Name).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Reloaded CLR type names, ordered — the runtime-side delta.</summary>
    internal static string[] ReloadedTypeNames(Assembly assembly) =>
        GeneratedObjectTypes(assembly).Select(type => type.Name).Order(StringComparer.Ordinal).ToArray();

    /// <summary>`Kind:Id` strings, so xUnit can serialize theory expectations verbatim.</summary>
    internal static string[] KeyStrings(IEnumerable<RadObjectRef> objects) =>
        objects.Select(item => $"{item.Key.Kind}:{item.Key.Id}").Order(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Replace a sentinel that must appear exactly once. An edit that silently matched
    /// nothing would leave the source identical and the test would then be asserting
    /// against a cycle that had nothing to do.
    /// </summary>
    internal static void ReplaceExactlyOnce(string path, string before, string after)
    {
        var source = File.ReadAllText(path);
        Assert.Equal(1, source.Split(before, StringSplitOptions.None).Length - 1);
        File.WriteAllText(path, source.Replace(before, after, StringComparison.Ordinal));
    }

    internal static string SourceFile(string tempRoot, string fileName) =>
        Path.Combine(tempRoot, "src", fileName);
}

/// <summary>The committed first-cycle state a warm RAD edit starts from.</summary>
internal sealed record SeededBaseline(
    RadWorkspace Workspace,
    BcCompiler Compiler,
    IReadOnlyDictionary<string, Type> Types,
    MetadataSnapshot Metadata)
{
    /// <summary>Run one warm cycle: re-diff the tree and emit whatever changed.</summary>
    internal RadEmitResult Cycle(string tempRoot) =>
        Compiler.EmitIncremental([tempRoot], RadFixture.ModuleName, Workspace);

    /// <summary>
    /// Assert the tree is fully settled: a committed cycle must leave no residue that
    /// makes the NEXT cycle believe something still changed.
    /// </summary>
    internal void AssertSettled(string tempRoot)
    {
        var settled = Cycle(tempRoot);
        Assert.True(settled.NoChange);
        Assert.Empty(settled.Emit.Sources);
    }

    /// <summary>
    /// Assert exactly <paramref name="moved"/> now resolve out of <paramref name="owner"/>
    /// and every other baseline object still resolves to the identical baseline
    /// <see cref="Type"/> instance — the runtime half of "only what changed reloaded".
    /// A null <paramref name="owner"/> expects the moved names to resolve to nothing,
    /// which is what a committed deletion leaves behind.
    /// </summary>
    internal void AssertOwnership(Assembly? owner, IReadOnlyCollection<string> moved)
    {
        var replaced = moved.ToHashSet(StringComparer.Ordinal);
        foreach (var name in replaced)
            Assert.Same(owner, AlObjectResolution.FindOwned(name, requiredBase: null)?.Assembly);
        foreach (var (name, baselineType) in Types)
        {
            if (replaced.Contains(name)) continue;
            var current = AlObjectResolution.FindOwned(name, requiredBase: null);
            Assert.NotNull(current);
            Assert.Same(baselineType, current);
        }
    }
}

/// <summary>
/// The runtime metadata registries the AL emitter writes into, snapshotted by id.
///
/// These are the OTHER half of a reload: BC resolves a page/report/xmlport/enum through
/// this metadata, not through the CLR type alone. A delta that replaces the right type but
/// leaves stale metadata behind — or refreshes all 20 entries to replace one — breaks the
/// same proportionality claim, so every metadata suite diffs two of these.
/// </summary>
internal sealed record MetadataSnapshot(
    IReadOnlyDictionary<int, string> Pages,
    IReadOnlyDictionary<int, string> Reports,
    IReadOnlyDictionary<int, string> XmlPorts,
    IReadOnlyDictionary<int, string> Enums)
{
    internal static MetadataSnapshot Take() => new(
        Own(AlPageMetadataRegistry.Ids).ToDictionary(id => id, id =>
            AlPageMetadataRegistry.TryGet(id, out var xml) ? xml : string.Empty),
        Own(AlReportMetadataRegistry.Ids).ToDictionary(id => id, id =>
            AlReportMetadataRegistry.TryGet(id, out var xml) ? xml : string.Empty),
        Own(AlXmlPortMetadataRegistry.Ids).ToDictionary(id => id, id =>
            AlXmlPortMetadataRegistry.TryGet(id, out var xml) ? xml : string.Empty),
        Own(AlEnumMetadataRegistry.Ids).ToDictionary(id => id, RenderEnum));

    /// <summary>
    /// The `Kind:Id` entries that differ between the two snapshots in either direction —
    /// added, removed or changed. This is the metadata delta a cycle actually performed.
    /// </summary>
    internal static string[] Diff(MetadataSnapshot before, MetadataSnapshot after) =>
        [
            .. DiffOne("Page", before.Pages, after.Pages),
            .. DiffOne("Report", before.Reports, after.Reports),
            .. DiffOne("XmlPort", before.XmlPorts, after.XmlPorts),
            .. DiffOne("Enum", before.Enums, after.Enums),
        ];

    private static IEnumerable<string> DiffOne(
        string kind,
        IReadOnlyDictionary<int, string> before,
        IReadOnlyDictionary<int, string> after) =>
        before.Keys.Concat(after.Keys).Distinct().Order()
            .Where(id =>
            {
                var had = before.TryGetValue(id, out var was);
                var has = after.TryGetValue(id, out var now);
                return had != has || !string.Equals(was, now, StringComparison.Ordinal);
            })
            .Select(id => $"{kind}:{id}");

    // Scoped to the fixture's own id range: these registries are process-wide, so every
    // other suite that ran first in this test host has entries in them too, and a diff
    // over all of them would be neither stable nor about this fixture.
    private static IEnumerable<int> Own(IEnumerable<int> ids) =>
        ids.Where(id => id is >= 71000 and <= 71199);

    private static string RenderEnum(int id)
    {
        if (!AlEnumMetadataRegistry.TryGet(id, out var entry)) return string.Empty;
        return string.Join(",", entry.Indexes.Zip(entry.Options, (ordinal, name) => $"{ordinal}={name}"));
    }
}
