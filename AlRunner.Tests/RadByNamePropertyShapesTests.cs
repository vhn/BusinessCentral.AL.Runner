// RadByNamePropertyShapesTests — the by-name property shapes, each asserted against a cold
// compile of the identical tree.
//
// These were carried into this plan as the shapes "measured clean", to be pinned as trip-wires.
// RUNNING THEM DISAGREED. Six are clean; three are not, and two of those three are the same bug
// the W4 RED suite reproduces:
//
//     CLEAN (6, trip-wires)          WERE BROKEN (3, now fixed)
//     ------------------------       --------------------------------------------
//     SourceTable                    PageExtension.TargetObject .. was AL0270 the control
//     CalcFormula                        'BystanderMarker' is not found in the target
//     RunObject                      report RelatedTable ......... was AL0118 the name
//     LookupPageId/DrillDownPageId       'Description' does not exist in the current context
//     enum-value Implementation      query RelatedTable .......... was AL0386 a required
//     RoleCenter                         package dependency could not be found
//
// The three on the right are repaired by the bystander-rebind rules in DeltaCompile, and their
// expected object lists now INCLUDE the bystander. The six on the left must not: a rule that
// pulled a bystander into a clean shape would be the cascade this design exists to remove, and
// those six exact-list assertions are what would catch it.
//
// The first two broken shapes are NOT a new family. An untouched pageextension loses the control
// it contributes to a stripped page, and an untouched reportextension loses the column it
// contributes to a stripped report — exactly what an untouched tableextension does to a stripped
// table (AL0132). The unifying rule is not "V's surface names X": a plain by-name pointer
// re-resolves fine against X's supplied syntax, which is why the six on the left pass and why
// TypeDefinition.Subtype passes too. What breaks is surface V holds ONLY BECAUSE X exists.
//
// query RelatedTable's AL0386 looked like a third, unexplained thing — package resolution
// rather than a by-name break. It was not. A query column serializes only its SourceColumn and
// never a type, so the dataitem is the only record of what any column is, and a stripped source
// table takes that with it; the report rule repaired the query with no query-specific code. The
// lesson is about the diagnostic, not the shape: AL0386 named the symptom's neighbourhood
// rather than its cause, and reading it as a distinct defect would have been wrong.
//
// `--watch` strips changed objects from the packaged ModuleDefinition so the new source binds
// (BcCompiler.Rad.cs, `WithoutObjects`). Which OTHER objects get re-emitted is decided by
// `changedSurfaces`, which only admits codeunits and id-less kinds whose serialized surface
// fingerprint moved. Everything else in the app keeps whatever the packaged baseline says
// about it — including surface it holds only because that object exists. Six of the nine
// survive that narrowness unaided; three needed the bystander-rebind rules. Nothing was
// pinning any of them.
//
// WHAT A "CLEAN" RESULT ACTUALLY MEANS HERE, and the measurement behind it.
//
// A trip-wire is only worth its runtime if a broken shape would produce a DIFFERENT result
// from a clean one. So each shape's by-name reference was pointed at a name that does not
// exist and the fixture recompiled cold, to find out whether this pipeline diagnoses the
// break at all:
//
//     SourceTable ................. diagnosed        TableRelation .......... NOT diagnosed
//     CalcFormula ................. diagnosed        Permissions ............ NOT diagnosed
//     RunObject ................... diagnosed        IncludedPermissionSets . NOT diagnosed
//     LookupPageId/DrillDownPageId  diagnosed
//     enum-value Implementation ... diagnosed
//     RoleCenter .................. diagnosed
//     report RelatedTable ......... diagnosed
//     PageExtension.TargetObject .. diagnosed
//     query RelatedTable .......... diagnosed
//
// That measurement is what makes the six clean results meaningful: a broken shape here really
// would look different from a working one.
//
// The three on the right are NOT tested here, deliberately. A dangling `TableRelation` — to a
// missing table OR to a missing field of a present one — compiles silently, and so does a
// `Permissions` line naming a table that does not exist and an `IncludedPermissionSets` naming
// a set that does not exist. A cold compile therefore cannot tell a surviving reference from a
// destroyed one, so `delta == cold == no diagnostics` would go green either way. That is a test
// that asserts nothing dressed up as coverage, which is worse than the acknowledged gap. Their
// objects stay in the fixture (they document the shape and cost nothing), but no [Fact] claims
// to prove them. See the report/PR for what a real oracle for those would need.
//
// The query shape IS testable — cold-compiling the fixture proves the query codegens on the full
// path, and pointing its dataitem at a missing table proves the reference is really bound. It was
// broken, and the report rule fixed it; see the header for why its AL0386 was misleading.

using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// <para><b>Every scenario is a THREE-object triple, and that is the whole design.</b>
/// <b>X</b> is the object the delta strips out of the packaged baseline. <b>V</b> is UNTOUCHED,
/// so it is resolved from that stripped baseline and its serialized surface NAMES X. <b>W</b> is
/// in the same delta and binds to the part of V's surface that names X.</para>
///
/// <para>A two-object fixture — edit X, edit W, no bystander — may never query the damaged
/// representation at all: W binds against X's own freshly supplied syntax and the cycle is green
/// whether or not the shape works. That is the failure mode this whole file exists to avoid, and
/// it is why "clean" is asserted through a bystander rather than directly. The same rule is
/// stated on <see cref="RadByName"/>, which every test here goes through.</para>
///
/// <para><b>The oracle is a cold compile of the identical tree</b>, never a hand-written expected
/// list — a delta must accept and reject exactly what a full compile of the same source accepts
/// and rejects. On top of that, the cold compile of the EDITED tree is asserted to be clean in
/// its own right, so "delta == cold == both broken" cannot pass as success.</para>
///
/// <para><b>How sharp each W is, honestly.</b> Some of these bind a MEMBER of V whose very
/// existence depends on the by-name reference resolving — `SetTableView`'s parameter type is the
/// page's `SourceTable`; `CalcFields` is only legal against a FlowField whose `CalcFormula`
/// resolved; a report column's source expression is resolved against the dataitem's table; an
/// enum-to-interface assignment is the one construct that consumes `Implementation`; a query
/// column's type comes from the source field. Those are sharp: a degraded V produces a real
/// diagnostic. Others — `LookupPageId`/`DrillDownPageId` and `RoleCenter` — are OBJECT-level
/// properties with no member surface to degrade, so the strongest W the language offers is one
/// that merely forces V to resolve (`Record V`, `profileextension extends V`). Those are blunter,
/// and the per-test comments say so rather than letting the suite look uniformly sharp.</para>
///
/// <para>The change set is asserted exactly, on every test, for a reason that is not bookkeeping:
/// if a widening rule ever drags V into the delta, V gets recompiled FROM SOURCE and stops being
/// a bystander — the test would then pass while proving nothing. Naming the modified and emitted
/// objects is what makes that visible instead of silent.</para>
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class RadByNamePropertyShapesTests(BcEngineFixture engine)
{
    private const string FixtureName = "RadByNamePropertyShapes";
    private const string ModuleName = "RAD ByName Property Shapes";
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000015");

    /// <summary>
    /// Generated C# sources the fixture produces, which is what <c>seed.Emit.Sources</c> counts —
    /// NOT the number of declared objects. The interface, the profile and the profileextension
    /// contribute symbols and metadata and no code at all, so 38 declarations emit 35 sources.
    /// </summary>
    private const int EmittedObjectCount = 35;

    /// <summary>
    /// `SourceTable` — a page names its record type by name. W calls `SetTableView`/`SetRecord`
    /// on the bystander page, and those parameters ARE its `SourceTable`: if the reference did
    /// not survive the strip, the overload W is calling would not be there to bind.
    /// </summary>
    [SkippableFact]
    public void SourceTable_SurvivesStrippingTheTableItNames()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        Shape(
            "SourceTable",
            tempRoot =>
            {
                Edit(tempRoot, "SourceTableTarget.Table.al", "'sourcetable-v1'", "'sourcetable-v2'");
                Edit(tempRoot, "SourceTableCaller.Codeunit.al", "'caller-v1'", "'caller-v2'");
            },
            modified: ["BN SourceTable Caller", "BN SourceTable Target"],
            emitted: ["BN SourceTable Caller", "BN SourceTable Target"]);
    }

    /// <summary>
    /// `CalcFormula` — a FlowField names the table it counts/sums by name. `CalcFields` is only
    /// legal against a field that IS a FlowField, so W's call cannot bind unless both of the
    /// bystander's formulas still resolve against the stripped table.
    /// </summary>
    [SkippableFact]
    public void CalcFormula_SurvivesStrippingTheTableItNames()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        Shape(
            "CalcFormula",
            tempRoot =>
            {
                Edit(tempRoot, "CalcFormulaLine.Table.al", "Amount := 1;", "Amount := 2;");
                Edit(tempRoot, "CalcFormulaCaller.Codeunit.al", "'calc-v1'", "'calc-v2'");
            },
            modified: ["BN CalcFormula Caller", "BN CalcFormula Line"],
            emitted: ["BN CalcFormula Caller", "BN CalcFormula Line"]);
    }

    /// <summary>
    /// `RunObject` — a page action names the object it runs by name. W is a pageextension whose
    /// `modify` names that very action, so resolving the modify target is what pulls the action —
    /// by-name reference included — out of the bystander's packaged definition.
    /// </summary>
    [SkippableFact]
    public void RunObject_SurvivesStrippingThePageItNames()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        Shape(
            "RunObject",
            tempRoot =>
            {
                Edit(tempRoot, "RunObjectTarget.Page.al", "Marker := 1;", "Marker := 2;");
                Edit(tempRoot, "RunObjectHostExt.PageExt.al", "Visible = true;", "Visible = false;");
            },
            modified: ["BN RunObject Host Ext", "BN RunObject Target"],
            emitted: ["BN RunObject Host Ext", "BN RunObject Target"]);
    }

    /// <summary>
    /// `LookupPageId`/`DrillDownPageId` — a table names a page by name, twice.
    ///
    /// <para>One of the two blunt ones. These are OBJECT-level properties: they contribute no
    /// member for a W to aim at, the way `CalcFormula` contributes a FlowField, so the strongest
    /// available W simply declares `Record V` and forces the bystander's whole definition — both
    /// page names included — to resolve out of the packaged baseline. Stated plainly rather than
    /// dressed up: this test would not notice a degradation that left V's fields intact.</para>
    /// </summary>
    [SkippableFact]
    public void LookupPageId_SurvivesStrippingThePageItNames()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        Shape(
            "LookupPageId/DrillDownPageId",
            tempRoot =>
            {
                Edit(tempRoot, "LookupPagePage.Page.al", "'lookup-v1'", "'lookup-v2'");
                Edit(tempRoot, "LookupPageCaller.Codeunit.al", "'lookup-caller-v1'", "'lookup-caller-v2'");
            },
            modified: ["BN LookupPage Caller", "BN LookupPage Page"],
            emitted: ["BN LookupPage Caller", "BN LookupPage Page"]);
    }

    /// <summary>
    /// enum-value `Implementation` — an enum value names the codeunit that implements the
    /// interface for it. Assigning the enum to an interface variable is the ONE AL construct that
    /// consumes that property, so W binds precisely the part of the bystander's surface that
    /// names the stripped codeunit.
    ///
    /// <para>Only the codeunit's BODY is edited. A codeunit whose serialized surface moves is
    /// admitted to `changedSurfaces` and its direct users are rebound — which could drag the enum
    /// into the same delta, and a bystander recompiled from source proves nothing. The exact
    /// change set asserted below is what catches that if it ever starts happening.</para>
    /// </summary>
    [SkippableFact]
    public void EnumValueImplementation_SurvivesStrippingTheCodeunitItNames()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        Shape(
            "enum-value Implementation",
            tempRoot =>
            {
                Edit(tempRoot, "ImplAlpha.Codeunit.al", "exit(145);", "exit(146);");
                Edit(tempRoot, "ImplCaller.Codeunit.al", "+ 1);", "+ 2);");
            },
            modified: ["BN Impl Alpha", "BN Impl Caller"],
            emitted: ["BN Impl Alpha", "BN Impl Caller"]);
    }

    /// <summary>
    /// `RoleCenter` — a profile names its role-centre page by name.
    ///
    /// <para>This is a SHIPPED branch nothing was pinning. RadProfileApp has had a profile with a
    /// `RoleCenter` since the id-less-object work, but no test ever edits the page it names, so
    /// the delta path's behaviour when that page is stripped had never been exercised.</para>
    ///
    /// <para>The other blunt one, for the same reason as `LookupPageId`: `RoleCenter` is an
    /// object-level property. A profileextension is the only AL construct that binds a profile at
    /// all, so `extends V` is the tightest W the language permits here — it forces the bystander
    /// profile out of the packaged baseline, and no further.</para>
    /// </summary>
    [SkippableFact]
    public void RoleCenter_SurvivesStrippingThePageItNames()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        Shape(
            "RoleCenter",
            tempRoot =>
            {
                Edit(tempRoot, "RoleCenterPage.Page.al", "'rolecenter-v1'", "'rolecenter-v2'");
                Edit(tempRoot, "RoleCenterExt.ProfileExt.al", "'rolecenter-ext-v1'", "'rolecenter-ext-v2'");
            },
            // The profileextension is id-less: it is MODIFIED and emits no C# whatsoever, so the
            // emitted list is the page alone. A delta that emitted two here would mean an
            // unrelated object was dragged in.
            modified: ["BN RoleCenter Ext", "BN RoleCenter Page"],
            emitted: ["BN RoleCenter Page"]);
    }

    /// <summary>
    /// report `RelatedTable` — a report dataitem names its source table by name, which is what the
    /// serialized dataitem definition calls `RelatedTable`. W is a reportextension adding a column
    /// whose source expression is resolved AGAINST that table: the only way the compiler knows
    /// which table that is, is the bystander's by-name reference.
    /// </summary>
    [SkippableFact]
    public void ReportRelatedTable_SurvivesStrippingTheTableItNames()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        Shape(
            "report RelatedTable",
            tempRoot =>
            {
                Edit(tempRoot, "ReportTable.Table.al", "'report-table-v1'", "'report-table-v2'");
                Edit(tempRoot, "ReportHostExt.ReportExt.al",
                    "column(BnReportExtraV1;", "column(BnReportExtraV2;");
            },
            // The report itself joins the delta: its dataitem is the only record of what its
            // columns are, so a stripped source table takes the column definitions with it. See
            // the pageextension case above for why a bystander in this list is the fix.
            modified: ["BN Report Host", "BN Report Host Ext", "BN Report Table"],
            emitted: ["BN Report Host", "BN Report Host Ext", "BN Report Table"]);
    }

    /// <summary>
    /// `PageExtension.TargetObject` — a pageextension names the page it extends by name. The
    /// bystander's control only reaches the target page through that reference, and W modifies
    /// exactly that control, so W cannot bind unless the bystander's `TargetObject` still
    /// resolves against the page this delta stripped.
    /// </summary>
    [SkippableFact]
    public void PageExtensionTargetObject_SurvivesStrippingThePageItNames()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        Shape(
            "PageExtension.TargetObject",
            tempRoot =>
            {
                Edit(tempRoot, "PageExtBase.Page.al", "'pageext-page-v1'", "'pageext-page-v2'");
                Edit(tempRoot, "PageExtCaller.PageExt.al", "Visible = true;", "Visible = false;");
            },
            // The bystander IS expected in the delta here, unlike the six clean shapes. That is
            // not a relaxed assertion — it is the repair. A pageextension's control lives on its
            // target, so once the target is stripped and rebuilt from syntax the control is only
            // recoverable by rebinding the extension from source too. A shape that is broken and
            // a shape that is clean want opposite things from this list, and which one applies is
            // decided by measurement, not by symmetry.
            modified: ["BN PageExt Bystander", "BN PageExt Caller", "BN PageExt Page"],
            emitted: ["BN PageExt Bystander", "BN PageExt Caller", "BN PageExt Page"]);
    }

    /// <summary>
    /// query `RelatedTable` — a query dataitem names its source table by name. W reads two columns
    /// into locals of the SOURCE FIELDS' exact types, so the assignments only type-check if the
    /// compiler can still follow the bystander's dataitem back to the stripped table.
    ///
    /// <para>This is the shape the original probe could not settle: it reported AL0386 because it
    /// put the query in the delta, and a reference-free probe cannot codegen one. Here the query
    /// is the bystander, so the delta never code-generates it — see the file header.</para>
    /// </summary>
    [SkippableFact]
    public void QueryRelatedTable_SurvivesStrippingTheTableItNames()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        Shape(
            "query RelatedTable",
            tempRoot =>
            {
                Edit(tempRoot, "QueryTable.Table.al", "'query-table-v1'", "'query-table-v2'");
                Edit(tempRoot, "QueryCaller.Codeunit.al", "'query-caller-v1'", "'query-caller-v2'");
            },
            // Same rule as the report, and the same fix repaired both: a query column serializes
            // only its SourceColumn and no type, so the dataitem is the only record of what the
            // column is. This is why the AL0386 this test used to report was never a separate
            // defect — it was the report break wearing a package-resolution diagnostic.
            modified: ["BN Query Caller", "BN Query Host", "BN Query Table"],
            emitted: ["BN Query Caller", "BN Query Host", "BN Query Table"]);
    }

    /// <summary>
    /// Seed a committed baseline, apply the shape's edits to X and W, and assert the cycle is a
    /// delta that says exactly what a cold compile of the same tree says.
    ///
    /// <para><paramref name="modified"/> and <paramref name="emitted"/> are asserted exactly
    /// rather than by count, because the interesting failure is a widening rule pulling the
    /// BYSTANDER into the delta: that recompiles it from source, and every remaining assertion
    /// then passes while testing nothing.</para>
    /// </summary>
    private static void Shape(
        string shape,
        Action<string> edit,
        string[] modified,
        string[] emitted)
    {
        RadByName.Run(FixtureName, ModuleName, AppId, EmittedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                edit(tempRoot);

                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);

                Assert.False(delta.FullRebuild,
                    $"{shape}: the cycle rebuilt the whole module instead of deltaing X and W");

                // The oracle FIRST. The diagnostics path returns a result whose Changes is
                // RadChangeSet.Empty and whose Sources are empty, so asserting the object lists
                // before this reports "expected two objects, got none" for what is really a
                // binding failure — the AL diagnostic naming the broken reference is the
                // evidence, and it must not be hidden behind a count mismatch.
                //
                // "delta == cold" is only evidence when the cold side is genuinely clean: a tree
                // both paths reject identically would otherwise read as a surviving reference.
                var cold = RadByName.ColdCompile(tempRoot, ModuleName);
                Assert.True(cold.Emit.Diagnostics.Count == 0,
                    $"{shape}: the edited tree does not compile cold, so 'delta == cold' says "
                    + "nothing about the shape:" + Environment.NewLine
                    + string.Join(Environment.NewLine, cold.Emit.Diagnostics));

                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);

                // Then precision: the interesting REGRESSION once a widening rule exists is that
                // rule pulling the bystander into the delta, which recompiles it from source and
                // makes every assertion above pass while testing nothing.
                Assert.Equal(modified,
                    delta.Changes.Modified.Select(item => item.Name)
                        .Order(StringComparer.Ordinal).ToArray());
                Assert.Empty(delta.Changes.Added);
                Assert.Empty(delta.Changes.Removed);
                Assert.Equal(emitted, RadFixture.EmittedNames(delta));
            });
    }

    private static void Edit(string tempRoot, string file, string before, string after) =>
        RadByName.Replace(RadByName.SourceFile(tempRoot, file), before, after);
}
