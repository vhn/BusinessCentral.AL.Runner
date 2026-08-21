/// <summary>
/// This app's tableextension on the SHARED LIBRARY's table — not on a platform table
/// and not on its own. HCB Ledger Ext extends the same table from one level further
/// down the hierarchy, so the runner's per-base-table extension merge
/// (`RecordPatches._parsedExtensionFields`, keyed on the base table name with no app
/// qualifier) has to keep two apps' fields on a sibling-owned table distinct.
/// </summary>
tableextension 64573 "HCA Ledger Ext" extends "HSL Shared Ledger"
{
    fields
    {
        field(64574; "HCA Alpha Note"; Text[50]) { DataClassification = CustomerContent; }
    }
}
