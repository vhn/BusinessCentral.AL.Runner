namespace AlRunner.Tests.RadByNameTableNoRename;

// X — the object the delta strips out of the packaged baseline. A table is never a
// `changedSurfaces` candidate (only codeunits and id-less kinds are), so nothing here makes V
// a direct caller of X. It reaches V by a different mechanism entirely: TableNo is resolved by
// NAME against the packaged module, and X's own key survives a rename — an id'd object's key
// is (Kind, Id), not its name — so the rename arrives as a MODIFICATION, still keyed on id
// 72100, carrying the source's new name while the packaged baseline still remembers the old
// one under that same key.
table 72100 "Rename Target"
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
