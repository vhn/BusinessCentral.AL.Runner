namespace AlRunner.Tests.RadByNameSubtype;

// W — the CALLER. Edited in the same cycle as the table, so it is recompiled from source
// and has to bind `Take` against the bystander's surface as the packaged baseline reports
// it. That is the moment the damaged parameter type becomes observable.
codeunit 72002 "Subtype Caller"
{
    procedure Call(): Integer
    var
        Bystander: Codeunit "Subtype Bystander";
        Target: Record "Subtype Target";
    begin
        Target."Entry No." := 7;
        exit(Bystander.Take(Target) + 1);
    end;
}
