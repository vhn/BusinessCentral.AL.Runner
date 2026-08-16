using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// Shared harness for the by-name reference shapes (plan tasks 9–15).
///
/// <para><b>Why every fixture here is THREE objects.</b> The damage needs a triple:
/// <b>X</b> is stripped from the packaged baseline ∧ <b>V</b> is untouched, so its serialized
/// surface — which names X — is what the delta binds against ∧ <b>W</b> is in the same delta
/// and binds to the part of V's surface that names X. A two-object fixture may never query the
/// bystander's damaged representation, so it goes green while proving nothing. If a new shape
/// is added here, it gets three objects or it is not evidence.</para>
///
/// <para><b>The oracle is a cold compile of the identical tree.</b> Not "some diagnostic", and
/// not a hand-written expected list: a delta must accept and reject exactly what a full compile
/// of the same source accepts and rejects. Hand-written expectations encode the author's belief
/// about BC, which is the failure mode <c>.claude/rules/bc-behavior-tests-go-upstream.md</c>
/// exists to prevent.</para>
///
/// <para>These are METHOD-BODY diagnostics. <c>BcCompiler.DeltaCompile</c> asks only
/// <c>GetDeclarationDiagnostics()</c> before codegen, so every shape here reaches the runner
/// through <c>rad.Emit(...)</c> instead.</para>
/// </summary>
internal static class RadByName
{
    internal static readonly string Publisher = "AlRunner Tests";
    internal static readonly Version AppVersion = new(1, 0, 0, 0);

    private static string FixtureRoot(string fixtureName) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", fixtureName));

    /// <summary>
    /// Copy <paramref name="fixtureName"/> to a private temp tree, seed a committed baseline
    /// over it, and hand both to <paramref name="scenario"/>.
    ///
    /// <para>The identity comes from the fixture's own app.json so that each shape's fixture is
    /// a real app rather than a borrowed one — two fixtures sharing an AppId would share a
    /// <c>RadWorkspaceStore</c> entry if one is ever driven through the store.</para>
    /// </summary>
    internal static void Run(
        string fixtureName,
        string moduleName,
        Guid appId,
        int expectedObjectCount,
        Action<BcCompiler, RadWorkspace, string> scenario)
    {
        var source = FixtureRoot(fixtureName);
        Assert.True(Directory.Exists(source), $"fixture not found: {source}");

        var tempRoot = Path.Combine(
            Path.GetTempPath(), "al-runner-rad-byname", Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(tempRoot, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(appId, Publisher, AppVersion);
            var workspace = new RadWorkspace(moduleName, tempRoot);
            var compiler = new BcCompiler();

            var seed = compiler.EmitIncremental([tempRoot], moduleName, workspace);
            Assert.True(seed.Emit.Diagnostics.Count == 0,
                "the fixture does not compile clean:" + Environment.NewLine
                + string.Join(Environment.NewLine, seed.Emit.Diagnostics));
            Assert.True(seed.FullRebuild);
            Assert.Equal(expectedObjectCount, seed.Emit.Sources.Count);
            Assert.True(seed.CanCommit,
                "the first compile produced no committable baseline — the snapshot threw");
            seed.Commit(workspace, Load(workspace, seed.Emit.Sources));
            Assert.True(workspace.HasBaseline);

            scenario(compiler, workspace, tempRoot);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Compile <paramref name="tempRoot"/> from scratch into a workspace with no baseline —
    /// same tree, same compiler, no delta path.
    /// </summary>
    internal static RadEmitResult ColdCompile(string tempRoot, string moduleName) =>
        new BcCompiler().EmitIncremental(
            [tempRoot], moduleName, new RadWorkspace(moduleName, tempRoot));

    /// <summary>
    /// Diagnostics reduced to their sorted AL ids, after collapsing byte-identical repeats.
    /// The repeats are the full-compile path's, not information; distinct locations survive the
    /// collapse, so "cold found this break in four places and the delta found it in one" still
    /// fails.
    /// </summary>
    internal static string[] DiagnosticCodes(IEnumerable<string> diagnostics) => diagnostics
        .Distinct(StringComparer.Ordinal)
        .Select(text => System.Text.RegularExpressions.Regex.Match(text, @"\bAL\d{4}\b"))
        .Select(match => match.Success ? match.Value : "no-al-code")
        .Order(StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Assert the delta says exactly what a cold compile of the same tree says — and, when the
    /// tree is expected to be broken, that it says something at all.
    /// </summary>
    internal static void AssertMatchesColdCompile(
        RadEmitResult delta, string tempRoot, string moduleName)
    {
        var cold = ColdCompile(tempRoot, moduleName);
        var expected = DiagnosticCodes(cold.Emit.Diagnostics);
        var actual = DiagnosticCodes(delta.Emit.Diagnostics);
        if (expected.SequenceEqual(actual, StringComparer.Ordinal)) return;

        // The codes alone say a shape broke; they never say WHICH reference broke, and that
        // is the whole question when several by-name references are in play. Fail with both
        // sides' full text.
        Assert.Fail(
            $"the delta did not report what a cold compile of the same tree reports.{Environment.NewLine}"
            + $"cold  [{string.Join(", ", expected)}]:{Environment.NewLine}"
            + Indent(cold.Emit.Diagnostics) + Environment.NewLine
            + $"delta [{string.Join(", ", actual)}]:{Environment.NewLine}"
            + Indent(delta.Emit.Diagnostics));

        static string Indent(IEnumerable<string> diagnostics)
        {
            var lines = diagnostics.Distinct(StringComparer.Ordinal).ToArray();
            return lines.Length == 0
                ? "    (none)"
                : string.Join(Environment.NewLine, lines.Select(line => "    " + line));
        }
    }

    internal static void Replace(string path, string before, string after)
    {
        var source = File.ReadAllText(path);
        Assert.Equal(1, source.Split(before, StringSplitOptions.None).Length - 1);
        File.WriteAllText(path, source.Replace(before, after, StringComparison.Ordinal));
    }

    internal static string SourceFile(string tempRoot, string fileName) =>
        Path.Combine(tempRoot, "src", fileName);

    private static System.Reflection.Assembly Load(
        RadWorkspace workspace, IReadOnlyList<EmittedSource> sources)
    {
        var compiled = new BcAssembler().Compile(workspace.NextAssemblyName(), sources);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Errors));
        return System.Reflection.Assembly.Load(compiled.AssemblyBytes!);
    }
}
