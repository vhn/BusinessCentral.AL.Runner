// enum-value Implementation, W — the enum-to-interface assignment is the ONE AL construct
// that consumes `Implementation`, so this binds precisely the part of the bystander's surface
// that names the stripped codeunit.
codeunit 72147 "BN Impl Caller"
{
    procedure Ask(): Integer
    var
        Which: Enum "BN Impl Enum";
        Contract: Interface "BN Impl Contract";
    begin
        Which := Which::Alpha;
        Contract := Which;
        exit(Contract.Answer() + 1);
    end;
}
