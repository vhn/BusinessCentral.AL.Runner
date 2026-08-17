// The table-content probe. The test inserts one row and never deletes it, so a row
// found at the START of a later execution means committed table content outlived the
// isolation boundary.
table 60981 "Watch Residency Row"
{
    fields
    {
        field(1; "No."; Code[20]) { }
    }

    keys
    {
        key(PK; "No.") { Clustered = true; }
    }
}
