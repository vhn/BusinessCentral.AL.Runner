using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// Performance contract for RAD itself, below FileSystemWatcher and the command line.
/// The emitted-object set is Microsoft's AddApplicationObject callback set; the loaded
/// type set proves that the resulting overlay replaces only those runtime objects.
///
/// Deletions live in <see cref="RadDeletionDeltaTests"/> and runtime metadata in
/// <see cref="RadMetadataDeltaTests"/>; this suite covers edits and additions.
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class RadObjectDeltaTests(BcEngineFixture engine)
{
    private const string ScenarioDir = "al-runner-rad-object-delta";

    public static IEnumerable<object[]> ObjectBodyEdits()
    {
        yield return Case(
            "codeunit",
            "RadPerfService.Codeunit.al",
            "exit(40);",
            "exit(41);",
            ["RAD Perf Service"],
            ["Codeunit71000"]);

        yield return Case(
            "table",
            "RadPerfHeader.Table.al",
            "Description := 'header-v1';",
            "Description := 'header-v2';",
            ["RAD Perf Header"],
            ["Record71000"]);

        // The compiler can emit an extension by itself. Its commit must refresh the one
        // affected target-table metadata entry, without recompiling that table or siblings.
        yield return Case(
            "tableextension",
            "RadPerfHeaderExtA.TableExt.al",
            "Rec.Description := 'extension-a-v1';",
            "Rec.Description := 'extension-a-v2';",
            ["RAD Perf Header Ext A"],
            ["TableExtension71000"]);

        yield return Case(
            "page",
            "RadPerfHeaderCard.Page.al",
            "Rec.Description := 'page-v1';",
            "Rec.Description := 'page-v2';",
            ["RAD Perf Header Card"],
            ["Page71000"]);

        yield return Case(
            "pageextension",
            "RadPerfHeaderCardExt.PageExt.al",
            "Rec.Description := 'pageextension-v1';",
            "Rec.Description := 'pageextension-v2';",
            ["RAD Perf Header Card Ext"],
            ["PageExtension71000"]);

        yield return Case(
            "report",
            "RadPerfHeaderReport.Report.al",
            "Marker := 1;",
            "Marker := 2;",
            ["RAD Perf Header Report"],
            ["Report71000"]);

        yield return Case(
            "query",
            "RadPerfHeaderQuery.Query.al",
            "TopNumberOfRows = 10;",
            "TopNumberOfRows = 11;",
            ["RAD Perf Header Query"],
            ["Query71000"]);

        yield return Case(
            "xmlport",
            "RadPerfHeaderXml.XmlPort.al",
            "exit(1);",
            "exit(2);",
            ["RAD Perf Header Xml"],
            ["XmlPort71000"]);

        yield return Case(
            "enum",
            "RadPerfStatus.Enum.al",
            "Caption = 'Open';",
            "Caption = 'Open v2';",
            ["RAD Perf Status"],
            []);

        yield return Case(
            "enumextension",
            "RadPerfStatusExt.EnumExt.al",
            "Caption = 'Archived';",
            "Caption = 'Archived v2';",
            ["RAD Perf Status Ext"],
            []);
    }

    [SkippableTheory]
    [MemberData(nameof(ObjectBodyEdits))]
    public void EditingOneObject_ReloadsOnlyItsSemanticDelta(
        string objectKind,
        string fileName,
        string before,
        string after,
        string[] expectedEmittedObjects,
        string[] expectedReloadedTypes)
    {
        RunOverlayScenario(
            $"{objectKind} edit",
            tempRoot => RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, fileName), before, after),
            expectedEmittedObjects,
            expectedReloadedTypes);
    }

    /// <summary>
    /// Schema edits, the reason `--watch` is worth having on an app with tables: the
    /// runner has no database, so adding a field is an object recompile and a metadata
    /// refresh, never a migration. A structural change must therefore stay exactly as
    /// proportional as a body edit — one object in, one object out.
    /// </summary>
    public static IEnumerable<object[]> StructuralEdits()
    {
        // Field ids from the AL ID Manager for app e23cd601 (table 71001 → next free 3).
        yield return Case(
            "table field added",
            "RadPerfLine.Table.al",
            "field(2; \"Header No.\"; Code[20]) { DataClassification = SystemMetadata; }",
            """
            field(2; "Header No."; Code[20]) { DataClassification = SystemMetadata; }
                    field(3; Note; Text[30]) { DataClassification = SystemMetadata; }
            """,
            ["RAD Perf Line"],
            ["Record71001"]);

        // Extension fields 71000/71001 already extend RAD Perf Header, so the next free
        // field id on that target table is 71002 — the allocator does not know this
        // fixture's existing fields, only its 71000-71199 object range.
        yield return Case(
            "tableextension field added",
            "RadPerfHeaderExtA.TableExt.al",
            "field(71000; \"Extension A\"; Text[30])",
            """
            field(71002; "Extension A Note"; Text[30]) { DataClassification = SystemMetadata; }
                    field(71000; "Extension A"; Text[30])
            """,
            ["RAD Perf Header Ext A"],
            ["TableExtension71000"]);

        // Enumextension value 71001 from the AL ID Manager (71000 = Archived is taken).
        yield return Case(
            "enumextension value added",
            "RadPerfStatusExt.EnumExt.al",
            "value(71000; Archived) { Caption = 'Archived'; }",
            """
            value(71000; Archived) { Caption = 'Archived'; }
                value(71001; Retired) { Caption = 'Retired'; }
            """,
            ["RAD Perf Status Ext"],
            []);

        // A page's metadata surface, not its code: the control tree is what BC resolves a
        // TestPage against, and it lives in the emitted metadata XML.
        yield return Case(
            "page control added",
            "RadPerfLineList.Page.al",
            "field(HeaderNo; Rec.\"Header No.\") { ApplicationArea = All; }",
            """
            field(HeaderNo; Rec."Header No.") { ApplicationArea = All; }
                            field(HeaderNoAgain; Rec."Header No.") { ApplicationArea = All; }
            """,
            ["RAD Perf Line List"],
            ["Page71001"]);
    }

    [SkippableTheory]
    [MemberData(nameof(StructuralEdits))]
    public void StructurallyEditingOneObject_StaysProportional(
        string scenario,
        string fileName,
        string before,
        string after,
        string[] expectedEmittedObjects,
        string[] expectedReloadedTypes)
    {
        RunOverlayScenario(
            scenario,
            tempRoot => RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, fileName), before, after),
            expectedEmittedObjects,
            expectedReloadedTypes);
    }

    [SkippableFact]
    public void EditingACallableSurface_ReloadsTheChangedObjects_ButNotTheirTransitiveCaller()
    {
        RunOverlayScenario(
            "callable-surface edit",
            tempRoot => RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "procedure Coerce(Input: Integer): Integer",
                "procedure Coerce(Input: Decimal): Integer"),
            ["RAD Perf Caller", "RAD Perf Service"],
            ["Codeunit71000", "Codeunit71001"]);
    }

    /// <summary>
    /// Adding a procedure moves the callable surface too — generated calls bake
    /// Microsoft's member ids, so the direct caller has to rebind. What must NOT happen is
    /// the transitive caller (RAD Perf Unrelated A → Caller → Service) coming along:
    /// that is the difference between a one-hop rebind and a whole-module rebuild on a
    /// deep dependency graph.
    /// </summary>
    [SkippableFact]
    public void AddingAProcedure_RebindsDirectCallersOnly()
    {
        RunOverlayScenario(
            "procedure addition",
            tempRoot => RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "    procedure Coerce",
                """
                    procedure Added(): Integer
                    begin
                        exit(1);
                    end;

                    procedure Coerce
                """),
            ["RAD Perf Caller", "RAD Perf Service"],
            ["Codeunit71000", "Codeunit71001"]);
    }

    /// <summary>
    /// The surface fingerprint must compare the callable surface, and nothing else the
    /// compiler happens to record beside it.
    ///
    /// <para>Concretely: when the compile is given an app root (#1912, which is what the CLI
    /// does on every cycle) BC records a <c>ReferenceSourceFileName</c> on each symbol it
    /// writes. That used to be asymmetric — the full compile got a file system, the RAD one did
    /// not — so a re-emitted object came back with that property NULL. If the fingerprint is the
    /// raw serialized symbol, every modified codeunit therefore reads as "its surface moved",
    /// pulls in its direct callers, and those pull in theirs: measured on this fixture, a
    /// one-line body edit went from 1 re-emitted object to 3, with two rounds of "rebinding
    /// direct caller file(s)". On an app the size of NP Retail that is the difference between a
    /// delta and a cascade.</para>
    ///
    /// <para>The delta is now constructed with the same file system, so both producers record
    /// the identical relative path and the canonicalisation no longer has anything to do here.
    /// These two tests keep their teeth regardless: withhold the file system from the RAD
    /// compilation alone and they fail again, the body edit pulling in
    /// <c>RAD Perf Unrelated A</c>. That is what makes them a check on the fingerprint rather
    /// than on one BC release's serializer.</para>
    ///
    /// <para>Both directions, because a fingerprint that ignored too much would be worse than
    /// one that ignores too little: the body edit must stay at one object, and changing the
    /// actual signature immediately below must still rebind the caller.</para>
    /// </summary>
    [SkippableFact]
    public void ABodyEdit_StaysOneObject_WhenTheCompileRecordsSourceFileNames()
    {
        RunOverlayScenario(
            "body edit with a file system",
            tempRoot => RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "exit(40);",
                "exit(41);"),
            ["RAD Perf Service"],
            ["Codeunit71000"],
            withFileSystem: true);
    }

    [SkippableFact]
    public void ACallableSurfaceEdit_StillRebindsItsCaller_WhenTheCompileRecordsSourceFileNames()
    {
        RunOverlayScenario(
            "callable-surface edit with a file system",
            tempRoot => RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "procedure Coerce(Input: Integer): Integer",
                "procedure Coerce(Input: Decimal): Integer"),
            ["RAD Perf Caller", "RAD Perf Service"],
            ["Codeunit71000", "Codeunit71001"],
            withFileSystem: true);
    }

    /// <summary>
    /// The second way the two baselines describe one unchanged surface differently — and the
    /// one that broke `--watch` on NP Retail outright, where every warm cycle re-emitted zero
    /// objects and reported an AL error in a file the edit never touched.
    ///
    /// <para>The committed baseline comes from
    /// <c>SerializableSymbolModelConverter.ConvertModuleToSerializableSymbolModel(Compilation)</c>
    /// and is held as an object graph. The merged one is written by
    /// <c>CompilationUtilities.WriteSymbolReference</c> and read back with
    /// <c>SymbolReferenceJsonReader</c> — so it has been through a JSON round trip that the
    /// committed one has not. A method ATTRIBUTE that takes no arguments is where the two
    /// representations disagree: the converter leaves <c>Arguments</c> null, the round trip
    /// materialises it as an empty array. Measured on NP Retail's `NPR Adyen Management`, whose
    /// serialized surface is 36 KB — five argument-less attributes, ten characters of
    /// difference, and the whole cascade below follows from it:</para>
    ///
    /// <code>
    /// before …"NameForSerialization":"NonDebuggable","Arguments":null}]…
    /// after  …"NameForSerialization":"NonDebuggable","Arguments":[]}]…
    /// </code>
    ///
    /// <para>The codeunit then reads as "its surface moved", which pulls in its 30 direct caller
    /// files, one of which cannot bind against the reduced packaged baseline — so the cycle
    /// reported `EMIT-ZERO` plus an AL0126 and the watch loop never progressed. No fixture object
    /// carried a method attribute, so nothing caught it: the divergence needs an attribute to
    /// exist at all.</para>
    ///
    /// <para>`[NonDebuggable]` is the attribute used here because it takes no arguments, which
    /// is the whole trigger — an attribute WITH arguments serializes the same on both sides.</para>
    /// </summary>
    [SkippableFact]
    public void ABodyEdit_StaysOneObject_WhenTheEditedCodeunitCarriesAnArgumentLessAttribute()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            // Before the baseline exists, so the attribute is part of what the full compile
            // recorded — the edit below then changes nothing but a literal.
            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "    procedure Value(): Integer",
                """
                    [NonDebuggable]
                    procedure Value(): Integer
                """);
            var baseline = RadFixture.Seed(tempRoot);

            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "exit(40);",
                "exit(41);");

            var delta = baseline.Cycle(tempRoot);

            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            // RAD Perf Caller calls Service.Coerce, so a spurious surface move shows up here
            // as a second emitted object — exactly what npcore's cascade started as.
            Assert.Equal(["RAD Perf Service"], RadFixture.EmittedNames(delta));

            var overlay = RadFixture.AssembleAndLoad(baseline.Workspace, delta.Emit.Sources);
            delta.Commit(baseline.Workspace, overlay);
            baseline.AssertOwnership(overlay, ["Codeunit71000"]);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The other direction of the test above: ignoring an attribute's ARGUMENTS is not the fix.
    /// An <c>[EventSubscriber]</c> names the object and event it binds to in its arguments, so
    /// retargeting one is a real surface change and its callers must still rebind.
    /// </summary>
    [SkippableFact]
    public void ChangingAnAttributesArguments_StillCountsAsASurfaceMove()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "    procedure Coerce(Input: Integer): Integer",
                """
                    [IntegrationEvent(false, false)]
                    procedure OnSomething()
                    begin
                    end;

                    procedure Coerce(Input: Integer): Integer
                """);
            var baseline = RadFixture.Seed(tempRoot);

            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                "[IntegrationEvent(false, false)]",
                "[IntegrationEvent(true, false)]");

            var delta = baseline.Cycle(tempRoot);

            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.False(delta.FullRebuild);
            Assert.Equal(
                ["RAD Perf Caller", "RAD Perf Service"], RadFixture.EmittedNames(delta));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [SkippableFact]
    public void AddingOneCodeunit_ReloadsOnlyTheAddedObject()
    {
        RunOverlayScenario(
            "codeunit addition",
            WriteAddedCodeunit,
            ["RAD Perf Added"],
            ["Codeunit71006"]);
    }

    /// <summary>
    /// A rename keeps the object id and therefore its CLR type, but the OLD name must
    /// leave the symbol baseline. Microsoft's symbol merger keys on id AND name, so a
    /// baseline that kept both definitions would let source still bind to the old name —
    /// a compile that succeeds here and fails on a cold full build.
    /// </summary>
    [SkippableFact]
    public void RenamingAnObject_DropsTheOldNameFromTheBaseline()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, "RadPerfUnrelatedD.Codeunit.al"),
                "\"RAD Perf Unrelated D\"",
                "\"RAD Perf Renamed D\"");

            var rename = baseline.Cycle(tempRoot);
            Assert.False(rename.FullRebuild);
            Assert.True(rename.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, rename.Emit.Diagnostics));
            Assert.Equal(["RAD Perf Renamed D"], RadFixture.EmittedNames(rename));
            var renamed = Assert.Single(rename.Changes.Modified);
            Assert.Equal(new RadObjectKey("Codeunit", 71005), renamed.Key);
            Assert.Equal("RAD Perf Renamed D", renamed.Name);

            var overlay = RadFixture.AssembleAndLoad(baseline.Workspace, rename.Emit.Sources);
            rename.Commit(baseline.Workspace, overlay);
            baseline.AssertOwnership(overlay, ["Codeunit71005"]);
            baseline.AssertSettled(tempRoot);

            // Read the committed baseline back the way the next delta binds against it.
            var symbols = File.ReadAllText(BcCompiler.WriteWorkspaceSymbols(
                baseline.Workspace, Path.Combine(tempRoot, "committed.symbols.json")));
            Assert.Contains("RAD Perf Renamed D", symbols);
            Assert.DoesNotContain("RAD Perf Unrelated D", symbols);
            // Every other object survived the merge — a rename must not shrink the baseline.
            foreach (var kept in new[]
                { "RAD Perf Service", "RAD Perf Caller", "RAD Perf Header", "RAD Perf Status" })
                Assert.Contains(kept, symbols);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// An id-less object — one keyed by name because AL gives it no id — is a delta like any
    /// other, and on a 20-object app that is the whole point: adding a `controladdin` used to
    /// rebuild all twenty. It generates no C#, so a correct delta compiles nothing at all and
    /// every existing runtime object stays exactly where it was.
    /// </summary>
    [SkippableFact]
    public void AddingAnIdLessObject_IsADelta_AndCompilesNothing()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);

            File.WriteAllText(
                RadFixture.SourceFile(tempRoot, "RadPerfAddIn.ControlAddIn.al"),
                """
                namespace AlRunner.Tests.RadTwentyObject;

                controladdin "RAD Perf Add In"
                {
                    RequestedHeight = 100;
                    RequestedWidth = 100;
                }
                """);

            var delta = baseline.Cycle(tempRoot);
            Assert.False(delta.FullRebuild,
                "adding an id-less object rebuilt the whole module");
            Assert.False(delta.NoChange);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Equal(["RAD Perf Add In"], delta.Changes.Added.Select(item => item.Name).ToArray());
            Assert.Empty(delta.Changes.Modified);
            Assert.Empty(delta.Changes.Removed);
            Assert.Empty(delta.Emit.Sources);

            delta.Commit(baseline.Workspace, null);
            // Nothing moved: every one of the twenty baseline types is still the identical
            // Type instance the seed produced.
            baseline.AssertOwnership(owner: null, moved: []);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// A file that declares no AL object — a new empty one, a comment-only one — costs NO
    /// compiler work over its whole life: created, edited, deleted, all three are deltas that
    /// emit nothing and reload nothing.
    ///
    /// <para>Each of those used to be a whole-module rebuild, on the grounds that "the
    /// workspace has no objects for this path" is also what an unidentifiable declaration
    /// looks like. It is not the same thing, and the difference is now recorded rather than
    /// inferred: BC's parser answers "declares nothing" positively (no `ObjectSyntax` node in
    /// the tree), and the workspace carries a per-file note for the files whose declarations a
    /// compile could NOT fully record — see
    /// <see cref="AFileTheWorkspaceCouldNotFullyRecord_StillForcesAFullCompile"/> and
    /// <see cref="AFileDeclaringADotNetPackage_StillForcesAFullCompile"/>, which are the two
    /// cases that must stay on the full-compile path.</para>
    ///
    /// <para>The claim is exact: zero emitted sources, an empty change set, every one of the
    /// twenty baseline types still the identical <see cref="Type"/> instance, and not one
    /// full-recompile note recorded for the developer to read.</para>
    /// </summary>
    [SkippableFact]
    public void AFileThatDeclaresNoObject_CostsNoCompilerWork_WhenAddedEditedOrDeleted()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            AlRunner.Rad.RadCycleNotes.Drain();   // whatever the seed cycle left behind

            var file = RadFixture.WriteDeclarationlessFile(tempRoot, "created");
            AssertNoCompilerWork(baseline, tempRoot, "adding");

            RadFixture.WriteDeclarationlessFile(tempRoot, "edited");
            AssertNoCompilerWork(baseline, tempRoot, "editing");

            File.Delete(file);
            AssertNoCompilerWork(baseline, tempRoot, "deleting");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// One cycle over a file that declares nothing: a delta that compiles nothing, replaces
    /// nothing, explains nothing to the developer — and leaves the tree settled, so the file
    /// is not re-detected as changed on the cycle after.
    /// </summary>
    private static void AssertNoCompilerWork(SeededBaseline baseline, string tempRoot, string operation)
    {
        var cycle = baseline.Cycle(tempRoot);
        Assert.False(cycle.FullRebuild, $"{operation} a file that declares no object rebuilt the module");
        Assert.False(cycle.NoChange, $"{operation} a file must be seen as a change at all");
        Assert.True(cycle.Emit.Diagnostics.Count == 0,
            string.Join(Environment.NewLine, cycle.Emit.Diagnostics));
        Assert.Empty(cycle.Emit.Sources);
        Assert.Empty(cycle.Changes.Added);
        Assert.Empty(cycle.Changes.Modified);
        Assert.Empty(cycle.Changes.Removed);
        // Nothing to tell the developer: the yellow "full recompile" panel must stay absent.
        Assert.Empty(AlRunner.Rad.RadCycleNotes.Drain());

        cycle.Commit(baseline.Workspace, assembly: null);
        Assert.Single(baseline.Workspace.Generations);
        baseline.AssertOwnership(owner: null, moved: []);
        baseline.AssertSettled(tempRoot);
    }

    /// <summary>
    /// The one file shape that declares no AL object and still gets the whole module. A
    /// `dotnet` package publishes the types every object in the app binds against, and a RAD
    /// object compilation carries no package declaration trees at all — `MergeRadBaseline`
    /// restores the previously committed `DotNetPackages` wholesale, so a delta over an edited
    /// one would compile against the packages as they were.
    ///
    /// Asserted in both directions, because they reach the rule differently: declaring one is
    /// read off the changed file's syntax, while DELETING one can only come from the per-file
    /// record the previous compile left behind — there is no file left to parse.
    /// </summary>
    [SkippableFact]
    public void AFileDeclaringADotNetPackage_StillForcesAFullCompile()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            AlRunner.Rad.RadCycleNotes.Drain();

            var packages = RadFixture.WriteDotNetPackageFile(tempRoot, "RadPerfBuilder");
            var added = baseline.Cycle(tempRoot);
            Assert.True(added.FullRebuild, "declaring a dotnet package must rebuild the module");
            // And it says so, naming the file — an unexplained whole-module rebuild is
            // indistinguishable from the delta path being broken.
            Assert.Contains(
                $"{Path.GetFileName(packages)} declares a dotnet package",
                string.Join(" | ", AlRunner.Rad.RadCycleNotes.Drain()));
            Assert.True(added.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, added.Emit.Diagnostics));
            Assert.True(added.Emit.Sources.Count >= RadFixture.ObjectCount,
                $"a full compile emitted only {added.Emit.Sources.Count} object(s)");
            added.Commit(
                baseline.Workspace,
                RadFixture.AssembleAndLoad(baseline.Workspace, added.Emit.Sources));
            baseline.AssertSettled(tempRoot);

            // Now delete it. Nothing is left to parse, so this can only be answered from what
            // the full compile recorded about the file.
            File.Delete(packages);
            var deleted = baseline.Cycle(tempRoot);
            Assert.True(deleted.FullRebuild, "deleting a dotnet package declaration must rebuild the module");
            Assert.Contains(
                $"{Path.GetFileName(packages)} declares a dotnet package",
                string.Join(" | ", AlRunner.Rad.RadCycleNotes.Drain()));
            Assert.True(deleted.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, deleted.Emit.Diagnostics));
            deleted.Commit(
                baseline.Workspace,
                RadFixture.AssembleAndLoad(baseline.Workspace, deleted.Emit.Sources));
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// A `controladdin` declaring a resource file (`StartupScript`, `Scripts`, `StyleSheets`,
    /// `Images`) must be adjudicated BY THE DELTA, exactly as the full compile adjudicates it.
    ///
    /// <para>BC resolves those paths through an <c>IFileSystem</c> the compilation is given,
    /// anchored at the app root (#1899/#1912). <c>Compilation.CreateForRad</c> takes one as its
    /// optional parameter 12 and the delta now passes it, under the same
    /// <c>appRootDir != null &amp;&amp; Directory.Exists</c> guard the full compile uses — so
    /// neither path can answer a resource question the other cannot.</para>
    ///
    /// <para>Before that, the delta had no file system, raised AL0327 "Missing file" for a file
    /// present at the declared path, and the cycle had to hand the whole module to a full
    /// compile to find out. Measured on this fixture: 20 objects re-emitted for a one-line
    /// property edit to an add-in that emits no C# at all.</para>
    ///
    /// <para>Both directions, because a fix that simply suppressed AL0327 would pass the first
    /// half and hide every real typo: a resource path that genuinely does not exist must still
    /// raise AL0327 naming that file — and now from the delta itself, without spending a
    /// whole-module compile to say so.</para>
    /// </summary>
    [SkippableFact]
    public void AControlAddInResourcePath_IsAnsweredByTheDelta_NotSilencedAndNotFailed()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);

            // The add-in and its script are added BEFORE the baseline, so the resource path is
            // part of the committed module and the edit below is an ordinary warm cycle.
            Directory.CreateDirectory(Path.Combine(tempRoot, "src", "addin"));
            File.WriteAllText(
                Path.Combine(tempRoot, "src", "addin", "startup.js"), "function Start() { }\n");
            var addin = RadFixture.SourceFile(tempRoot, "RadPerfAddin.ControlAddIn.al");
            File.WriteAllText(addin,
                "controladdin \"RAD Perf Addin\"\n{\n" +
                "    StartupScript = 'src/addin/startup.js';\n" +
                "    RequestedHeight = 100;\n}\n");

            // Still 20 emitted sources: a controladdin generates no C# of its own.
            var baseline = RadFixture.Seed(tempRoot, appRootDir: tempRoot);
            AlRunner.Rad.RadCycleNotes.Drain();

            RadFixture.ReplaceExactlyOnce(addin, "RequestedHeight = 100;", "RequestedHeight = 120;");
            var edited = baseline.Cycle(tempRoot, appRootDir: tempRoot);

            Assert.DoesNotContain("AL0327", string.Join(Environment.NewLine, edited.Emit.Diagnostics));
            Assert.True(edited.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, edited.Emit.Diagnostics));
            // The claim this test exists for: the delta ANSWERED. A controladdin emits no C#,
            // so a delta that resolved the path emits nothing at all — where the fallback
            // re-emitted all 20 objects and said so.
            Assert.False(edited.FullRebuild);
            Assert.Empty(edited.Emit.Sources);
            Assert.DoesNotContain(
                "file resource (AL0327)",
                string.Join(" | ", AlRunner.Rad.RadCycleNotes.Drain()));
            edited.Commit(baseline.Workspace, null);
            baseline.AssertSettled(tempRoot);

            // Negative: point it at a file that is not there. The answer must still be AL0327
            // naming that path — the delta was given a file system so it could tell the two
            // apart, not so it could stop reporting either.
            RadFixture.ReplaceExactlyOnce(
                addin, "StartupScript = 'src/addin/startup.js';", "StartupScript = 'src/addin/absent.js';");
            var broken = baseline.Cycle(tempRoot, appRootDir: tempRoot);
            var reported = string.Join(Environment.NewLine, broken.Emit.Diagnostics);
            Assert.Contains("AL0327", reported);
            Assert.Contains("absent.js", reported);
            Assert.False(broken.FullRebuild);
            Assert.DoesNotContain(
                "file resource (AL0327)",
                string.Join(" | ", AlRunner.Rad.RadCycleNotes.Drain()));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// Giving the RAD compilation a file system must not cost it its packaged module
    /// definition — the thing that lets every object the delta did NOT re-parse still resolve.
    ///
    /// <para>This is not hypothetical. The obvious way to attach one —
    /// <c>rad.WithFileSystem(new RelativeFileSystem(appRoot))</c> on the compilation
    /// <c>CreateForRad</c> returned — measurably loses it. Run against this exact scenario it
    /// answers:</para>
    /// <code>
    /// RadPerfHeaderExtA.TableExt.al@3:54: error AL0247:
    ///     The target Table 'RAD Perf Header' for the extension object is not found
    /// </code>
    /// <para>…and emits zero objects, because the extension's target lives only in the packaged
    /// definition that clone dropped. Passing the same file system as <c>CreateForRad</c>'s
    /// <c>fileSystem</c> parameter does not: the delta below re-emits its one tableextension
    /// and binds the target out of the packaged definition, as it did with no file system
    /// at all.</para>
    ///
    /// <para>A body edit to a plain codeunit would NOT catch this — it needs nothing from the
    /// packaged definition and stays green either way (measured). The probe has to be an
    /// extension object whose target is untouched.</para>
    /// </summary>
    [SkippableFact]
    public void ADeltaGivenAFileSystem_StillResolvesAnUntouchedExtensionTarget()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            // appRootDir is what makes the file system real — without it there is nothing to
            // attach and this scenario cannot fail.
            var baseline = RadFixture.Seed(tempRoot, appRootDir: tempRoot);
            AlRunner.Rad.RadCycleNotes.Drain();

            var extension = RadFixture.SourceFile(tempRoot, "RadPerfHeaderExtA.TableExt.al");
            RadFixture.ReplaceExactlyOnce(extension, "'extension-a-v1'", "'extension-a-v2'");
            var edited = baseline.Cycle(tempRoot, appRootDir: tempRoot);

            Assert.DoesNotContain("AL0247", string.Join(Environment.NewLine, edited.Emit.Diagnostics));
            Assert.True(edited.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, edited.Emit.Diagnostics));
            Assert.False(edited.FullRebuild);
            Assert.Empty(AlRunner.Rad.RadCycleNotes.Drain());
            Assert.Equal(["RAD Perf Header Ext A"], RadFixture.EmittedNames(edited));

            var assembly = RadFixture.AssembleAndLoad(baseline.Workspace, edited.Emit.Sources);
            edited.Commit(baseline.Workspace, assembly);
            baseline.AssertSettled(tempRoot);

            // Negative: the same delta must still FAIL when the target genuinely is not there.
            // Deleting the target table leaves the extension bound to nothing, and the answer
            // has to be that diagnostic — not a clean cycle, and not the packaged definition
            // quietly serving the old table.
            File.Delete(RadFixture.SourceFile(tempRoot, "RadPerfHeader.Table.al"));
            var orphaned = baseline.Cycle(tempRoot, appRootDir: tempRoot);
            Assert.NotEmpty(orphaned.Emit.Diagnostics);
            Assert.Contains(
                "RAD Perf Header",
                string.Join(Environment.NewLine, orphaned.Emit.Diagnostics));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The guard that makes the delta above safe. A file the workspace could not fully record
    /// — a declaration with no usable key, or one whose key a second file also claimed, both of
    /// which `MapObjectsToFiles` drops rather than guess at — must NOT be read as a file that
    /// declares nothing. Emptying such a file would otherwise pass for a comment-only edit
    /// while the object's symbol survived in the baseline.
    ///
    /// <para>The state is driven in through the workspace rather than through AL source,
    /// because no valid AL produces it: a duplicate id or name is a compile error, so a full
    /// compile clean enough to become a baseline cannot contain one. That is the reason this
    /// is a guard rather than a live path — and the reason it needs a test, since nothing else
    /// would notice it being dropped.</para>
    /// </summary>
    [SkippableFact]
    public void AFileTheWorkspaceCouldNotFullyRecord_StillForcesAFullCompile()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            AlRunner.Rad.RadCycleNotes.Drain();

            // The workspace now believes it never learned what this file declares.
            var opaque = RadFixture.SourceFile(tempRoot, "RadPerfUnrelatedA.Codeunit.al");
            baseline.Workspace.Commit(new RadWorkspaceUpdate(
                RadWorkspace.HashSourceTree(
                    Directory.EnumerateFiles(tempRoot, "*.al", SearchOption.AllDirectories).ToList()),
                new Dictionary<string, List<RadObjectRef>>
                {
                    [opaque] = baseline.Workspace.ObjectsIn(opaque).ToList(),
                },
                new Dictionary<string, RadFileDeclarations>
                {
                    [opaque] = new(DotNetPackage: false, Unrecorded: true),
                },
                new Dictionary<RadObjectKey, HashSet<RadObjectKey>>(),
                new Dictionary<RadObjectKey, HashSet<RadAppObjectRef>>(),
                new Dictionary<RadObjectKey, RadObjectKey>(),
                Array.Empty<RadObjectKey>(),
                Array.Empty<RadObjectKey>(),
                baseline.Workspace.Baseline!,
                Full: false));

            File.AppendAllText(opaque, "// touched\n");
            var cycle = baseline.Cycle(tempRoot);
            Assert.True(cycle.FullRebuild,
                "a file whose declarations were never recorded must not be deltaed");
            Assert.Contains(
                $"{Path.GetFileName(opaque)} declared something the last full compile could not identify",
                string.Join(" | ", AlRunner.Rad.RadCycleNotes.Drain()));
            Assert.True(cycle.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, cycle.Emit.Diagnostics));

            cycle.Commit(
                baseline.Workspace,
                RadFixture.AssembleAndLoad(baseline.Workspace, cycle.Emit.Sources));
            // …and the full compile clears the note: the next edit to that file deltas again.
            Assert.Equal(default, baseline.Workspace.DeclarationsIn(opaque));
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The guard rail on the other side: a delta is only interchangeable with a full compile
    /// while both resolve the same external symbols. Change the app's own identity — or its
    /// dependencies, or its preprocessor symbols — and every cached object was bound against
    /// a picture that no longer holds, so the workspace must invalidate rather than emit an
    /// overlay that silently disagrees with the module its dependents compiled against.
    ///
    /// It also has to SAY which facet moved, naming `app.json`. Switching a git branch usually
    /// switches `app.json` with it, and that is the common way a warm watch loses its baseline —
    /// a cycle that silently costs minutes instead of a second reads as the delta path being
    /// broken rather than as the developer's own branch switch. The reason is asserted through
    /// `RadCycleNotes` rather than through the log line because the interactive dashboard
    /// redirects stderr to `TextWriter.Null` while the bundle loop runs, so the note is the
    /// only form of it the developer ever sees.
    /// </summary>
    [SkippableFact]
    public void ChangingTheReferenceSurface_InvalidatesTheBaseline_AndSaysWhichFacetMoved()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            SeededBaseline baseline;
            using (BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion))
            {
                baseline = RadFixture.Seed(tempRoot);
                RadFixture.ReplaceExactlyOnce(
                    RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"),
                    "exit(40);", "exit(41);");
            }

            using (BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, new Version(1, 0, 0, 1)))
            {
                AlRunner.Rad.RadCycleNotes.Drain();   // whatever the seed cycle left behind
                var rebuilt = baseline.Cycle(tempRoot);
                Assert.True(rebuilt.FullRebuild,
                    "a version change left the delta path armed against a stale baseline");
                Assert.Contains(
                    "app.json changed the app version: 1.0.0.0 → 1.0.0.1",
                    string.Join(" | ", AlRunner.Rad.RadCycleNotes.Drain()));
                Assert.True(rebuilt.Emit.Diagnostics.Count == 0,
                    string.Join(Environment.NewLine, rebuilt.Emit.Diagnostics));
                Assert.Equal(RadFixture.ObjectCount, rebuilt.Emit.Sources.Count);
                Assert.False(baseline.Workspace.HasBaseline);

                var reseeded = RadFixture.AssembleAndLoad(baseline.Workspace, rebuilt.Emit.Sources);
                rebuilt.Commit(baseline.Workspace, reseeded);
                Assert.True(baseline.Workspace.HasBaseline);
                // A full generation supersedes the whole chain, not just the edited object.
                foreach (var name in baseline.Types.Keys)
                    Assert.Same(reseeded, AlObjectResolution.FindOwned(name, requiredBase: null)?.Assembly);
                baseline.AssertSettled(tempRoot);
            }
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [SkippableFact]
    public void AbandonedCandidate_IsRetriedUntilOneIsCommitted()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            WriteAddedCodeunit(tempRoot);

            var abandoned = baseline.Cycle(tempRoot);
            Assert.False(abandoned.NoChange);
            Assert.Equal(["RAD Perf Added"], RadFixture.EmittedNames(abandoned));
            Assert.Single(abandoned.Changes.Added);

            // Deliberately do not assemble or commit the first candidate.
            var retry = baseline.Cycle(tempRoot);

            Assert.False(retry.NoChange);
            Assert.False(retry.FullRebuild);
            Assert.Equal(RadFixture.EmittedNames(abandoned), RadFixture.EmittedNames(retry));
            Assert.Equal(
                RadFixture.KeyStrings(abandoned.Changes.Added),
                RadFixture.KeyStrings(retry.Changes.Added));
            Assert.Empty(retry.Changes.Modified);
            Assert.Empty(retry.Changes.Removed);

            var assembly = RadFixture.AssembleAndLoad(baseline.Workspace, retry.Emit.Sources);
            retry.Commit(baseline.Workspace, assembly);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// A generation the C# backend rejects must leave the workspace exactly where it was:
    /// the same edit has to recompile next cycle, not report the app unchanged and run the
    /// previous generation's code as a green test.
    /// </summary>
    [SkippableFact]
    public void RejectedCandidate_DoesNotAdvanceTheWorkspace()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            // AL-valid, but its generated C# is rejected by Roslyn — the cheapest way to
            // exercise "AL emit succeeded, backend failed" without a synthetic seam.
            File.WriteAllText(
                RadFixture.SourceFile(tempRoot, "RadPerfUnrelatedD.Codeunit.al"),
                """
                namespace AlRunner.Tests.RadTwentyObject;

                codeunit 71005 "RAD Perf Unrelated D"
                {
                    procedure Value(): Integer
                    var
                        FileName: Text;
                    begin
                        Database.ExportData(false, FileName);
                        exit(105);
                    end;
                }
                """);

            var rejected = baseline.Cycle(tempRoot);
            Assert.False(rejected.FullRebuild);
            Assert.True(rejected.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, rejected.Emit.Diagnostics));
            Assert.Equal(["RAD Perf Unrelated D"], RadFixture.EmittedNames(rejected));

            var compiled = RadFixture.TryAssemble(baseline.Workspace, rejected.Emit.Sources);
            Assert.False(compiled.Success,
                "the fixture edit no longer produces C# Roslyn rejects; pick another");

            // Nothing committed: ownership is untouched and the same edit recompiles.
            baseline.AssertOwnership(owner: null, moved: []);
            var again = baseline.Cycle(tempRoot);
            Assert.False(again.NoChange);
            Assert.Equal(RadFixture.EmittedNames(rejected), RadFixture.EmittedNames(again));
            Assert.Equal(
                RadFixture.KeyStrings(rejected.Changes.Modified),
                RadFixture.KeyStrings(again.Changes.Modified));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private void RunOverlayScenario(
        string scenario,
        Action<string> mutate,
        string[] expectedEmittedObjects,
        string[] expectedReloadedTypes,
        bool withFileSystem = false)
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            // withFileSystem mirrors what the CLI does (`RunEmit` passes the app root, #1912);
            // the compiler then records a ReferenceSourceFileName on every symbol it writes,
            // which a RAD compilation cannot do — see AObjectSurfaceFingerprint... below.
            var appRootDir = withFileSystem ? tempRoot : null;
            var baseline = RadFixture.Seed(tempRoot, appRootDir);
            mutate(tempRoot);

            var delta = baseline.Cycle(tempRoot, appRootDir);
            var actualNames = RadFixture.EmittedNames(delta);
            var expectedNames = expectedEmittedObjects.Order(StringComparer.Ordinal).ToArray();

            Assert.False(delta.FullRebuild,
                $"{scenario} rebuilt all {actualNames.Length} emitted objects: " +
                string.Join(", ", actualNames));
            Assert.False(delta.NoChange);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Empty(delta.Emit.ExcludedObjects);
            Assert.Equal(expectedNames, actualNames);
            Assert.True(actualNames.Length < RadFixture.ObjectCount,
                $"{scenario} re-emitted the complete fixture");

            var overlayAssembly = RadFixture.AssembleAndLoad(baseline.Workspace, delta.Emit.Sources);
            delta.Commit(baseline.Workspace, overlayAssembly);

            var expectedTypes = expectedReloadedTypes.Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expectedTypes, RadFixture.ReloadedTypeNames(overlayAssembly));
            Assert.True(expectedTypes.Length < baseline.Types.Count,
                $"{scenario} reloaded every generated runtime object");

            baseline.AssertOwnership(overlayAssembly, expectedTypes);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static object[] Case(
        string scenario,
        string fileName,
        string before,
        string after,
        string[] expectedEmittedObjects,
        string[] expectedReloadedTypes) =>
        [scenario, fileName, before, after, expectedEmittedObjects, expectedReloadedTypes];

    // Codeunit 71006 from the AL ID Manager for app e23cd601.
    private static void WriteAddedCodeunit(string tempRoot) => File.WriteAllText(
        RadFixture.SourceFile(tempRoot, "RadPerfAdded.Codeunit.al"),
        """
        namespace AlRunner.Tests.RadTwentyObject;

        codeunit 71006 "RAD Perf Added"
        {
            procedure Value(): Integer
            begin
                exit(106);
            end;
        }
        """);
}
