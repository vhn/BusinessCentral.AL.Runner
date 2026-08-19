// RadByNameTableNoRenameTests — pins the TableNo-bystander rule
// (`ModuleDefinitionOps.CodeunitsWithTableNo`, driven from `BcCompiler.DeltaCompile`)
// against the one shape no fixture has ever exercised: a table RENAME, not a modification or
// a removal.
//
// The rule: every delta collects, for each table it strips from the packaged baseline, BOTH
// the object's current name and its committed (pre-edit) one — see `StrippedTableNames` in
// `BcCompiler.Rad.cs` — because a RENAMED table keeps its `RadObjectKey` (an id'd
// object's key is (Kind, Id), never its name), so it arrives in `modified`, not as a
// remove-then-add. `item.Name` (parsed from the edited source) reports the NEW name;
// `ws.Object(item.Key)` (the committed baseline) still reports the OLD one.
// `ModuleDefinitionOps.CodeunitsWithTableNo` then scans the PACKAGED (pre-edit) baseline for a
// codeunit whose `TableNo` matches either name. Only the OLD name can ever match there, because
// a bystander codeunit's packaged `TableNo` was serialized before the rename happened — so
// matching only the new name would silently stop finding it.
//
// The three objects, mirroring RadByNameHarness's own contract (X stripped ∧ V untouched ∧ W
// in the same delta, or the shape proves nothing):
//   X = table "Rename Target" (id 72100) — the edit renames it, keeping the id.
//   V = codeunit "Rename Bystander" — `TableNo = 72100;` by ID, not by name, so its own file
//       can stay byte-for-byte untouched across the rename. A by-name TableNo here would leave
//       V's unedited source naming an identifier the rename just retired, which breaks V for a
//       reason unrelated to the rule under test.
//   W = codeunit "Rename Caller" — calls `Bystander.Run(Target)`, which only binds while V
//       still exposes `Run(Record)`. Edited in the same delta as X: a variable's type is not an
//       id reference, so W's own reference to the table has to follow the rename, alongside an
//       unrelated body-literal change.

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// What a PASS proves: the shipped "under BOTH names" rule holds under a rename, not just under
/// a modification/removal. V gets pulled into the delta as a bystander, rebinds against the
/// table's new name, and the delta re-emits all three objects with zero diagnostics — matching
/// a cold compile of the identical, fully-renamed tree exactly.
///
/// <para>What a FAIL would mean: a seventh bug. If the rule only matched the new name, V's
/// packaged `TableNo` (still the OLD name — V was never rebound to learn the new one) would
/// never match, V would stay out of the delta, and it would be reconstructed from a packaged
/// baseline that no longer has ANY table named "Rename Target" (X is stripped from `packaged`
/// by key regardless of the rule under test). V comes back without `Run(Record)`, and W's
/// `Bystander.Run(Target)` fails to bind — AL0126, on a file a cold compile accepts cleanly.</para>
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class RadByNameTableNoRenameTests(BcEngineFixture engine)
{
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000014");
    private const string ModuleName = "RAD ByName TableNo Rename";

    /// <summary>
    /// Rename X, edit W in the same delta, never touch V — then require the delta to say
    /// exactly what a cold compile of the identical renamed tree says, AND to have actually
    /// rebound V from source rather than merely avoiding a diagnostic by coincidence.
    /// </summary>
    [SkippableFact]
    public void RenamingTheTableNoTarget_StillBindsTheBystandersRunOverload()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            "RadByNameTableNoRename",
            ModuleName,
            AppId,
            expectedObjectCount: 3,
            scenario: (compiler, workspace, tempRoot) =>
            {
                var tablePath = RadByName.SourceFile(tempRoot, "RenameTarget.Table.al");
                var callerPath = RadByName.SourceFile(tempRoot, "RenameCaller.Codeunit.al");

                // The rename itself: X keeps id 72100, only its quoted name changes. Both
                // textual occurrences of the old name must follow — X's own header, and W's
                // local variable declaration (V's reference is by id, so it has none to fix).
                RadByName.Replace(tablePath, "\"Rename Target\"", "\"Rename Target Renamed\"");
                RadByName.Replace(callerPath, "\"Rename Target\"", "\"Rename Target Renamed\"");
                // An unrelated body change, so W is "modified" for a reason beyond the rename
                // follow-up alone too — the shape the shipped comment describes.
                RadByName.Replace(callerPath, "\"Entry No.\" := 1;", "\"Entry No.\" := 2;");

                var cold = RadByName.ColdCompile(tempRoot, ModuleName);
                // Positive and specific, not "happens to be empty": the fully-renamed tree is
                // legal AL entirely on its own, with no help from the delta path at all. If
                // this fails, the fixture is broken, not the rule under test.
                Assert.True(cold.Emit.Diagnostics.Count == 0,
                    "cold compile of the renamed tree must be clean: " +
                    string.Join(Environment.NewLine, cold.Emit.Diagnostics));

                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);

                Assert.False(delta.FullRebuild);
                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);

                // The trip-wire itself. Zero diagnostics on both sides is necessary but not
                // sufficient — a delta that silently failed to rebind V would ALSO report zero
                // diagnostics on this minimal fixture (nothing here forces V's dangling
                // reconstruction to surface as an error the way it would on a richer app), so
                // "matches cold" alone cannot tell a healthy pass from a vacuous one. The object
                // set can: PASS means the delta re-emitted all three objects — X and W because
                // they were edited, and V, untouched, because the TableNo rule pulled it in as
                // a bystander. A rule that matched only the new table name would leave this at
                // two (X, W only), because V's packaged TableNo never says the new name.
                Assert.Equal(
                    new[] { "Rename Bystander", "Rename Caller", "Rename Target Renamed" },
                    delta.Emit.Sources
                        .Select(source => source.Name)
                        .Order(StringComparer.Ordinal)
                        .ToArray());
            });
    }
}
