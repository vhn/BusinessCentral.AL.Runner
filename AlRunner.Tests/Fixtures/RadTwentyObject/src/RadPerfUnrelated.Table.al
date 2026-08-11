namespace AlRunner.Tests.RadTwentyObject;

table 71002 "RAD Perf Unrelated"
{
    fields
    {
        field(1; Code; Code[20]) { DataClassification = SystemMetadata; }
        field(2; Description; Text[50]) { DataClassification = SystemMetadata; }
    }

    keys
    {
        key(PK; Code) { Clustered = true; }
    }
}
