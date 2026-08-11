namespace AlRunner.Tests.RadTwentyObjectTests;

using AlRunner.Tests.RadTwentyObject;

codeunit 71202 "RAD Perf Watch Tests"
{
    Subtype = Test;

    [Test]
    procedure ServiceValueIsForty()
    var
        Assert: Codeunit "RAD Perf Assert";
        Service: Codeunit "RAD Perf Service";
    begin
        Assert.AreEqualInt(40, Service.Value(), 'Service value');
    end;

    [Test]
    procedure CallerCoercesToThirtyNine()
    var
        Assert: Codeunit "RAD Perf Assert";
        Caller: Codeunit "RAD Perf Caller";
    begin
        Assert.AreEqualInt(39, Caller.Value(), 'Caller value');
    end;

    [Test]
    procedure HeaderInsertStampsV1()
    var
        Assert: Codeunit "RAD Perf Assert";
        Header: Record "RAD Perf Header";
    begin
        Header."No." := 'H1';
        Header.Insert(true);
        Header.Get('H1');
        Assert.AreEqualText('header-v1', Header.Description, 'Header insert trigger');
    end;

    [Test]
    procedure ExtensionAFieldRoundTrips()
    var
        Assert: Codeunit "RAD Perf Assert";
        Header: Record "RAD Perf Header";
    begin
        Header."No." := 'H2';
        Header."Extension A" := 'kept';
        Header.Insert();
        Header.Get('H2');
        Assert.AreEqualText('kept', Header."Extension A", 'Extension A round trip');
    end;

    [Test]
    procedure StatusExtensionOrdinalIs71000()
    var
        Assert: Codeunit "RAD Perf Assert";
        Status: Enum "RAD Perf Status";
    begin
        Status := Enum::"RAD Perf Status"::Archived;
        Assert.AreEqualInt(71000, Status.AsInteger(), 'Archived ordinal');
    end;

    [Test]
    procedure LineRoundTrips()
    var
        Assert: Codeunit "RAD Perf Assert";
        Line: Record "RAD Perf Line";
    begin
        Line."Entry No." := 1;
        Line."Header No." := 'H1';
        Line.Insert();
        Line.Get(1);
        Assert.AreEqualText('H1', Line."Header No.", 'Line header no.');
    end;
}
