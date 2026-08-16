namespace AlRunner.Tests.RadByNameTableExtTarget;

codeunit 72062 "ExtTarget Consumer"
{
    procedure Value(): Integer
    var
        R: Record "ExtTarget Base";
    begin
        exit(R."Ext Value");
    end;
}
