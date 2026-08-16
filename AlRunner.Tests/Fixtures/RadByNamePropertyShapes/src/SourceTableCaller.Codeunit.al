// SourceTable, W — edited in the same cycle as the table, so it is rebound from source and
// has to type-check `SetTableView`/`SetRecord` against the bystander page. Those parameters
// ARE the page's `SourceTable`, so this call is the by-name reference becoming observable.
codeunit 72122 "BN SourceTable Caller"
{
    procedure Show(): Code[20]
    var
        Target: Record "BN SourceTable Target";
        Bystander: Page "BN SourceTable Page";
    begin
        Target."No." := 'caller-v1';
        Bystander.SetTableView(Target);
        Bystander.SetRecord(Target);
        exit(Target."No.");
    end;
}
