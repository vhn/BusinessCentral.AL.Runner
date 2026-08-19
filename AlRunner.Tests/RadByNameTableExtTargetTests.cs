using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// RAD delta compilation must rebind a <c>tableextension</c>'s <c>TargetObject</c> by-name
/// reference when the table it names is stripped from the packaged baseline. It currently does
/// not: the trigger that decides which OTHER, untouched objects get pulled into a delta
/// (<c>changedSurfaces</c>, in <c>BcCompiler.DeltaCompile</c>) only admits codeunits and id-less
/// kinds whose serialized surface fingerprint moved. A modified table is never a member of that
/// set, so an untouched tableextension whose serialized surface names it is never rebound —
/// even though the table itself, being a table and not an extension, WAS just stripped out of
/// the packaged module definition by <c>ModuleDefinitionOps.WithoutObjects</c>.
///
/// <para><b>Why this needs three objects, not two.</b> The damage is only observable through a
/// bystander that reads the broken representation, which is why every fixture in this family
/// (see <see cref="RadByName"/>) is built from an X/V/W triple:</para>
///
/// <list type="bullet">
/// <item><b>X</b> — <c>table 72060 "ExtTarget Base"</c>. The edit adds a field to it, so X is
///   MODIFIED. Being a table rather than an extension, <c>RadObjectKey.IsExtension</c> is false
///   for it, so it is NOT exempt from stripping: its entry is removed from the packaged module
///   definition and its symbol is rebuilt fresh from the supplied source instead. That part
///   works fine on its own — the damage falls on whoever still names "ExtTarget Base" by NAME
///   against the (now X-less) packaged baseline rather than holding a live reference to it.</item>
/// <item><b>V</b> — <c>tableextension 72061 "ExtTarget Ext" extends "ExtTarget Base"</c>.
///   Deliberately left UNTOUCHED by the edit, so it never enters <c>modified</c>/<c>removed</c>
///   at all, and its serialized <c>TargetObject</c> ("ExtTarget Base") is read as-is out of the
///   packaged baseline rather than recompiled from its own (unchanged) source. That packaged
///   baseline no longer has an entry named "ExtTarget Base" — X was just stripped from it — so
///   V's by-name target reference cannot resolve there any more. Nothing widens this delta to
///   recompile V from source instead, because <c>changedSurfaces</c> does not admit tables, so
///   <c>DirectUsersOf</c> is never even asked about X, and V — the one object whose surface
///   actually names X — is never pulled in. V's field never attaches to X's rebuilt symbol as a
///   result.</item>
/// <item><b>W</b> — <c>codeunit 72062 "ExtTarget Consumer"</c>, which reads <c>R."Ext Value"</c>
///   (V's field) off a <c>Record "ExtTarget Base"</c> (X). W must ALSO be in this same delta —
///   it gets an unrelated body edit — or the broken binding is never exercised: left untouched,
///   W keeps whatever CLR type it already had, and the delta never asks the RAD emitter to
///   rebind its body at all this cycle. Both the delta and a cold compile would then report
///   zero diagnostics for entirely different reasons, and the fixture would go green having
///   proven nothing.</item>
/// </list>
///
/// <para>Drop any one of the three and the fixture stops being evidence: without V there is no
/// by-name reference to break; without W nothing in this delta ever queries V's damaged
/// representation; without X nothing gets stripped in the first place.</para>
///
/// <para><b>Provenance.</b> A <c>TargetObject</c> property is how BOTH a <c>tableextension</c>
/// and an <c>enumextension</c> serialize their target — same property name on both extension
/// definitions — so a static count over the npcore corpus counted them together: 193 combined
/// occurrences of a serialized <c>TargetObject</c> naming a table or an enum. This fixture pins
/// the tableextension half; the enumextension half is a sibling fixture of its own, since the
/// two extension kinds still need separate AL syntax to reproduce.</para>
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class RadByNameTableExtTargetTests(BcEngineFixture engine)
{
    private const string FixtureName = "RadByNameTableExtTarget";
    private const string ModuleName = "RAD ByName TableExt Target";
    private const int ExpectedObjectCount = 3;
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000012");

    /// <summary>
    /// RED: stripping table X (the edit adds a field to it) while leaving tableextension V
    /// untouched must still let codeunit W's <c>R."Ext Value"</c> read bind — exactly as a cold
    /// compile of the identical, already-edited tree does. Today the delta reports AL0132
    /// against that read instead, because V's <c>TargetObject</c> can no longer resolve
    /// "ExtTarget Base" once X is stripped out of the packaged baseline, and nothing widens the
    /// delta to rebind V and repair that.
    /// </summary>
    [SkippableFact]
    public void StrippingTheTargetTable_StillLetsTheExtensionsFieldBind()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(
            FixtureName,
            ModuleName,
            AppId,
            ExpectedObjectCount,
            (compiler, workspace, tempRoot) =>
            {
                // X: add a field. X is a table, not an extension, so RadObjectKey.IsExtension
                // is false and this modification strips X from the packaged baseline
                // (ModuleDefinitionOps.WithoutObjects). X's own symbol still compiles fine from the
                // freshly supplied source — the damage lands on V, which names X BY NAME.
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, "ExtTargetBase.Table.al"),
                    "field(2; Description; Text[50]) { DataClassification = SystemMetadata; }",
                    "field(2; Description; Text[50]) { DataClassification = SystemMetadata; }\n"
                    + "        field(3; Marker; Integer) { DataClassification = SystemMetadata; }");

                // W: an unrelated body edit, so it enters `modified` too and the RAD emitter
                // actually rebinds its body THIS cycle. Without this, W keeps its
                // previously-compiled type untouched, the delta never asks anything to rebind
                // its `R."Ext Value"` read at all, and both cold and delta would report zero
                // diagnostics for unrelated reasons -- the two-object trap RadByName's own
                // summary warns about, generalized to "the third object went untouched".
                RadByName.Replace(
                    RadByName.SourceFile(tempRoot, "ExtTargetConsumer.Codeunit.al"),
                    "exit(R.\"Ext Value\");",
                    "exit(R.\"Ext Value\" + 1);");

                // V (tableextension 72061) is deliberately left untouched -- see the class
                // summary for why it has to be the bystander rather than a participant.

                var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);

                // Positive control, asserted BEFORE the oracle comparison below: the EDITED
                // tree is legal AL on its own terms (X has its new field, V still extends it,
                // W's read still targets a field that exists) so if the delta disagrees, the
                // delta invented the diagnostic -- it is not that this fixture failed to compile.
                // Asserting this first means a failure here points at a broken fixture, and a
                // failure at AssertMatchesColdCompile below unambiguously points at the delta.
                var cold = RadByName.ColdCompile(tempRoot, ModuleName);
                Assert.True(cold.Emit.Diagnostics.Count == 0,
                    "the cold compile of the edited tree is not clean, so this fixture does " +
                    "not isolate the bug:" + Environment.NewLine
                    + string.Join(Environment.NewLine, cold.Emit.Diagnostics));

                Assert.False(delta.FullRebuild);

                // The oracle -- expected to fail today. The delta reports AL0132 against W's
                // `R."Ext Value"` (V's field never re-attached to X's rebuilt symbol) while the
                // cold compile just above proved the identical, fully-edited tree reports
                // nothing at all.
                RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);
            });
    }
}
