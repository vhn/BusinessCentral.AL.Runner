namespace AlRunner.Tests.RadTwentyObject;

table 71000 "RAD Perf Header"
{
    fields
    {
        field(1; "No."; Code[20]) { DataClassification = SystemMetadata; }
        field(2; Description; Text[50]) { DataClassification = SystemMetadata; }
    }

    keys
    {
        key(PK; "No.") { Clustered = true; }
    }

    trigger OnInsert()
    begin
        Description := 'header-v1';
    end;
}
