// CalcFormula, V — the BYSTANDER. Untouched, so both FlowFields are resolved from the
// packaged baseline, and each one's `CalcFormula` names the line table by name.
table 72131 "BN CalcFormula Header"
{
    DataClassification = CustomerContent;

    fields
    {
        field(1; "No."; Code[20]) { DataClassification = CustomerContent; }
        field(2; "Line Count"; Integer)
        {
            Editable = false;
            FieldClass = FlowField;
            CalcFormula = count("BN CalcFormula Line" where("Header No." = field("No.")));
        }
        field(3; "Line Total"; Decimal)
        {
            Editable = false;
            FieldClass = FlowField;
            CalcFormula = sum("BN CalcFormula Line".Amount where("Header No." = field("No.")));
        }
    }

    keys
    {
        key(PK; "No.") { Clustered = true; }
    }
}
