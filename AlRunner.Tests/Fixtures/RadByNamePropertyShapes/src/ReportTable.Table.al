// report RelatedTable, X — the table the bystander report's dataitem names. Stripped by the
// edit. `RelatedTable` is what the serialized ReportDataItemDefinition calls that name.
table 72165 "BN Report Table"
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
        Description := 'report-table-v1';
    end;
}
