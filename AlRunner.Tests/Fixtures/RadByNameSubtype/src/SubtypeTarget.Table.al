namespace AlRunner.Tests.RadByNameSubtype;

// X — the object the delta strips out of the packaged baseline. Nothing about it is
// special: it is a table, so `changedSurfaces` never admits it, and every untouched
// object whose serialized surface NAMES it is resolved against a module where it is
// no longer there.
table 72000 "Subtype Target"
{
    DataClassification = CustomerContent;

    fields
    {
        field(1; "Entry No."; Integer) { DataClassification = CustomerContent; }
        field(2; Amount; Decimal) { DataClassification = CustomerContent; }
    }

    keys
    {
        key(PK; "Entry No.") { Clustered = true; }
    }
}
