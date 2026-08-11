namespace AlRunner.Tests.RadTwentyObject;

table 71001 "RAD Perf Line"
{
    fields
    {
        field(1; "Entry No."; Integer) { DataClassification = SystemMetadata; }
        field(2; "Header No."; Code[20]) { DataClassification = SystemMetadata; }
    }

    keys
    {
        key(PK; "Entry No.") { Clustered = true; }
    }
}
