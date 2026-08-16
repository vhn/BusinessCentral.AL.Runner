// query RelatedTable, X — the table the bystander query's dataitem names. Stripped by the
// edit.
table 72175 "BN Query Table"
{
    DataClassification = CustomerContent;

    fields
    {
        field(1; "No."; Code[20]) { DataClassification = CustomerContent; }
        field(2; Description; Text[50]) { DataClassification = CustomerContent; }
    }

    keys
    {
        key(PK; "No.") { Clustered = true; }
    }

    trigger OnInsert()
    begin
        Description := 'query-table-v1';
    end;
}
