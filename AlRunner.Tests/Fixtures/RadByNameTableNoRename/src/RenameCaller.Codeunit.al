namespace AlRunner.Tests.RadByNameTableNoRename;

// W — the CALLER, edited in the same delta as the table. `Bystander.Run(Target)` only binds
// while "Rename Bystander" still exposes `Run(Record)` — the overload that exists exactly
// when its TableNo target resolves. A variable's type is not an id reference, so W has to
// name the table by its current name; the rename's follow-up edit lands here, riding along
// with an unrelated body change so this file is "modified" for a reason beyond the rename
// alone too.
codeunit 72102 "Rename Caller"
{
    procedure Call(): Boolean
    var
        Bystander: Codeunit "Rename Bystander";
        Target: Record "Rename Target";
    begin
        Target."Entry No." := 1;
        exit(Bystander.Run(Target));
    end;
}
