namespace AlRunner.Tests.RadBulkSwitch;

table 71201 "Bulk Switch Line"
{
    fields
    {
        field(1; "Entry No."; Integer) { DataClassification = SystemMetadata; }
        field(2; "Header Code"; Code[20]) { DataClassification = SystemMetadata; }
    }

    keys
    {
        key(PK; "Entry No.") { Clustered = true; }
    }

    procedure Weight(): Integer
    begin
        exit(5);
    end;
}
