using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// <see cref="RadAppCohort"/> translates the AppId a COMPILATION is built with into the identity
/// the workspace store keys that app by. The two are not the same thing, and the gap is the whole
/// reason the type exists: an app group with no <c>app.json</c> keys as <c>name:&lt;module&gt;</c>
/// in the store while its compilation is given <c>DeterministicGuid(moduleName)</c>.
///
/// <para>Every failure of this translation is silent by construction — a lookup that does not
/// match returns null, the edge is dropped, and the graph then says "this app calls nothing",
/// which is exactly what a correct answer looks like for an app that really calls nothing. So
/// the cases below are asserted directly rather than through a compile.</para>
///
/// <para>Pure; no BC artifacts.</para>
/// </summary>
public class RadAppCohortTests
{
    private const string Root = "/tmp/rad-cohort-fixture";

    /// <summary>
    /// The declared case. Nothing clever, but it is the half that would still pass if
    /// <see cref="RadAppCohort.IdentityOf"/> were gutted to always return null — so the
    /// app.json-less case below is the one that proves the translation.
    /// </summary>
    [Fact]
    public void AnAppWithAnAppJsonId_IsFoundByThatId()
    {
        var declared = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var cohort = RadAppCohort.Build(Root, [(declared, "Declared App")]);

        Assert.Equal(RadWorkspaceStore.IdentityOf(declared, "Declared App"), cohort.IdentityOf(declared));
        Assert.Equal(1, cohort.Count);
    }

    /// <summary>
    /// The load-bearing one. The compiler never sees a null AppId — it substitutes a hash of the
    /// module name — so a cohort that keyed on the DECLARED id would never match this group, and
    /// every edge into it would be dropped with no diagnostic anywhere.
    /// </summary>
    [Fact]
    public void AnAppWithNoAppJsonId_IsFoundByTheIdItsCompilationActuallyGets()
    {
        var cohort = RadAppCohort.Build(Root, [((Guid?)null, "Orphan Suites")]);

        var compilerId = BcCompiler.CompilationAppId(null, "Orphan Suites");
        Assert.NotEqual(Guid.Empty, compilerId);
        Assert.Equal("name:Orphan Suites", cohort.IdentityOf(compilerId));
        // And it is NOT reachable under the id it does not have.
        Assert.Null(cohort.IdentityOf(Guid.Empty));
    }

    /// <summary>
    /// Everything outside the bundle — the Base Application, the System Application, every
    /// precompiled <c>.app</c> — answers null, which is what makes the caller drop those edges
    /// rather than retain 70k–210k unactionable ones.
    /// </summary>
    [Fact]
    public void AnAppOutsideTheBundle_IsNotFound()
    {
        var cohort = RadAppCohort.Build(Root, [(Guid.NewGuid(), "In Bundle")]);

        Assert.Null(cohort.IdentityOf(Guid.Parse("99999999-9999-9999-9999-999999999999")));
    }

    /// <summary>
    /// Two app groups whose compilations would be built with the SAME AppId is not a bundle this
    /// runner can answer questions about: one identity's edges would be silently retargeted at
    /// the other. Keeping the first and discarding the second is the one outcome that cannot be
    /// noticed downstream, because the loss looks identical to "that app calls nothing".
    ///
    /// <para>Duplicate <c>app.json</c> ids are rejected earlier (#1850), but that check compares
    /// STORE identities, and the collision this guards is between a declared id and the
    /// deterministic hash given to an app.json-less group — whose store identities differ
    /// (<c>&lt;guid&gt;</c> versus <c>name:&lt;module&gt;</c>), so it passes that check.</para>
    /// </summary>
    [Fact]
    public void TwoAppsWhoseCompilationsShareAnAppId_AreRefusedRatherThanSilentlyMerged()
    {
        // Construct the collision directly: whatever id the app.json-less group's compilation
        // gets, declare that same id on a second group.
        var collided = BcCompiler.CompilationAppId(null, "Orphan Suites");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RadAppCohort.Build(Root, [((Guid?)null, "Orphan Suites"), (collided, "Declared App")]));

        // The message has to name both, or it cannot be acted on.
        Assert.Contains(collided.ToString(), ex.Message);
        Assert.Contains("name:Orphan Suites", ex.Message);
        Assert.Contains(RadWorkspaceStore.IdentityOf(collided, "Declared App"), ex.Message);
    }

    /// <summary>
    /// …but the same app listed twice is not a collision — both entries agree on the answer, so
    /// there is nothing to be ambiguous about. Without this the guard above would reject bundles
    /// that are merely redundant.
    /// </summary>
    [Fact]
    public void TheSameAppListedTwice_IsNotACollision()
    {
        var declared = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var cohort = RadAppCohort.Build(Root, [(declared, "Declared App"), (declared, "Declared App")]);

        Assert.Equal(1, cohort.Count);
        Assert.Equal(RadWorkspaceStore.IdentityOf(declared, "Declared App"), cohort.IdentityOf(declared));
    }
}
