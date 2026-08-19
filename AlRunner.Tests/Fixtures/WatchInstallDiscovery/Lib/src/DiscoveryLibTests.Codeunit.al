codeunit 60994 "Watch Discovery Lib Tests"
{
    Subtype = Test;

    /// <summary>
    /// A test codeunit in the SAME app that owns the install codeunit. npcore's Application
    /// app carries exactly one of these (codeunit 6014508), and when its app group's install
    /// seeding throws, the whole group reports zero tests — the three tests disappear from
    /// the run rather than failing in it.
    /// </summary>
    [Test]
    procedure InstallSeededTheOwningApp()
    var
        Registry: Record "Watch Discovery Registry";
        Assert: Codeunit "Watch Discovery Assert";
    begin
        Assert.IsTrue(Registry.Get('ALPHA'), 'ALPHA discovered in the owning app');
        Assert.AreEqual('Alpha entry', Registry.Description, 'ALPHA description');
        Assert.IsTrue(Registry.Get('BETA'), 'BETA discovered in the owning app');
        Assert.IsTrue(Registry.Get('TRAILER'), 'install trigger ran to completion');
    end;

    /// <summary>
    /// The negative direction, inside the owning app: the discovery event publishes exactly
    /// the two codes its subscriber named. A dispatcher that fired the subscriber twice, or
    /// that let a superseded generation's copy fire alongside the current one, would show up
    /// here as extra rows rather than as a silent duplicate insert.
    /// </summary>
    [Test]
    procedure InstallDiscoveredNothingItWasNotToldTo()
    var
        Registry: Record "Watch Discovery Registry";
        Assert: Codeunit "Watch Discovery Assert";
    begin
        Assert.IsFalse(Registry.Get('GAMMA'), 'GAMMA was never discovered');
        Assert.AreEqual('3', Format(Registry.Count()), 'rows the install trigger left behind');
    end;
}
