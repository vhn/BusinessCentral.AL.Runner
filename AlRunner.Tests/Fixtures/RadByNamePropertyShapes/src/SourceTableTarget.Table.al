// SourceTable, X — the object the delta strips out of the packaged baseline.
table 72120 "BN SourceTable Target"
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
        Description := 'sourcetable-v1';
    end;
}
