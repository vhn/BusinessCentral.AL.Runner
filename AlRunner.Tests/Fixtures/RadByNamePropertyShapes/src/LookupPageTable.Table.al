// LookupPageId/DrillDownPageId, V — the BYSTANDER. Untouched, so its serialized
// TableDefinition is what the delta reads, and both properties name the page by name.
table 72141 "BN LookupPage Table"
{
    DataClassification = CustomerContent;
    LookupPageId = "BN LookupPage Page";
    DrillDownPageId = "BN LookupPage Page";

    fields
    {
        field(1; "No."; Code[20]) { DataClassification = CustomerContent; }
        field(2; Description; Text[50]) { DataClassification = CustomerContent; }
    }

    keys
    {
        key(PK; "No.") { Clustered = true; }
    }
}
