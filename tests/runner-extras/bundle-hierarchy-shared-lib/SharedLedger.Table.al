/// <summary>
/// Table owned by the shared library, written to by all three dependent apps and
/// read back by a fourth. If the runner gave each app group its own instance of a
/// sibling-owned table, the test app would read an empty table instead of the three
/// rows its dependents inserted.
///
/// Two of the dependents extend this table (HCA Ledger Ext field 64574, HCB Ledger
/// Ext field 64584) from different levels of the hierarchy — HCA depends only on
/// this library, HCB depends on this library AND on HCA.
/// </summary>
table 64560 "HSL Shared Ledger"
{
    DataClassification = CustomerContent;

    fields
    {
        field(1; "Entry Code"; Code[20]) { DataClassification = CustomerContent; }
        field(2; "Source App"; Text[50]) { DataClassification = CustomerContent; }
        field(3; "Entry Weight"; Integer) { DataClassification = CustomerContent; }
    }

    keys
    {
        key(PK; "Entry Code") { Clustered = true; }
    }
}
