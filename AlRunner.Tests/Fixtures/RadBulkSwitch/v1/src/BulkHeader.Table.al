namespace AlRunner.Tests.RadBulkSwitch;

table 71200 "Bulk Switch Header"
{
    fields
    {
        field(1; "Code"; Code[20]) { DataClassification = SystemMetadata; }
        field(2; "Value"; Integer) { DataClassification = SystemMetadata; }
    }

    keys
    {
        key(PK; "Code") { Clustered = true; }
    }

    trigger OnInsert()
    var
        Helper: Codeunit "Bulk Switch Helper A";
    begin
        Value := Helper.Seed();
    end;
}
