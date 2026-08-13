// RadIdlessObjectTests — the delta path against the AL object kinds that have no object id.
//
// `RadObjectKey` was `(Kind, Id)`, and four AL kinds do not fit that: `interface`,
// `controladdin`, `profile` and `pagecustomization` have no id at all. They broke it in two
// different ways, and only one of them was visible.
//
//   * A `profile` IS an `ISymbolWithId` — it satisfies every "does this have an id?" check
//     and then reports id 0, so every profile in an app keys as `Profile:0`. An app with two
//     of them produced two objects with one key, which threw out of the baseline snapshot
//     and left the app with no baseline at all. Silently: that failure is caught and logged.
//   * An `interface` or `controladdin` is not returned by
//     `GetDeclaredApplicationObjectSymbols()` at all, so the workspace never recorded which
//     file declared it. Its file therefore looked untracked for the life of the process, and
//     every edit to it — including a comment — took the full-compile path.
//
// Measured on NP Retail, that was 84 of 7,339 files (60 interface, 16 controladdin, 8
// profile), each a guaranteed whole-module rebuild on any edit.
//
// The key now carries a Name, used as the discriminator exactly when there is no id, and
// id-less declarations the symbol API omits are read off the syntax tree instead. So the
// claim this suite makes is the same one the rest of the RAD suites make for id-bearing
// objects: one edit costs one object.

using System.Reflection;
using AlRunner.Rad;
using Xunit;
using NavSymRef = Microsoft.Dynamics.Nav.CodeAnalysis.SymbolReference;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadIdlessObjectTests(BcEngineFixture engine)
{
    private const string ModuleName = "RAD Profile Fixture";
    private static readonly Guid AppId = Guid.Parse("5a1d0f27-7c64-4b53-9f2e-3d8b6c41a907");
    private static readonly Version AppVersion = new(1, 0, 0, 0);

    /// <summary>
    /// The fixture declares a page, three codeunits, two profiles, two controladdins and
    /// two interfaces. Only the page and the codeunits generate code — the id-less kinds
    /// contribute symbols and metadata, never a C# source — which is exactly why an id-less
    /// delta emits nothing at all.
    /// </summary>
    private const int EmittedObjectCount = 4;

    private static readonly string Source = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "RadProfileApp"));

    /// <summary>
    /// Two profiles must be two objects. This is the case that used to cost the app its
    /// baseline outright, so it is asserted before anything else: without a baseline there
    /// is no delta path to test.
    /// </summary>
    [Fact]
    public void TwoProfiles_AreTwoDistinctObjects_NotOneCollidingKey()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            var profiles = workspace.ObjectsIn(Path.Combine(tempRoot, "src", "RadProfileA.Profile.al"))
                .Concat(workspace.ObjectsIn(Path.Combine(tempRoot, "src", "RadProfileB.Profile.al")))
                .ToList();

            Assert.Equal(
                ["Profile:0:RAD Profile A", "Profile:0:RAD Profile B"],
                profiles.Select(Describe).Order(StringComparer.Ordinal).ToArray());
        });
    }

    /// <summary>
    /// The kinds the symbol API never reports. If the workspace does not know which file
    /// declares them, their files stay untracked forever and every edit is a full compile —
    /// which is what this asserted the opposite of before.
    /// </summary>
    [Theory]
    [InlineData("RadIdlessContract.Interface.al", "Interface:0:RAD Idless Contract")]
    [InlineData("RadIdlessAddin.ControlAddin.al", "ControlAddIn:0:RAD Idless Addin")]
    [InlineData("RadIdlessAddinB.ControlAddin.al", "ControlAddIn:0:RAD Idless Addin B")]
    public void ObjectsTheSymbolApiOmits_AreStillTrackedToTheirFile(string file, string expected)
    {
        Run((compiler, workspace, tempRoot) =>
            Assert.Equal(
                [expected],
                workspace.ObjectsIn(Path.Combine(tempRoot, "src", file))
                    .Select(Describe).ToArray()));
    }

    /// <summary>
    /// Editing an id-less object is a delta like any other. It emits no C# — there is none
    /// to emit — so the observable delta is the change set and the fact that the cycle did
    /// not rebuild the module.
    /// </summary>
    [Theory]
    [InlineData("RadProfileB.Profile.al", "Enabled = true;", "Enabled = false;", "Profile:0:RAD Profile B")]
    [InlineData("RadIdlessAddin.ControlAddin.al", "'idless-addin.js'", "'idless-addin-2.js'", "ControlAddIn:0:RAD Idless Addin")]
    public void EditingAnIdLessObject_IsADelta_NotAFullCompile(
        string file, string before, string after, string expected)
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", file), before, after);

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild,
                $"editing {file} rebuilt the whole module instead of deltaing one id-less object");
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal([expected], delta.Changes.Modified.Select(Describe).ToArray());
            Assert.Empty(delta.Changes.Added);
            Assert.Empty(delta.Changes.Removed);
            // An id-less object owns no generated type, so a correct delta compiles no C# at
            // all. Emitting something here would mean an unrelated object was dragged in.
            Assert.Empty(delta.Emit.Sources);

            delta.Commit(workspace, null);
            Assert.True(compiler.EmitIncremental([tempRoot], ModuleName, workspace).NoChange);
        });
    }

    /// <summary>
    /// The interface case has a consumer, which is what makes it more than bookkeeping:
    /// widening the contract and its implementer in one cycle must rebind and re-emit the
    /// implementer, and nothing else. If the interface were left in the packaged baseline
    /// its old shape would shadow the edit and the implementer would fail to satisfy it.
    /// </summary>
    [Fact]
    public void WideningAnInterface_ReEmitsItsImplementer_AndNothingElse()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadIdlessContract.Interface.al"),
                "    procedure Answer(): Integer;",
                "    procedure Answer(): Integer;\n    procedure Second(): Integer;");
            Replace(Path.Combine(tempRoot, "src", "RadIdlessImpl.Codeunit.al"),
                "        exit(42);\n    end;",
                "        exit(42);\n    end;\n\n    procedure Second(): Integer\n    begin\n        exit(43);\n    end;");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal(
                ["Codeunit:71402:RAD Idless Impl", "Interface:0:RAD Idless Contract"],
                delta.Changes.Modified.Select(Describe).Order(StringComparer.Ordinal).ToArray());
            // Only the implementer generates code; the interface contributes symbols.
            Assert.Equal(["RAD Idless Impl"], RadFixture.EmittedNames(delta));

            delta.Commit(workspace, RadFixture.AssembleAndLoad(workspace, delta.Emit.Sources));
            Assert.True(compiler.EmitIncremental([tempRoot], ModuleName, workspace).NoChange);
        });
    }

    /// <summary>
    /// The sharp version of the interface case, and the one that says whether the modified
    /// object was really stripped from the packaged baseline before `CreateForRad` bound the
    /// new source. NARROWING the contract — renaming its only method, and the implementer's
    /// with it — is only legal against the NEW interface. If the stale packaged definition
    /// still shadows it, the implementer no longer satisfies `Answer` and the cycle fails
    /// with an AL diagnostic. Widening cannot detect this: implementing a method the old
    /// contract did not ask for is not an error.
    /// </summary>
    [Fact]
    public void RenamingAnInterfaceMethod_BindsAgainstTheNewContract_NotTheBaselineCopy()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadIdlessContract.Interface.al"),
                "procedure Answer(): Integer;", "procedure Renamed(): Integer;");
            Replace(Path.Combine(tempRoot, "src", "RadIdlessImpl.Codeunit.al"),
                "procedure Answer(): Integer", "procedure Renamed(): Integer");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                "the delta bound the implementer against the pre-edit interface still in the " +
                "packaged baseline:" + Environment.NewLine +
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(["RAD Idless Impl"], RadFixture.EmittedNames(delta));

            delta.Commit(workspace, RadFixture.AssembleAndLoad(workspace, delta.Emit.Sources));

            // And the NEXT delta must bind against the renamed contract too — proving the
            // merged baseline carries the new shape rather than both shapes.
            Replace(Path.Combine(tempRoot, "src", "RadIdlessImpl.Codeunit.al"),
                "exit(42);", "exit(44);");
            var second = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(second.Emit.Diagnostics.Count == 0,
                "the merged baseline did not carry the renamed interface:" + Environment.NewLine +
                string.Join(Environment.NewLine, second.Emit.Diagnostics));
            Assert.False(second.FullRebuild);
            Assert.Equal(["RAD Idless Impl"], RadFixture.EmittedNames(second));
        });
    }

    /// <summary>
    /// The case both interface tests above are blind to: widening the contract and NOT
    /// touching the implementer. An interface is a binding contract, so its users have to be
    /// rebound when it moves — and they cannot be, unless the dependency graph records an
    /// edge onto an object that is not an application object. Without that edge the delta
    /// reported success, emitted nothing, and left the implementer bound to a contract it no
    /// longer satisfies. The correct answer is the compiler's: AL0582.
    /// </summary>
    [Fact]
    public void WideningAnInterfaceAlone_RebindsItsImplementer_AndReportsTheBreak()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadIdlessContract.Interface.al"),
                "    procedure Answer(): Integer;",
                "    procedure Answer(): Integer;\n    procedure Second(): Integer;");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.Contains("AL0582", string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Empty(delta.Emit.Sources);
        });
    }

    /// <summary>
    /// The same claim against an identifier that contains a quote. AL escapes one by doubling
    /// it, and the compiler reports the decoded value — so a syntax reader that strips only
    /// the outer delimiters produces `RAD ""Quoted"" Contract` where the module definition
    /// says `RAD "Quoted" Contract`. Two keys for one object, and the delta then fails to
    /// strip its own baseline copy: the narrowed contract binds against the stale one and
    /// AL0582 comes back. Nothing else in the suite would notice, because every other name
    /// survives naive unquoting unchanged.
    /// </summary>
    [Fact]
    public void RenamingAMethodOnAQuotedInterface_BindsAgainstTheNewContract()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadIdlessQuoted.Interface.al"),
                "procedure Answer(): Integer;", "procedure Renamed(): Integer;");
            Replace(Path.Combine(tempRoot, "src", "RadIdlessQuotedImpl.Codeunit.al"),
                "procedure Answer(): Integer", "procedure Renamed(): Integer");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                "the quoted interface was keyed differently by the syntax reader and the " +
                "module definition, so its baseline copy was never stripped:" +
                Environment.NewLine + string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(["RAD Idless Quoted Impl"], RadFixture.EmittedNames(delta));
        });
    }

    /// <summary>
    /// AL identifiers are case-insensitive, so a case-only rename is the SAME object. Keyed
    /// on the exact spelling it read as one addition plus one removal of an object that
    /// never went anywhere.
    /// </summary>
    [Fact]
    public void RenamingAnIdLessObjectsCaseOnly_IsAModification_NotAnAddAndRemove()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadIdlessAddinB.ControlAddin.al"),
                @"controladdin ""RAD Idless Addin B""", @"controladdin ""RAD IDLESS ADDIN B""");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(["ControlAddIn:0:RAD IDLESS ADDIN B"],
                delta.Changes.Modified.Select(Describe).ToArray());
            Assert.Empty(delta.Changes.Added);
            Assert.Empty(delta.Changes.Removed);
        });
    }

    /// <summary>
    /// Deleting an id-less object has to leave the baseline, or the next compile still
    /// resolves a `controladdin` whose declaration is gone — the exact failure the old
    /// blanket full-compile fallback existed to avoid. Nothing references this one, so the
    /// delta is a pure removal.
    /// </summary>
    [Fact]
    public void DeletingAnIdLessObject_RemovesItFromTheBaseline()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            File.Delete(Path.Combine(tempRoot, "src", "RadIdlessAddinB.ControlAddin.al"));

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal(["ControlAddIn:0:RAD Idless Addin B"],
                delta.Changes.Removed.Select(Describe).ToArray());
            Assert.Empty(delta.Changes.Modified);
            Assert.Empty(delta.Emit.Sources);

            delta.Commit(workspace, null);

            // The change list saying "removed" is not the same as it being gone. Microsoft's
            // symbol writer drops a removed object from the previous module by matching the
            // change element, and a serialized id-less element carries a synthesized id that
            // the element built from source cannot reproduce — so the deleted add-in survived
            // the merge and the next delta still resolved it, while every assertion above
            // still passed. Read the merged baseline itself.
            var baseline = (NavSymRef.ModuleDefinition)workspace.Baseline!;
            Assert.Null(ModuleDefinitionOps.ObjectSurfaceFingerprint(
                baseline, new RadObjectKey("ControlAddIn", 0, "RAD IDLESS ADDIN B")));

            // The survivor with the very similar name is still there — name-keyed identity
            // has to distinguish "RAD Idless Addin" from "RAD Idless Addin B", in the
            // baseline as well as in the workspace.
            Assert.NotNull(ModuleDefinitionOps.ObjectSurfaceFingerprint(
                baseline, new RadObjectKey("ControlAddIn", 0, "RAD IDLESS ADDIN")));
            Assert.Equal(["ControlAddIn:0:RAD Idless Addin"],
                workspace.ObjectsIn(Path.Combine(tempRoot, "src", "RadIdlessAddin.ControlAddin.al"))
                    .Select(Describe).ToArray());
            Assert.True(compiler.EmitIncremental([tempRoot], ModuleName, workspace).NoChange);
        });
    }

    /// <summary>
    /// The regression guard: an app that declares id-less objects must still delta its
    /// ordinary ones by id, one object at a time.
    /// </summary>
    [Fact]
    public void AnOrdinaryEdit_InAnAppWithIdLessObjects_IsStillOneObject()
    {
        Run((compiler, workspace, tempRoot) =>
        {
            Replace(Path.Combine(tempRoot, "src", "RadProfileService.Codeunit.al"),
                "exit(140);", "exit(141);");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);
            Assert.False(delta.FullRebuild);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal(["RAD Profile Service"], RadFixture.EmittedNames(delta));
            Assert.Equal(["Codeunit:71401:RAD Profile Service"],
                delta.Changes.Modified.Select(Describe).ToArray());
        });
    }

    /// <summary>Seed a committed baseline over a private copy, then hand it to the scenario.</summary>
    private void Run(Action<BcCompiler, RadWorkspace, string> scenario)
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
            Assert.Equal(EmittedObjectCount, seed.Emit.Sources.Count);
            Assert.True(seed.CanCommit,
                "the first compile produced no committable baseline — the snapshot threw");
            seed.Commit(workspace, Load(workspace, seed.Emit.Sources));
            Assert.True(workspace.HasBaseline);

            scenario(compiler, workspace, tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>`Kind:Id:Name` — the whole identity, so a name-keyed object is legible.</summary>
    private static string Describe(RadObjectRef obj) => $"{obj.Key.Kind}:{obj.Key.Id}:{obj.Name}";

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
