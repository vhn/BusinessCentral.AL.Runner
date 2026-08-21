/// <summary>
/// Two PEER apps in one bundle each extend the platform Item table (27):
/// "BTA Two App Ext A" contributes fields 64531/64532, "BTB Two App Ext B"
/// contributes 64541/64542. Neither depends on the other; this app depends on both.
///
/// Why the claim is runner-specific rather than plain BC behaviour: the runner has no
/// per-app extension model at all. `RecordPatches._parsedExtensionFields` is a
/// `Dictionary&lt;string, List&lt;ParsedField&gt;&gt;` keyed on the base table NAME with no app
/// qualifier (`RecordPatches.cs:96`), and `ApplyTableExtensions`
/// (`RecordPatches.AlSourceParser.cs:743-800`) merges every app's tableextension into
/// that single list, de-duplicating by field id alone:
///
///     var existingIds = new HashSet&lt;int&gt;(existing.Select(f =&gt; f.FieldId));
///     foreach (var f in fields)
///         if (existingIds.Add(f.FieldId)) existing.Add(f);
///
/// So "one merged field set per base table, N registered extension objects" is the
/// runner's model of a shape BC models as N independent extensions. That merge is
/// faithful only if each app's fields stay individually addressable by number, name
/// and type after it — which is what the tests below pin. The order the two peers
/// merge in is not fixed by anything: they declare no dependency on each other, so
/// `BuildAppGroups`' topological sort has nothing to order them by.
/// </summary>
codeunit 64551 "BTX Two App TableExt Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit "BTX Assert";

    /// <summary>
    /// Positive: all four fields — two from each peer app — round-trip through
    /// Insert + Get on one Item record with concrete non-default values. Four distinct
    /// values across three AL types, so an implementation that dropped either app's
    /// contribution, or aliased the two apps onto one slot, cannot pass.
    /// </summary>
    [Test]
    procedure ItemRoundtrip_FieldsFromBothPeerApps_KeepIndependentValues()
    var
        Item: Record Item;
    begin
        Item.Init();
        Item."No." := 'BTX-BOTH-1';
        Item."BTA Alpha Flag" := true;
        Item."BTA Alpha Tag" := 'ALPHA-A';
        Item."BTB Beta Count" := 71;
        Item."BTB Beta Tag" := 'BETA-B';
        Item.Insert();

        Item.Get('BTX-BOTH-1');

        Assert.AreEqual(true, Item."BTA Alpha Flag",
            'peer app A''s Boolean extension field must round-trip');
        Assert.AreEqual('ALPHA-A', Item."BTA Alpha Tag",
            'peer app A''s Code field must round-trip with its own value');
        Assert.AreEqual(71, Item."BTB Beta Count",
            'peer app B''s Integer extension field must round-trip');
        Assert.AreEqual('BETA-B', Item."BTB Beta Tag",
            'peer app B''s Code field must round-trip with its own value');
    end;

    /// <summary>
    /// Positive: writing only peer app A's fields leaves peer app B's untouched.
    /// Proves the two apps hold separate slots on the same record rather than one
    /// shared slot that the last writer wins — the failure mode a name-keyed,
    /// app-agnostic merge invites.
    /// </summary>
    [Test]
    procedure ItemModify_OnlyPeerAsFields_LeavesPeerBsFieldsIntact()
    var
        Item: Record Item;
    begin
        Item.Init();
        Item."No." := 'BTX-BOTH-2';
        Item."BTA Alpha Tag" := 'ALPHA-BEFORE';
        Item."BTB Beta Count" := 33;
        Item."BTB Beta Tag" := 'BETA-KEEP';
        Item.Insert();

        Item.Get('BTX-BOTH-2');
        Item."BTA Alpha Tag" := 'ALPHA-AFTER';
        Item."BTA Alpha Flag" := true;
        Item.Modify();

        Item.Get('BTX-BOTH-2');

        Assert.AreEqual('ALPHA-AFTER', Item."BTA Alpha Tag",
            'peer app A''s field must hold the modified value');
        Assert.AreEqual(true, Item."BTA Alpha Flag",
            'peer app A''s Boolean must hold the modified value');
        Assert.AreEqual(33, Item."BTB Beta Count",
            'peer app B''s Integer must be unchanged by a write to peer app A''s fields');
        Assert.AreEqual('BETA-KEEP', Item."BTB Beta Tag",
            'peer app B''s Code field must be unchanged by a write to peer app A''s fields');
    end;

    /// <summary>
    /// Positive: each peer app's field is addressable BY ITS OWN NUMBER through
    /// RecordRef, and reports its own name. This is the assertion a merge that
    /// de-duplicates by field id across apps cannot satisfy: whichever app lost would
    /// leave its number resolving to the other app's field, or not resolving at all.
    /// </summary>
    [Test]
    procedure FieldRefByNumber_EachPeerAppsField_ReportsItsOwnName()
    var
        Item: Record Item;
        RecRef: RecordRef;
        AlphaTag: FieldRef;
        BetaTag: FieldRef;
    begin
        Item.Init();
        RecRef.GetTable(Item);

        AlphaTag := RecRef.Field(64532);
        BetaTag := RecRef.Field(64542);

        Assert.AreEqual('BTA Alpha Tag', AlphaTag.Name,
            'field 64532 must report peer app A''s own field name');
        Assert.AreEqual('BTB Beta Tag', BetaTag.Name,
            'field 64542 must report peer app B''s own field name');
    end;

    /// <summary>
    /// Positive: Init() leaves both peer apps' fields at their AL type defaults, which
    /// proves each is really present in the record's field list — a field that is
    /// silently absent leaves the read uninitialized rather than type-defaulted.
    /// </summary>
    [Test]
    procedure ItemInit_FieldsFromBothPeerApps_DefaultToTypeDefaults()
    var
        Item: Record Item;
    begin
        Item.Init();

        Assert.AreEqual(false, Item."BTA Alpha Flag",
            'peer app A''s Boolean must default to false');
        Assert.AreEqual('', Item."BTA Alpha Tag",
            'peer app A''s Code field must default to empty');
        Assert.AreEqual(0, Item."BTB Beta Count",
            'peer app B''s Integer must default to 0');
        Assert.AreEqual('', Item."BTB Beta Tag",
            'peer app B''s Code field must default to empty');
    end;

    /// <summary>
    /// Negative: TestField on a blank extension field raises BC's real
    /// "must have a value" error naming THAT app's field. Run for both peers, so the
    /// error text proves each field carries its own identity into BC's message
    /// formatting rather than one app's field standing in for the other's.
    /// </summary>
    [Test]
    procedure TestField_BlankTagFromEachPeerApp_RaisesErrorNamingThatField()
    var
        Item: Record Item;
    begin
        Item.Init();
        Item."No." := 'BTX-BOTH-3';

        asserterror Item.TestField(Item."BTA Alpha Tag");
        Assert.ExpectedErrorContains(GetLastErrorText(), 'BTA Alpha Tag',
            'TestField on peer app A''s blank field must name peer app A''s field');

        asserterror Item.TestField(Item."BTB Beta Tag");
        Assert.ExpectedErrorContains(GetLastErrorText(), 'BTB Beta Tag',
            'TestField on peer app B''s blank field must name peer app B''s field');
    end;

    /// <summary>
    /// Negative: a field number inside peer app A's declared id range that no
    /// tableextension actually uses must NOT resolve. Guards the positive RecordRef
    /// test above against passing on a provider that answers for any number in range.
    /// </summary>
    [Test]
    procedure FieldRefByNumber_UnusedNumberInPeerAsRange_RaisesError()
    var
        Item: Record Item;
        RecRef: RecordRef;
        Unused: FieldRef;
    begin
        Item.Init();
        RecRef.GetTable(Item);

        asserterror Unused := RecRef.Field(64539);

        Assert.ExpectedErrorContains(GetLastErrorText(), '64539',
            'an unused field number in peer app A''s range must not resolve to a field');
    end;
}
