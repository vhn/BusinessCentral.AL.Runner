/// <summary>
/// The second app to extend the shared library's table, with a field id in its own
/// range and a different AL type from HCA's. Distinct types matter: a merge that
/// aliased the two apps onto one slot would surface as BC's real
/// NavObjectDefinitionChangedException ("old type: Text, new type: Integer") rather
/// than as a wrong value.
/// </summary>
tableextension 64583 "HCB Ledger Ext" extends "HSL Shared Ledger"
{
    fields
    {
        field(64584; "HCB Beta Score"; Integer) { DataClassification = CustomerContent; }
    }
}
