// CalcFormula, W — `CalcFields` is only legal against a FlowField, so binding this call
// forces the bystander's two formulas to resolve. Both name the stripped table.
codeunit 72132 "BN CalcFormula Caller"
{
    procedure Total(): Decimal
    var
        Header: Record "BN CalcFormula Header";
    begin
        Header."No." := 'calc-v1';
        Header.CalcFields("Line Count", "Line Total");
        exit(Header."Line Total" + Header."Line Count");
    end;
}
