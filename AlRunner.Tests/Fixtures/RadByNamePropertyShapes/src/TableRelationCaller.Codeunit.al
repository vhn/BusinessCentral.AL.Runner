// TableRelation, W — the W this shape WOULD use: it validates the very field whose
// `TableRelation` names the target. No test drives it; see TableRelationBystander.Table.al.
codeunit 72127 "BN TableRelation Caller"
{
    procedure Relate(): Code[20]
    var
        Source: Record "BN TableRelation Table";
    begin
        Source."Entry No." := 1;
        Source.Validate("Target Code", 'rel-v1');
        exit(Source."Target Code");
    end;
}
