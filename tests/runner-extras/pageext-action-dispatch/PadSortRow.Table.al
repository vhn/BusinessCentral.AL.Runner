table 64529 "Pad Sort Row"
{
    fields
    {
        field(1; Type; Code[10]) { }
        field(2; "Table"; Code[10]) { }
        field(3; Value; Code[10]) { }
        field(4; "Sort Order"; Integer) { }
    }

    keys
    {
        key(PK; Type, "Table", Value) { Clustered = true; }
        key(BySortOrder; Type, "Table", "Sort Order") { }
    }
}
