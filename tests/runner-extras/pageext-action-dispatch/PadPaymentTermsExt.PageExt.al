pageextension 64526 "Pad Payment Terms Ext" extends "Payment Terms"
{
    layout
    {
        modify(Description)
        {
            trigger OnAfterValidate()
            var
                Row: Record "Pad Row";
            begin
                Row.Log('VALIDATE-' + Rec.Code);
            end;
        }
    }
}
