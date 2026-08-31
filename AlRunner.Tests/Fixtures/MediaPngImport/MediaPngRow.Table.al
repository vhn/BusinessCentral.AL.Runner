table 72490 "Media PNG Fixture Row"
{
    DataClassification = CustomerContent;

    fields
    {
        field(1; "Entry No."; Integer) { }
        field(2; Picture; Media) { }
    }

    keys
    {
        key(PK; "Entry No.") { Clustered = true; }
    }
}
