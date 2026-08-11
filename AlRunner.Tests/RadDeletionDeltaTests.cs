// RadDeletionDeltaTests — deleting an object is a first-class RAD change, not a fallback.
//
// Deletion is the one edit class where "proportional" and "correct" pull in opposite
// directions, and where getting it wrong is invisible:
//
//   * Proportional: removing an object recompiles NOTHING. Microsoft's RAD emit produces
//     zero callbacks, and the merged symbol baseline simply loses that definition — so the
//     cheap outcome is also the right one.
//   * Correct: .NET cannot unload, so the deleted object's CLR type is still loaded from
//     the previous generation. Without a tombstone the runner's type finders resurrect it
//     and a test passes against code that no longer exists in the source tree — a green
//     run for deleted code, which is the worst failure direction there is.
//   * Correct: Microsoft's symbol merger does NOT validate untouched dependents. Deleting
//     an object something still calls produces a clean merge that would silently execute
//     the old loaded type, so the runner has to reject that candidate on AL diagnostics
//     instead of committing it.
//
// Every theory row therefore asserts all three: zero re-emitted objects, the exact removed
// object identities, and the exact CLR names tombstoned — with every surviving object still
// resolving to the identical baseline Type instance.

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RadDeletionDeltaTests(BcEngineFixture engine)
{
    private const string ScenarioDir = "al-runner-rad-deletion-delta";

    /// <summary>
    /// One row per object kind the fixture represents, plus the two multi-object cases:
    /// an enum family (base + extension) and the whole RAD Perf Header closure. Files are
    /// chosen so nothing surviving still references the deleted object — a dangling
    /// reference is a different contract, covered by
    /// <see cref="DeletingAUsedObject_IsRejected_AndCommitsNothing"/>.
    /// </summary>
    public static IEnumerable<object[]> Deletions()
    {
        yield return Case("codeunit leaf",
            ["RadPerfUnrelatedD.Codeunit.al"],
            ["Codeunit:71005"],
            ["Codeunit71005"]);

        yield return Case("tableextension leaf",
            ["RadPerfHeaderExtB.TableExt.al"],
            ["TableExtension:71001"],
            ["TableExtension71001"]);

        yield return Case("page leaf",
            ["RadPerfLineList.Page.al"],
            ["Page:71001"],
            ["Page71001"]);

        yield return Case("pageextension leaf",
            ["RadPerfHeaderCardExt.PageExt.al"],
            ["PageExtension:71000"],
            ["PageExtension71000"]);

        yield return Case("report leaf",
            ["RadPerfHeaderReport.Report.al"],
            ["Report:71000"],
            ["Report71000"]);

        yield return Case("query leaf",
            ["RadPerfHeaderQuery.Query.al"],
            ["Query:71000"],
            ["Query71000"]);

        yield return Case("xmlport leaf",
            ["RadPerfHeaderXml.XmlPort.al"],
            ["XmlPort:71000"],
            ["XmlPort71000"]);

        // Enums and enumextensions carry metadata but emit no CLR object type, so there is
        // nothing to tombstone — the whole removal is a symbol-baseline and metadata event.
        yield return Case("enumextension leaf",
            ["RadPerfStatusExt.EnumExt.al"],
            ["EnumExtension:71000"],
            []);

        yield return Case("enum family",
            ["RadPerfStatus.Enum.al", "RadPerfStatusExt.EnumExt.al"],
            ["Enum:71000", "EnumExtension:71000"],
            []);

        // The dense case: a table plus every object that extends or reads it. Eight of the
        // twenty objects go, and the twelve that remain must not be recompiled.
        yield return Case("table closure",
            [
                "RadPerfHeader.Table.al",
                "RadPerfHeaderExtA.TableExt.al",
                "RadPerfHeaderExtB.TableExt.al",
                "RadPerfHeaderCard.Page.al",
                "RadPerfHeaderCardExt.PageExt.al",
                "RadPerfHeaderReport.Report.al",
                "RadPerfHeaderQuery.Query.al",
                "RadPerfHeaderXml.XmlPort.al",
            ],
            [
                "Page:71000",
                "PageExtension:71000",
                "Query:71000",
                "Report:71000",
                "Table:71000",
                "TableExtension:71000",
                "TableExtension:71001",
                "XmlPort:71000",
            ],
            [
                "Page71000",
                "PageExtension71000",
                "Query71000",
                "Record71000",
                "Report71000",
                "TableExtension71000",
                "TableExtension71001",
                "XmlPort71000",
            ]);
    }

    [Theory]
    [MemberData(nameof(Deletions))]
    public void DeletingUnusedObjects_RecompilesNothing_AndTombstonesExactlyThem(
        string scenario,
        string[] deletedFiles,
        string[] expectedRemovedKeys,
        string[] expectedTombstones)
    {
        if (!engine.Ready)
        {
            Console.Error.WriteLine($"[skip] {engine.SkipReason}");
            return;
        }

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            foreach (var file in deletedFiles)
                File.Delete(RadFixture.SourceFile(tempRoot, file));

            var delta = baseline.Cycle(tempRoot);

            Assert.False(delta.FullRebuild,
                $"{scenario} rebuilt {delta.Emit.Sources.Count} surviving object(s): " +
                string.Join(", ", RadFixture.EmittedNames(delta)));
            Assert.False(delta.NoChange);
            Assert.True(delta.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, delta.Emit.Diagnostics));
            Assert.Empty(delta.Emit.ExcludedObjects);
            Assert.Empty(delta.Emit.Sources);
            Assert.Empty(delta.Changes.Added);
            Assert.Empty(delta.Changes.Modified);
            Assert.Equal(
                expectedRemovedKeys.Order(StringComparer.Ordinal).ToArray(),
                RadFixture.KeyStrings(delta.Changes.Removed));

            // Until the cycle commits, the objects are still live: a deletion candidate
            // that tombstoned at emit time would break a cycle that later fails.
            foreach (var name in expectedTombstones)
            {
                Assert.False(AlObjectResolution.IsTombstoned(name));
                Assert.Same(baseline.Types[name], AlObjectResolution.FindOwned(name, requiredBase: null));
            }

            delta.Commit(baseline.Workspace, assembly: null);

            // No assembly was produced, so no generation was added.
            Assert.Single(baseline.Workspace.Generations);
            foreach (var name in expectedTombstones)
            {
                Assert.True(AlObjectResolution.IsTombstoned(name), $"{name} was not tombstoned");
                Assert.Null(AlObjectResolution.FindOwned(name, requiredBase: null));
            }
            baseline.AssertOwnership(owner: null, moved: expectedTombstones);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// Deleting an object a survivor still calls must fail loudly. RAD rebinds the direct
    /// callers of a removed object precisely so their dangling reference becomes an AL
    /// diagnostic; the alternative — Microsoft's merger accepts the removal, the caller is
    /// never rebound, and the still-loaded old type keeps answering — is a silent green.
    ///
    /// The rejection must also stay cheap and honest. Today it is neither: BC's RAD emit
    /// throws out of code generation on the dangling reference ("Unexpected value 'None' of
    /// type NavTypeKind") instead of reporting it, the runner falls back to a full compile,
    /// and that compile's emit-retry EXCLUDES the caller and its own caller from the module.
    /// So the developer is told "1 broken object unrelated to the rest of the module" twice
    /// rather than "RAD Perf Service is missing" once, and pays a whole-module rebuild for
    /// it. Binding errors are knowable before code generation; this asserts the contract
    /// the reject path should meet.
    /// </summary>
    [Fact]
    public void DeletingAUsedObject_IsRejected_AndCommitsNothing()
    {
        if (!engine.Ready)
        {
            Console.Error.WriteLine($"[skip] {engine.SkipReason}");
            return;
        }

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            // RAD Perf Caller calls RAD Perf Service, which in turn Unrelated A calls.
            File.Delete(RadFixture.SourceFile(tempRoot, "RadPerfService.Codeunit.al"));

            var candidate = baseline.Cycle(tempRoot);

            Assert.False(candidate.NoChange);
            // The reason has to name the object that went missing — that is the whole
            // difference between a usable message and "something broke somewhere".
            Assert.Contains(candidate.Emit.Diagnostics, d =>
                d.Contains("RAD Perf Service", StringComparison.Ordinal));
            // Rejecting a bad edit must not cost a whole-module rebuild…
            Assert.False(candidate.FullRebuild,
                "rejecting a dangling reference fell back to a full compile");
            // …and must not quietly drop the objects it could not bind.
            Assert.Empty(candidate.Emit.ExcludedObjects);
            Assert.Empty(candidate.Emit.Sources);
            Assert.Throws<InvalidOperationException>(
                () => candidate.Commit(baseline.Workspace, assembly: null));

            // Nothing moved: the object is neither tombstoned nor re-owned…
            Assert.False(AlObjectResolution.IsTombstoned("Codeunit71000"));
            baseline.AssertOwnership(owner: null, moved: []);

            // …and the rejected candidate advanced no hashes, so the same deletion is
            // rediscovered — with the same diagnostics — on the next cycle.
            var retry = baseline.Cycle(tempRoot);
            Assert.False(retry.NoChange);
            Assert.Empty(retry.Emit.Sources);
            Assert.Equal(candidate.Emit.Diagnostics, retry.Emit.Diagnostics);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void DeletingThenReadding_RevivesOnlyThatObject()
    {
        if (!engine.Ready)
        {
            Console.Error.WriteLine($"[skip] {engine.SkipReason}");
            return;
        }

        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var baseline = RadFixture.Seed(tempRoot);
            var path = RadFixture.SourceFile(tempRoot, "RadPerfUnrelatedD.Codeunit.al");
            var original = File.ReadAllText(path);

            File.Delete(path);
            var deletion = baseline.Cycle(tempRoot);
            Assert.Empty(deletion.Emit.Sources);
            deletion.Commit(baseline.Workspace, assembly: null);
            Assert.True(AlObjectResolution.IsTombstoned("Codeunit71005"));

            File.WriteAllText(path, original);
            var readd = baseline.Cycle(tempRoot);

            Assert.False(readd.FullRebuild);
            Assert.Equal(["RAD Perf Unrelated D"], RadFixture.EmittedNames(readd));
            Assert.Empty(readd.Changes.Modified);
            Assert.Empty(readd.Changes.Removed);
            // Added, not Modified: proves the committed deletion really left the object
            // map and the merged symbol baseline, rather than only the CLR ownership table.
            Assert.Equal(["Codeunit:71005"], RadFixture.KeyStrings(readd.Changes.Added));

            var assembly = RadFixture.AssembleAndLoad(baseline.Workspace, readd.Emit.Sources);
            readd.Commit(baseline.Workspace, assembly);

            Assert.False(AlObjectResolution.IsTombstoned("Codeunit71005"));
            baseline.AssertOwnership(assembly, ["Codeunit71005"]);
            baseline.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static object[] Case(
        string scenario,
        string[] deletedFiles,
        string[] expectedRemovedKeys,
        string[] expectedTombstones) =>
        [scenario, deletedFiles, expectedRemovedKeys, expectedTombstones];
}
