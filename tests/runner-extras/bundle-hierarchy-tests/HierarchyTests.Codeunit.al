/// <summary>
/// Five apps in one bundle, wired into the only multi-level dependency hierarchy the
/// bundle contains:
///
///   HTS Hierarchy Tests  (this app)  -> HSL, HCA, HCB, HCC
///     |
///     +-> HCC Hierarchy Consumer C   -> HSL, HCA, HCB   (apex; sideways x2)
///           |
///           +-> HCB Hierarchy Consumer B -> HSL, HCA    (sideways x1)
///                 |
///                 +-> HCA Hierarchy Consumer A -> HSL
///                       |
///                       +-> HSL Hierarchy Shared Lib    (no in-bundle deps)
///
/// Measured over the bundle: HSL has in-degree 4, HCA 3, HCB 2; HTS has out-degree 4
/// and HCC 3; the longest chain HTS -> HCC -> HCB -> HCA -> HSL is four edges. Every
/// dependent of HSL also depends on at least one of its co-dependents, which is what
/// makes the sideways edges — and the HCC -> {HCA, HCB} -> HSL diamond — exist.
///
/// Why this is worth a fixture: before it, the 31 apps in `tests/runner-extras` formed
/// nine disconnected 1:1 `*-main` -> `*-dep` pairs plus one consumer with out-degree 2
/// (BTX Two App Ext Main) — 11 intra-bundle edges, maximum in-degree 1 (no shared
/// library with several dependents), maximum out-degree 2 (no diamond), and a longest
/// chain of one edge (no transitive depth at all). The runner's own cross-app machinery
/// was built for the shape none of them had: `RadWorkspace.cs:185-187` states outright
/// that "a bundle can have TWO dependents of one producer, and a drained signal is
/// consumed by whichever asks first", and `SiblingDependencies` (`Program.cs:5305`)
/// exists to "link A's types through an A &lt;- B &lt;- C chain". This fixture is the
/// first thing in the bundle to actually stand in that topology.
///
/// The claim is runner-specific, not plain BC behaviour: real BC reaches this state by
/// publishing five apps into a tenant one at a time, each already compiled against
/// published symbols. Here all five are compiled from source in one process, in an
/// order the runner derives itself (`BuildAppGroups`' topological sort over sibling
/// app ids), with each app's symbols republished to its siblings per cycle. What is
/// under test is that graph resolution, not AL semantics.
/// </summary>
codeunit 64601 "HTS Hierarchy Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit "HTS Assert";

    /// <summary>
    /// Positive: one call from the apex traverses four app boundaries and returns a
    /// total that is the sum of every app's own constant along both of its paths to
    /// the shared library.
    ///
    ///   HSL.Visit(v)        = v * 10
    ///   HCA.Contribute(v)   = HSL.Visit(v) + 1        = 10v + 1
    ///   HCB.Contribute(v)   = HCA.Contribute(v) + 20  = 10v + 21
    ///   HCC.ComputeTotal(v) = HCB + HCA + 300         = 20v + 322
    ///
    /// For v = 5 that is 71 + 51 + 300 = 422. Each app contributes a distinct constant,
    /// so a total that is short by 1, 20, 300 or a factor of 10 names the edge that
    /// failed. An implementation that stubbed any hop cannot land on 422.
    /// </summary>
    [Test]
    procedure TransitiveChain_ApexThroughBothSiblings_ReturnsTheWholePathsTotal()
    var
        Apex: Codeunit "HCC Consumer Api";
        Trail: Text;
    begin
        Trail := '';

        Assert.AreEqual(422, Apex.ComputeTotal(Trail, 5),
            'a call from the apex must run every app on both paths: (5*10+1+20) + (5*10+1) + 300');
    end;

    /// <summary>
    /// Positive: with input 0 the multiplicative term vanishes and the result is exactly
    /// the additive constants of the four apps — 1 + 20 + 1 + 300 = 322. Complements the
    /// test above: that one could pass with a wrong constant and a compensating factor,
    /// this one pins the constants alone.
    /// </summary>
    [Test]
    procedure TransitiveChain_WithZeroInput_ReturnsOnlyTheFourAppsConstants()
    var
        Apex: Codeunit "HCC Consumer Api";
        Trail: Text;
    begin
        Trail := '';

        Assert.AreEqual(322, Apex.ComputeTotal(Trail, 0),
            'with input 0 the total must be the four apps'' additive constants: 21 + 1 + 300');
    end;

    /// <summary>
    /// Positive: the by-ref trail is a transcript of the traversal, and it records the
    /// diamond. 'C' then HCB's path 'B','A','L' then HCA's direct path 'A','L' — so the
    /// shared library and HCA each appear TWICE in one call, reached by two different
    /// routes. The exact string 'CBALAL' is only producible by the full graph: drop the
    /// sideways HCC -> HCB edge and it is 'CAL', collapse the diamond and it is 'CBAL'.
    /// </summary>
    [Test]
    procedure TransitiveChain_DiamondTraversal_TrailRecordsBothRoutesToTheLibrary()
    var
        Apex: Codeunit "HCC Consumer Api";
        Trail: Text;
    begin
        Trail := '';

        Apex.ComputeTotal(Trail, 1);

        Assert.AreEqual('CBALAL', Trail,
            'the apex must reach the library via HCB->HCA and again via HCA directly');
    end;

    /// <summary>
    /// Positive: the shared library's table is ONE table for every app in the hierarchy.
    /// Three dependent apps each insert a row; a fifth app — this one, which owns none of
    /// the objects involved — reads all three back and sees each app's own values. Were a
    /// sibling-owned table instantiated per app group, this app's view would be empty.
    /// The range filter pins the count too, so an extra or missing row fails.
    /// </summary>
    [Test]
    procedure SharedLibTable_SeededByAllThreeDependents_IsOneTableSeenByAll()
    var
        Alpha: Codeunit "HCA Consumer Api";
        Beta: Codeunit "HCB Consumer Api";
        Apex: Codeunit "HCC Consumer Api";
        Ledger: Record "HSL Shared Ledger";
    begin
        Alpha.Seed('FANOUT-A');
        Beta.Seed('FANOUT-B');
        Apex.Seed('FANOUT-C');

        Ledger.Get('FANOUT-A');
        Assert.AreEqual('HCA', Ledger."Source App", 'the row inserted by consumer A must carry its tag');
        Assert.AreEqual(11, Ledger."Entry Weight", 'the row inserted by consumer A must carry its weight');

        Ledger.Get('FANOUT-B');
        Assert.AreEqual('HCB', Ledger."Source App", 'the row inserted by consumer B must carry its tag');
        Assert.AreEqual(22, Ledger."Entry Weight", 'the row inserted by consumer B must carry its weight');

        Ledger.Get('FANOUT-C');
        Assert.AreEqual('HCC', Ledger."Source App", 'the row inserted by the apex must carry its tag');
        Assert.AreEqual(33, Ledger."Entry Weight", 'the row inserted by the apex must carry its weight');

        Ledger.Reset();
        Ledger.SetRange("Entry Code", 'FANOUT-A', 'FANOUT-C');
        Assert.AreEqual(3, Ledger.Count(),
            'the three dependents must have written three rows into one shared table');
    end;

    /// <summary>
    /// Positive: two dependents extend the shared library's table from different levels
    /// of the hierarchy (HCA depends on the library only; HCB depends on the library and
    /// on HCA), and the apex — which depends on both — sets both fields on one row. This
    /// app then reads both. Distinct AL types (Text vs Integer) on distinct field ids, so
    /// a merge that aliased the two apps onto one slot fails here rather than returning a
    /// plausible value.
    /// </summary>
    [Test]
    procedure SharedLibTableExt_TwoDependentsFieldsSetByTheApex_AreBothReadable()
    var
        Apex: Codeunit "HCC Consumer Api";
        Ledger: Record "HSL Shared Ledger";
    begin
        Apex.Seed('APEX-EXT-1');

        Ledger.Get('APEX-EXT-1');

        Assert.AreEqual('APEX-WROTE-A', Ledger."HCA Alpha Note",
            'consumer A''s extension field on the library''s table must hold what the apex wrote');
        Assert.AreEqual(330, Ledger."HCB Beta Score",
            'consumer B''s extension field on the library''s table must hold what the apex wrote');
    end;

    /// <summary>
    /// Positive: the sideways edge carries data, not just symbols. Consumer A inserts a
    /// row and sets its own extension field; consumer B — which depends on A — reads that
    /// field off that row, through a table a third app owns. Three apps in one read path.
    /// </summary>
    [Test]
    procedure SharedLibTableExt_ConsumerBReadsConsumerAsField_AcrossTheSidewaysEdge()
    var
        Alpha: Codeunit "HCA Consumer Api";
        Beta: Codeunit "HCB Consumer Api";
    begin
        Alpha.Seed('SIDEWAYS-1');

        Assert.AreEqual('NOTE-FROM-A', Beta.ReadAlphaNoteFrom('SIDEWAYS-1'),
            'consumer B must read consumer A''s extension field on the shared library''s table');
    end;

    /// <summary>
    /// Positive: one publisher in the shared library, three subscribers in three separate
    /// dependent apps, each firing exactly once. Length 3 plus all three distinct tags
    /// present is exactly-once-each without assuming an order — nothing orders the
    /// registration of apps that declare no dependency on one another.
    ///
    /// The library declares no dependency on any of the three, so this is dispatch across
    /// the loaded set rather than down a declared closure.
    /// </summary>
    [Test]
    procedure SharedLibEvent_BroadcastFromTheLibrary_FiresOnceInEachDependent()
    var
        Bus: Codeunit "HSL Shared Bus";
        Visits: Text;
    begin
        Visits := '';

        Bus.Broadcast(Visits);

        Assert.AreEqual(3, StrLen(Visits),
            'exactly three subscribers — one per dependent app — must fire, once each');
        Assert.IsTrue(Visits.Contains('A'), 'consumer A''s subscriber must fire');
        Assert.IsTrue(Visits.Contains('B'), 'consumer B''s subscriber must fire');
        Assert.IsTrue(Visits.Contains('C'), 'the apex''s subscriber must fire');
    end;

    /// <summary>
    /// Positive: two dependents each add a value to the shared library's enum, and a
    /// fifth app resolves the base values and both extensions' values. The runner merges
    /// enumextensions in a registry keyed on the target base enum id with no app
    /// qualifier (`EnumMetadataPatches._extByTargetId`, merged on read in `TryGet`), so
    /// "both apps' values survive" is the claim — one extension clobbering the other, or
    /// either shadowing the base, fails on a concrete ordinal.
    /// </summary>
    [Test]
    procedure SharedLibEnum_ExtendedByTwoDependents_ExposesBaseAndBothAppsValues()
    var
        Kind: Enum "HSL Shared Kind";
    begin
        Assert.AreEqual(0, "HSL Shared Kind"::None.AsInteger(),
            'the library''s own base value must keep ordinal 0');
        Assert.AreEqual(1, "HSL Shared Kind"::Core.AsInteger(),
            'the library''s own second base value must keep ordinal 1');

        Kind := "HSL Shared Kind"::"HCA Alpha Kind";
        Assert.AreEqual(64575, Kind.AsInteger(),
            'consumer A''s enumextension value must keep its own ordinal');

        Kind := "HSL Shared Kind"::"HCB Beta Kind";
        Assert.AreEqual(64585, Kind.AsInteger(),
            'consumer B''s enumextension value must keep its own ordinal');
        Assert.AreEqual('HCB Beta Kind', Format(Kind),
            'consumer B''s value must format as its own caption, not as another app''s value');
    end;

    /// <summary>
    /// Positive: every app in the hierarchy keeps its own module identity, including the
    /// shared library when it is reached from three levels above through two sibling
    /// hops. A runner that resolved GetCurrentModuleInfo from the outermost frame would
    /// answer with the apex's name — or with this test app's — for all four.
    /// </summary>
    [Test]
    procedure ModuleInfo_EveryAppInTheHierarchy_ReportsItsOwnName()
    var
        Alpha: Codeunit "HCA Consumer Api";
        Beta: Codeunit "HCB Consumer Api";
        Apex: Codeunit "HCC Consumer Api";
    begin
        Assert.AreEqual('HCA Hierarchy Consumer A', Alpha.OwnModuleName(),
            'consumer A must see its own module name');
        Assert.AreEqual('HCB Hierarchy Consumer B', Beta.OwnModuleName(),
            'consumer B must see its own module name');
        Assert.AreEqual('HCC Hierarchy Consumer C', Apex.OwnModuleName(),
            'the apex must see its own module name');
        Assert.AreEqual('HSL Hierarchy Shared Lib', Apex.SharedLibModuleNameSeenFromApex(),
            'the shared library must see its own module name when called from the apex');
    end;

    /// <summary>
    /// Negative: a key no app in the hierarchy seeded must not resolve. Guards every
    /// positive Get above against an in-memory provider that answers for any key — which
    /// would make the "one shared table" tests pass without a single row existing.
    /// </summary>
    [Test]
    procedure SharedLibTable_GetOnAKeyNoAppSeeded_RaisesDoesNotExist()
    var
        Ledger: Record "HSL Shared Ledger";
    begin
        asserterror Ledger.Get('NOSEED-1');

        Assert.ExpectedErrorContains(GetLastErrorText(), 'does not exist',
            'a key no dependent seeded must not resolve in the shared library''s table');
    end;

    /// <summary>
    /// Negative: the shared table enforces its primary key across app boundaries — the
    /// same dependent seeding one key twice fails on the second insert. Proves the rows
    /// the positive tests read are held in one keyed table rather than appended to a
    /// per-app list.
    /// </summary>
    [Test]
    procedure SharedLibTable_SeedingOneKeyTwice_RaisesAlreadyExists()
    var
        Alpha: Codeunit "HCA Consumer Api";
    begin
        Alpha.Seed('DUP-1');

        asserterror Alpha.Seed('DUP-1');

        Assert.ExpectedErrorContains(GetLastErrorText(), 'already exists',
            'the shared library''s table must enforce its key on a second insert of one code');
    end;

    /// <summary>
    /// Negative: TestField on each dependent's blank extension field raises BC's real
    /// "must have a value" error naming THAT app's field. Guards the extension-field
    /// positives: a merge that kept only one app's field would name the surviving app's
    /// field in both messages.
    /// </summary>
    [Test]
    procedure TestField_BlankExtensionFieldOfEachDependent_NamesThatAppsOwnField()
    var
        Ledger: Record "HSL Shared Ledger";
    begin
        Ledger.Init();
        Ledger."Entry Code" := 'TESTFIELD-1';

        asserterror Ledger.TestField(Ledger."HCA Alpha Note");
        Assert.ExpectedErrorContains(GetLastErrorText(), 'HCA Alpha Note',
            'TestField on consumer A''s blank field must name consumer A''s field');

        asserterror Ledger.TestField(Ledger."HCB Beta Score");
        Assert.ExpectedErrorContains(GetLastErrorText(), 'HCB Beta Score',
            'TestField on consumer B''s blank field must name consumer B''s field');
    end;
}
