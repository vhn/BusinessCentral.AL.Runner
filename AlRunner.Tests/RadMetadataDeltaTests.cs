// RadMetadataDeltaTests — the second half of a reload, and the half with no type system
// to keep it honest.
//
// A page, report, xmlport or enum is not just its generated CLR type. BC resolves it
// through runtime metadata that the AL emitter writes into process-wide registries
// (AlPageMetadataRegistry, AlReportMetadataRegistry, AlXmlPortMetadataRegistry,
// AlEnumMetadataRegistry) as a side effect of Compilation.Emit. So a RAD cycle has to keep
// two things proportional, not one:
//
//   1. refresh the metadata of the objects it re-emitted — and only those. Refreshing all
//      twenty entries to replace one is the "rebuild the world" cost RAD exists to avoid,
//      and it is invisible in the emitted-object count.
//   2. drop the metadata of objects it removed. A deleted page whose metadata survives is
//      the metadata-level twin of a missing tombstone: BC still finds the object.
//
// And one thing atomic: those writes happen at AL-emit time, BEFORE Roslyn compiles the
// generated C# and before the assembly loads. A candidate the backend rejects must
// therefore leave no trace — otherwise the live runtime describes objects whose code never
// loaded, and the next cycle's tests run against that mixture.

using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadMetadataDeltaTests(BcEngineFixture engine)
{
    private const string ScenarioDir = "al-runner-rad-metadata-delta";

    /// <param name="Entry">The one registry entry this edit is allowed to move.</param>
    /// <param name="Marker">Text that must appear in it afterwards, and must not before.</param>
    private sealed record MetadataEdit(
        string Scenario, string FileName, string Before, string After, string Entry, string Marker);

    /// <summary>
    /// Metadata-visible edits: each changes what BC would read back for that object, so
    /// each must move exactly one registry entry.
    /// </summary>
    private static readonly MetadataEdit[] Edits =
    [
        new("page control added",
            "RadPerfLineList.Page.al",
            "field(HeaderNo; Rec.\"Header No.\") { ApplicationArea = All; }",
            """
            field(HeaderNo; Rec."Header No.") { ApplicationArea = All; }
                            field(HeaderNoAgain; Rec."Header No.") { ApplicationArea = All; }
            """,
            "Page:71001",
            "HeaderNoAgain"),

        new("report column added",
            "RadPerfHeaderReport.Report.al",
            "column(Description; Description) { }",
            """
            column(Description; Description) { }
                        column(DescriptionAgain; Description) { }
            """,
            "Report:71000",
            "DescriptionAgain"),

        new("xmlport element added",
            "RadPerfHeaderXml.XmlPort.al",
            "fieldelement(Description; Header.Description) { }",
            """
            fieldelement(Description; Header.Description) { }
                            fieldelement(DescriptionAgain; Header.Description) { }
            """,
            "XmlPort:71000",
            "DescriptionAgain"),

        // Enumextension values are registered against the BASE enum's id, so this proves
        // an extension edit refreshes its target's merged entry — the metadata equivalent
        // of the tableextension case, where the extension alone re-emits.
        new("enumextension value added",
            "RadPerfStatusExt.EnumExt.al",
            "value(71000; Archived) { Caption = 'Archived'; }",
            """
            value(71000; Archived) { Caption = 'Archived'; }
                value(71001; Retired) { Caption = 'Retired'; }
            """,
            "Enum:71000",
            "71001=Retired"),
    ];

    public static IEnumerable<object[]> MetadataEdits() => Edits.Select(edit =>
        new object[] { edit.Scenario, edit.FileName, edit.Before, edit.After, edit.Entry, edit.Marker });

    [SkippableTheory]
    [MemberData(nameof(MetadataEdits))]
    public void EditingOneObject_RefreshesOnlyItsMetadata(
        string scenario,
        string fileName,
        string before,
        string after,
        string expectedMovedEntry,
        string expectedMarker)
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            Assert.DoesNotContain(
                expectedMarker,
                Rendered(expectedMovedEntry, baseline.Metadata));

            RadFixture.ReplaceExactlyOnce(
                RadFixture.SourceFile(tempRoot, fileName), before, after);

            var delta = baseline.Cycle(tempRoot);
            Assert.False(delta.FullRebuild);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Single(delta.Emit.Sources);

            delta.Commit(
                baseline.Workspace,
                RadFixture.AssembleAndLoad(baseline.Workspace, delta.Emit.Sources));

            var refreshed = MetadataSnapshot.Take();
            Assert.Equal([expectedMovedEntry], MetadataSnapshot.Diff(baseline.Metadata, refreshed));
            Assert.Contains(expectedMarker, Rendered(expectedMovedEntry, refreshed));
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>One deletion per metadata-bearing object kind, plus the whole enum family.</summary>
    public static IEnumerable<object[]> MetadataDeletions()
    {
        yield return ["page", new[] { "RadPerfLineList.Page.al" }, "Page:71001"];
        yield return ["report", new[] { "RadPerfHeaderReport.Report.al" }, "Report:71000"];
        yield return ["xmlport", new[] { "RadPerfHeaderXml.XmlPort.al" }, "XmlPort:71000"];
        // The base enum survives, so its merged entry must simply lose the extension's
        // values rather than disappear.
        yield return ["enumextension", new[] { "RadPerfStatusExt.EnumExt.al" }, "Enum:71000"];
        yield return
        [
            "enum family",
            new[] { "RadPerfStatus.Enum.al", "RadPerfStatusExt.EnumExt.al" },
            "Enum:71000",
        ];
    }

    /// <summary>
    /// A deleted object's metadata must go with it. Nothing recompiles, so the removal is
    /// the ONLY thing the cycle does — if the entry survives, BC can still resolve an
    /// object that no longer exists in the source tree.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(MetadataDeletions))]
    public void DeletingOneObject_DropsOnlyItsMetadata(
        string scenario,
        string[] deletedFiles,
        string expectedMovedEntry)
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            Assert.NotEqual(string.Empty, Rendered(expectedMovedEntry, baseline.Metadata));

            foreach (var file in deletedFiles)
                File.Delete(RadFixture.SourceFile(tempRoot, file));

            var delta = baseline.Cycle(tempRoot);
            Assert.Empty(delta.Emit.Sources);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));

            // Before the commit the metadata is still live, for the same reason the CLR
            // types are: a candidate that fails later must leave the runtime intact.
            Assert.Empty(MetadataSnapshot.Diff(baseline.Metadata, MetadataSnapshot.Take()));

            delta.Commit(baseline.Workspace, assembly: null);

            var after = MetadataSnapshot.Take();
            Assert.Equal([expectedMovedEntry], MetadataSnapshot.Diff(baseline.Metadata, after));
            if (scenario == "enumextension")
                Assert.DoesNotContain("71000=Archived", Rendered(expectedMovedEntry, after));
            else
                Assert.Equal(string.Empty, Rendered(expectedMovedEntry, after));
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The same claim on the fallback path. A warm watch cycle keeps the metadata
    /// registries across reloads (that is what makes an untouched page stay resolvable),
    /// so a cycle that ends in a FULL compile cannot rely on a clean slate either: the
    /// re-emit overwrites every object that still exists and says nothing at all about the
    /// one that was deleted. Deleting a page while adding an id-less object — the
    /// documented full-compile trigger — is the cheapest way to reach that combination.
    /// </summary>
    [SkippableFact]
    public void DeletingOneObject_DropsItsMetadata_EvenWhenTheCycleFallsBackToAFullCompile()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            Assert.NotEqual(string.Empty, Rendered("Page:71001", baseline.Metadata));

            File.Delete(RadFixture.SourceFile(tempRoot, "RadPerfLineList.Page.al"));
            RadFixture.ForceFullCompile(tempRoot);

            var fallback = baseline.Cycle(tempRoot);
            Assert.True(fallback.FullRebuild, "the lever must force a full compile");
            Assert.True(fallback.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, fallback.Emit.Diagnostics));
            Assert.DoesNotContain("RAD Perf Line List", RadFixture.EmittedNames(fallback));

            fallback.Commit(
                baseline.Workspace,
                RadFixture.AssembleAndLoad(baseline.Workspace, fallback.Emit.Sources));

            var after = MetadataSnapshot.Take();
            Assert.Equal(["Page:71001"], MetadataSnapshot.Diff(baseline.Metadata, after));
            Assert.Equal(string.Empty, Rendered("Page:71001", after));
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// An enumextension is the one object kind whose metadata is NOT keyed by its own id:
    /// its values are registered against the base enum it extends, under its own NAME. So
    /// renaming one adds a second registration instead of replacing the first — and since
    /// the merged read keeps the earliest occurrence of each ordinal, the abandoned
    /// registration then WINS. Renaming the extension and its value together is what makes
    /// that visible: the enum must read back the new value, not the one under the old name.
    ///
    /// Asserted on both paths, because the two remove the stale identity in different
    /// places: the delta buffers its writes and clears the old identity first, the full
    /// compile has already written through and clears it at commit.
    /// </summary>
    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenamingAnEnumExtension_LeavesOneRegistration(bool forceFullCompile)
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            // The enum registries are process-wide, keyed by base-enum id, and the merged
            // read keeps the EARLIEST registration of each ordinal — so the sibling case's
            // renamed extension would still be answering for ordinal 71000 here. Production
            // clears them between bundles that cannot be preserved
            // (BcRuntime.ResetForNewBundleReload); this test asserts absolute content
            // rather than a diff, so it needs the same clean start.
            AlEnumMetadataRegistry.RemoveExtension(71000, "RAD Perf Status Ext");
            AlEnumMetadataRegistry.RemoveExtension(71000, "RAD Perf Status Ext Renamed");

            var baseline = RadFixture.Seed(tempRoot);
            Assert.Contains("71000=Archived", Rendered("Enum:71000", baseline.Metadata));

            var extensionFile = RadFixture.SourceFile(tempRoot, "RadPerfStatusExt.EnumExt.al");
            RadFixture.ReplaceExactlyOnce(
                extensionFile, "\"RAD Perf Status Ext\"", "\"RAD Perf Status Ext Renamed\"");
            // Same ordinal, different value name: if the pre-rename registration survives,
            // it is the earlier one and wins the merge, so the enum keeps reading Archived.
            RadFixture.ReplaceExactlyOnce(
                extensionFile,
                "value(71000; Archived) { Caption = 'Archived'; }",
                "value(71000; Retired) { Caption = 'Retired'; }");
            if (forceFullCompile) RadFixture.ForceFullCompile(tempRoot);

            var cycle = baseline.Cycle(tempRoot);
            Assert.Equal(forceFullCompile, cycle.FullRebuild);
            Assert.True(cycle.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, cycle.Emit.Diagnostics));

            cycle.Commit(
                baseline.Workspace,
                RadFixture.AssembleAndLoad(baseline.Workspace, cycle.Emit.Sources));

            var rendered = Rendered("Enum:71000", MetadataSnapshot.Take());
            Assert.Contains("71000=Retired", rendered);
            Assert.DoesNotContain("Archived", rendered);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// One cycle, four metadata-visible edits and one object whose generated C# Roslyn
    /// rejects. The cycle is a unit: nothing loads, so nothing may have been registered.
    ///
    /// This is the transactional gap the emit-time registry writes create — the AL emitter
    /// has already mutated the live runtime by the time the backend fails.
    /// </summary>
    [SkippableFact]
    public void RejectedCandidate_LeaksNoMetadata()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);

            foreach (var edit in Edits)
                RadFixture.ReplaceExactlyOnce(
                    RadFixture.SourceFile(tempRoot, edit.FileName), edit.Before, edit.After);

            // AL-valid, generated C# Roslyn rejects — the same seam RadDeltaWatchTests uses.
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

            var candidate = baseline.Cycle(tempRoot);
            Assert.False(candidate.FullRebuild);
            Assert.True(candidate.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, candidate.Emit.Diagnostics));
            Assert.Equal(Edits.Length + 1, candidate.Emit.Sources.Count);

            var compiled = RadFixture.TryAssemble(baseline.Workspace, candidate.Emit.Sources);
            Assert.False(compiled.Success,
                "the fixture edit no longer produces C# Roslyn rejects; pick another");

            Assert.Equal([], MetadataSnapshot.Diff(baseline.Metadata, MetadataSnapshot.Take()));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string Rendered(string entry, MetadataSnapshot snapshot)
    {
        var parts = entry.Split(':');
        var id = int.Parse(parts[1]);
        var table = parts[0] switch
        {
            "Page" => snapshot.Pages,
            "Report" => snapshot.Reports,
            "XmlPort" => snapshot.XmlPorts,
            "Enum" => snapshot.Enums,
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry, "unknown metadata kind"),
        };
        return table.TryGetValue(id, out var value) ? value : string.Empty;
    }
}
