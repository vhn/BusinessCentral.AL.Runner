// RadByNameInterfaceEnumTests — RAD delta compilation and an ENUM's `ImplementedInterfaces`.
//
// Plan Task 11 in `.context/plans/2026-08-16-delta-compile-correctness.md` (section W4,
// "by-name references"). The sibling shape, Task 10, is the same bug on a CODEUNIT's
// `ImplementedInterfaces`; this file is the ENUM half, which the plan calls out as its own
// task because `BcCompiler.Rad.cs:754-776`'s `changedSurfaces` gate treats the two kinds
// differently — see the class doc below.

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// RED: an untouched ENUM that <c>implements</c> an interface must keep binding to that
/// interface across a delta that edits the interface — and does not, because a modified
/// <c>Enum</c> never enters <c>changedSurfaces</c> at all.
///
/// <para><b>The mechanism.</b> <c>BcCompiler.DeltaCompile</c> strips every MODIFIED,
/// non-extension object from the packaged <c>ModuleDefinition</c> so the newly supplied
/// source binds in its place (<c>BcCompiler.Rad.cs:620-624</c>) — an <c>interface</c>
/// included, since <c>RadObjectKey.IsExtension</c> only exempts kinds whose name ends in
/// "Extension". Which OTHER objects get pulled back into the same delta on the stripped
/// object's behalf is decided by <c>changedSurfaces</c> (<c>:754-776</c>), and that gate
/// admits only two kinds:
/// <code>
/// .Where(item =&gt; item.Key.IsCodeunit || RadObjectKey.IsIdlessKind(item.Key.Kind))
/// </code>
/// An <c>Enum</c> is neither a codeunit nor an id-less kind (<c>RadObjectKey.IsIdlessKind</c>
/// lists exactly six kinds, and <c>"Enum"</c> is not one of them — it has a real object id).
/// So an untouched enum that names a stripped interface in its own serialized
/// <c>ImplementedInterfaces</c> is never re-emitted and never re-validated: it just sits in
/// the packaged baseline exactly as it compiled last time, still carrying a reference to an
/// interface the packaged module definition no longer has an entry for at all — because that
/// entry was stripped out from under it by the very same delta.</para>
///
/// <para><b>The X/V/W triple, and why a two-object fixture would prove nothing.</b> Every
/// by-name shape in this plan section needs three objects on three different sides of one
/// delta, or the fixture can never exercise the bystander's damaged representation at all:</para>
/// <list type="bullet">
/// <item><b>X</b> = <c>"ByName Enum Contract"</c>, the interface
/// (<c>ByNameEnumContract.Interface.al</c>). Every test in this class edits its file, so it
/// lands in <c>modified</c>, gets stripped from the packaged module, and is supplied fresh
/// from the syntax tree in its place.</item>
/// <item><b>V</b> = <c>"ByName Kind"</c>, the enum (<c>ByNameKind.Enum.al</c>). NEVER edited
/// by any test in this class — that is the whole point. Because it is untouched, its symbol
/// is reconstructed from the packaged baseline rather than recompiled, so the packaged
/// baseline's copy of "this enum implements X" is the ONLY copy this delta has of that fact.
/// If V were edited too it would be recompiled from source exactly like X, the by-name
/// reference would resolve the ordinary way, and there would be nothing here to prove — the
/// bug is specific to the packaged, unedited copy shadowing (or failing to resolve against)
/// the edit.</item>
/// <item><b>W</b> = <c>"ByName Enum Consumer"</c> (<c>ByNameEnumConsumer.Codeunit.al</c>), a
/// codeunit edited (body-only) in the SAME delta as X. Its <c>C := K;</c> enum-to-interface
/// cast is what forces the compiler to actually resolve V's <c>ImplementedInterfaces</c> entry
/// for X during this compile — without a consumer performing the cast, a broken by-name link
/// sitting on an untouched, never-recompiled enum could go unobserved indefinitely.</item>
/// </list>
/// <para>A fourth object, <c>"ByName Enum Impl Alpha"</c>
/// (<c>ByNameEnumImplAlpha.Codeunit.al</c>), exists only because the AL enum-as-interface-
/// factory pattern requires one: V's <c>Alpha</c> value needs a concrete codeunit for its
/// <c>Implementation</c> property to name. It is never edited by any test either. The plan's
/// requirement is "at least three" objects on three sides of the delta, not "exactly three" —
/// a fourth bystander that is itself untouched does not weaken the proof.</para>
///
/// <para><b>Why this is a method-body diagnostic.</b> <c>BcCompiler.Rad.cs:646</c> asks only
/// <c>GetDeclarationDiagnostics()</c> before code generation. A broken by-name
/// <c>ImplementedInterfaces</c> reference does not surface there — it surfaces once the
/// emitter actually generates the interface dispatch for <c>C := K;</c>, which happens
/// through <c>rad.Emit(...)</c> at <c>:682-687</c>. Declaration-only diagnostics are silent
/// on this bug by construction.</para>
///
/// <para><b>Occurrence count: 47.</b> Per the plan's measured damage matrix (Task 11 row), a
/// replication of <c>MapObjectReferences</c> over npcore's real object graph found 47 enum
/// objects whose serialized <c>ImplementedInterfaces</c> names an interface — the population
/// this shape is drawn from. That is far smaller than the sibling codeunit shape (Task 10,
/// 626 occurrences) or the dominant shape in this plan section (Task 9,
/// <c>TypeDefinition.Subtype</c>, 13,610 occurrences), but every one of the 47 is a delta that
/// can silently disagree with a cold compile of the same tree the moment the interface an
/// enum implements is edited without the enum itself being touched in the same cycle — exactly
/// the shape a real `--watch` session produces when a developer edits an interface and nothing
/// else.</para>
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class RadByNameInterfaceEnumTests(BcEngineFixture engine)
{
    private const string FixtureName = "RadByNameInterfaceEnum";
    private const string ModuleName = "RAD ByName Interface Enum";
    private static readonly Guid AppId = Guid.Parse("b1000000-0000-4000-8000-000000000011");

    /// <summary>
    /// <c>seed.Emit.Sources.Count</c> from the fixture's first (full) compile — a count of
    /// how many objects emit C#, not of how many objects are declared
    /// (<see cref="RadByName.Run"/> asserts this against the untouched fixture). The fixture
    /// declares four objects, but the interface X is id-less and
    /// <see cref="RadObjectKey.EmitsCode"/> is false for it — it contributes symbols to the
    /// module and no generated type at all. Only the other three emit code: the enum V, the
    /// codeunit backing its <c>Alpha</c> value's <c>Implementation</c>, and the consumer
    /// codeunit W.
    /// </summary>
    private const int ExpectedObjectCount = 3;

    /// <summary>
    /// RED. Edits X (the interface) and W (the consumer) in one delta; V (the enum) is never
    /// touched — see the class doc for why that split is exactly three objects wide.
    ///
    /// <para>Asserts, in this order and for this reason:</para>
    /// <list type="number">
    /// <item><c>delta.FullRebuild</c> is false. The bug under test is that the delta silently
    /// answers WRONG, not that it bails out to a (correct, if expensive) full compile — a
    /// bail-out here would hide this bug behind a different, louder one.</item>
    /// <item>A COLD compile of the identical, post-edit tree reports zero diagnostics. This is
    /// asserted BEFORE the comparison below, so that when the next assertion fails, the
    /// failure can only mean the delta invented a diagnostic — never that this fixture's edit
    /// made the source genuinely illegal. The edit to X is deliberately cosmetic (see inline
    /// comment) so that nothing about what V or the implementer codeunit satisfy actually
    /// moves, which is what keeps this cold compile clean.</item>
    /// <item><see cref="RadByName.AssertMatchesColdCompile"/> — the oracle this whole harness
    /// uses instead of a hand-written expected-diagnostics list: a delta must accept and
    /// reject exactly what a full compile of the same tree accepts and rejects. Expected to
    /// FAIL today: the delta reports AL0122 ("… does not implement the interface member …")
    /// against the untouched enum V, which the cold compile one line above already proved is
    /// not a real break in the source — so the AL0122 the delta reports is one the delta
    /// itself invented by stripping X out from under V's packaged, unedited
    /// <c>ImplementedInterfaces</c> entry.</item>
    /// </list>
    /// </summary>
    [SkippableFact]
    public void EditingTheInterfaceAndItsConsumer_MustNotBreakTheUntouchedEnumsInterfaceBinding()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        RadByName.Run(FixtureName, ModuleName, AppId, ExpectedObjectCount, (compiler, workspace, tempRoot) =>
        {
            // X: a cosmetic edit. It changes the file's bytes, so BcCompiler.DeltaCompile
            // classifies "ByName Enum Contract" as `modified` and strips it from the packaged
            // baseline (BcCompiler.Rad.cs:620-624) — without changing the interface's method
            // set at all, so nothing about what V or the implementer codeunit satisfy actually
            // moves. That is what keeps the cold compile of the edited tree clean (assertion 2
            // below): this is not a real widening or narrowing of the contract, just enough of
            // a touch to put X on the delta's `modified` side of the strip-and-resupply split.
            RadByName.Replace(
                RadByName.SourceFile(tempRoot, "ByNameEnumContract.Interface.al"),
                "interface \"ByName Enum Contract\"\n{",
                "interface \"ByName Enum Contract\"\n{\n    " +
                "// touched this cycle; the method set below is unchanged.");

            // W: a body-only edit, "enough" per the plan — it does not change W's own public
            // surface, only what Dispatch() returns, and it is what makes W part of the SAME
            // delta as X rather than an unrelated bystander that happened to compile alongside it.
            RadByName.Replace(
                RadByName.SourceFile(tempRoot, "ByNameEnumConsumer.Codeunit.al"),
                "exit(C.Label());",
                "exit('dispatched:' + C.Label());");

            var delta = compiler.EmitIncremental([tempRoot], ModuleName, workspace);

            Assert.False(delta.FullRebuild,
                "the delta fell back to a full compile instead of silently mis-answering — " +
                "that would hide this bug behind a different, louder one");

            // Prove the tree itself is fine BEFORE comparing the delta against a cold compile,
            // so a failure on the next line can only mean the delta invented a diagnostic —
            // never that this fixture's edit made the source genuinely illegal.
            var cold = RadByName.ColdCompile(tempRoot, ModuleName);
            Assert.True(cold.Emit.Diagnostics.Count == 0,
                "the edited tree does not compile clean cold, so it cannot isolate the delta " +
                "bug this test exists to prove:" + Environment.NewLine +
                string.Join(Environment.NewLine, cold.Emit.Diagnostics));

            // Expected to fail today: the delta reports AL0122 against the untouched enum V —
            // a diagnostic the cold compile just above already proved the same source does not
            // warrant.
            RadByName.AssertMatchesColdCompile(delta, tempRoot, ModuleName);
        });
    }
}
