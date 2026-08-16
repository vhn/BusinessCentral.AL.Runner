namespace AlRunner.Tests.RadByNameTableNoRename;

// V — the BYSTANDER. Never part of the delta under test, so its serialized surface always
// comes from the packaged baseline, never from a fresh parse. TableNo is written by OBJECT ID
// here, not by name: V's own file must stay byte-for-byte untouched across the rename, and
// X's NAME is exactly what the rename changes. A by-name TableNo here would leave V's
// unedited source naming an identifier the rename just retired — a real compile error, but
// the wrong one, and it would prove nothing about the bystander rule under test. The id form
// survives the rename unmodified, so V really can stay untouched while still targeting X.
codeunit 72101 "Rename Bystander"
{
    TableNo = 72100;

    trigger OnRun()
    begin
        Rec.Amount += 1;
    end;
}
