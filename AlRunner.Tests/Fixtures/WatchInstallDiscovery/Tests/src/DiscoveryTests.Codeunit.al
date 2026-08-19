codeunit 60996 "Watch Discovery Dep Tests"
{
    Subtype = Test;

    /// <summary>
    /// The page-opening shape all seventeen drifted npcore tests share: delete the rows, open
    /// a page belonging to the OTHER app, and assert that page's own OnOpenPage put them back.
    ///
    /// <para>Deliberately NOT asserted here: that this app can see the LIB app's
    /// install-trigger seed directly. It cannot, and that is a separate pre-existing
    /// divergence from BC — each app group opens with RecordPatches.ResetPerTestState() and
    /// then restores only the dependency+company baseline, and a sibling SOURCE app in the
    /// same bundle is not one of the registered dependency assemblies, so its Install codeunit
    /// never re-fires for this group. It is stable across cold and warm cycles alike, so it is
    /// not what this suite measures.</para>
    /// </summary>
    [Test]
    procedure OpeningTheListRediscoversDeletedEntries()
    var
        Registry: Record "Watch Discovery Registry";
        List: TestPage "Watch Discovery List";
        Assert: Codeunit "Watch Discovery Assert";
    begin
        Registry.DeleteAll();
        List.OpenView();
        List.Close();
        Assert.IsTrue(Registry.Get('ALPHA'), 'ALPHA rediscovered by the page');
        Assert.AreEqual('Alpha entry', Registry.Description, 'ALPHA description after rediscovery');
        Assert.IsTrue(Registry.Get('BETA'), 'BETA rediscovered by the page');
        Assert.AreEqual('Beta entry', Registry.Description, 'BETA description after rediscovery');
    end;

    /// <summary>
    /// The xmlport neighbour of the page case. RecordPatches.RealXmlPortMetadata keeps the
    /// same shape of process-lifetime "already loaded" set that made pages fail on a warm
    /// cycle, and it is NOT cleared by ResetForReload — so this looks like the same defect on
    /// a second surface.
    ///
    /// <para>It is not, or at least not reachably: with that set deliberately left uncleared,
    /// this test still passes on every cycle. The export therefore does not depend on the
    /// stale entry, and no runner change was made for it. What this test is, is the coverage
    /// that was missing while that was unknown — a warm-cycle xmlport export asserted by the
    /// content it produces, so if the neighbour ever does become reachable it fails here
    /// rather than in a corpus.</para>
    /// </summary>
    [Test]
    procedure ExportingThroughTheXmlPortEmitsTheDiscoveredRows()
    var
        Registry: Record "Watch Discovery Registry";
        Buffer: Record "Watch Discovery Buffer";
        List: TestPage "Watch Discovery List";
        Assert: Codeunit "Watch Discovery Assert";
        Port: XmlPort "Watch Discovery Xml";
        Payload: OutStream;
        Exported: InStream;
        Xml: Text;
        Line: Text;
    begin
        Registry.DeleteAll();
        List.OpenView();
        List.Close();

        Buffer.DeleteAll();
        Buffer.Init();
        Buffer.Code := 'EXPORT';
        Buffer.Insert();
        Buffer.Payload.CreateOutStream(Payload);
        Port.SetDestination(Payload);
        Port.Export();
        Buffer.Modify();

        Buffer.Get('EXPORT');
        Buffer.CalcFields(Payload);
        Buffer.Payload.CreateInStream(Exported);
        // Line by line rather than one Read: an xmlport export is not a length-prefixed AL
        // value, and where it puts its newlines is not this test's claim.
        while not Exported.EOS() do begin
            Exported.ReadText(Line);
            Xml += Line;
        end;
        Assert.IsTrue(StrPos(Xml, 'ALPHA') > 0, 'exported XML names ALPHA');
        Assert.IsTrue(StrPos(Xml, 'Beta entry') > 0, 'exported XML carries the BETA description');
    end;

    /// <summary>
    /// The negative direction: discovery publishes exactly what its subscriber named and
    /// nothing else, and asking for a row nobody discovered reports the specific
    /// nothing-within-the-filter error rather than an empty success.
    /// </summary>
    [Test]
    procedure UndiscoveredEntryIsAbsentAndReportsNotFound()
    var
        Registry: Record "Watch Discovery Registry";
        List: TestPage "Watch Discovery List";
        Assert: Codeunit "Watch Discovery Assert";
    begin
        Registry.DeleteAll();
        List.OpenView();
        List.Close();
        Assert.IsFalse(Registry.Get('GAMMA'), 'GAMMA was never discovered');
        Registry.SetRange(Code, 'GAMMA');
        asserterror Registry.FindFirst();
        Assert.ExpectedError(
            'There is no Watch Discovery Registry within the filter',
            'FindFirst on an undiscovered code');
    end;
}
