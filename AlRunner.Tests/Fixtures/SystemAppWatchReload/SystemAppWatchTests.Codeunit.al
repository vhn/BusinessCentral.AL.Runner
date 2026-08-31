codeunit 72501 "SystemPackage Watch Tests"
{
    Subtype = Test;
    TestPermissions = Disabled;

    [Test]
    procedure TenantMediaHasMicrosoftSystemPackageField()
    var
        Assert: Codeunit "SystemPackage Watch Assert";
        TenantMedia: RecordRef;
    begin
        TenantMedia.Open(2000000184);
        Assert.IsTrue(
            TenantMedia.FieldExist(10),
            'Tenant Media must retain Microsoft SystemPackage field 10 after every watch reload.');
        TenantMedia.Close();
    end;

    [Test]
    procedure TenantMediaDoesNotInventMissingFields()
    var
        Assert: Codeunit "SystemPackage Watch Assert";
        TenantMedia: RecordRef;
    begin
        TenantMedia.Open(2000000184);
        Assert.IsFalse(
            TenantMedia.FieldExist(9),
            'Tenant Media must not fabricate field 9 while restoring the Microsoft shape.');
        TenantMedia.Close();
    end;

    // EDIT-MARKER-V1
}
