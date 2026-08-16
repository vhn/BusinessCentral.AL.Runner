// CalcFormula, X — the table the bystander's FlowField formulas name. Stripped by the edit.
table 72130 "BN CalcFormula Line"
{
    DataClassification = CustomerContent;

    fields
    {
        field(1; "Entry No."; Integer) { DataClassification = CustomerContent; }
        field(2; "Header No."; Code[20]) { DataClassification = CustomerContent; }
        field(3; Amount; Decimal) { DataClassification = CustomerContent; }
    }

    keys
    {
        key(PK; "Entry No.") { Clustered = true; }
        key(Header; "Header No.") { SumIndexFields = Amount; }
    }

    trigger OnInsert()
    begin
        Amount := 1;
    end;
}
