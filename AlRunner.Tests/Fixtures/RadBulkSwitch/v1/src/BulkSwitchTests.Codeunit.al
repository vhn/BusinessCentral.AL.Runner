namespace AlRunner.Tests.RadBulkSwitch;

codeunit 71206 "Bulk Switch Tests"
{
    Subtype = Test;

    [Test]
    procedure BulkValuesMatchTheCheckedOutVersion()
    var
        Assert: Codeunit "Bulk Switch Assert";
        Service: Codeunit "Bulk Switch Service";
        Header: Record "Bulk Switch Header";
    begin
        Assert.AreEqual(42, Service.Compute(), 'Compute');
        Assert.AreEqual(3, Service.LineWeight(), 'LineWeight');
        Assert.AreEqual(2, Service.HighestStatus(), 'HighestStatus');

        Header.Code := 'SWITCH';
        Header.Insert(true);
        Header.Get('SWITCH');
        Assert.AreEqual(7, Header.Value, 'Header OnInsert seed');
    end;
}
