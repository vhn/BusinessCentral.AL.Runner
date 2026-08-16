namespace AlRunner.Tests.RadByNameTableExtTarget;

table 72060 "ExtTarget Base"
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
}
