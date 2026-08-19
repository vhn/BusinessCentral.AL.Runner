// W — an un-rebound intermediary, and the object this scenario needs left ALONE.
//
// The edit under test is body-only, so the library's serialized surface does not move, so this
// file is not rebound and its loaded IL keeps dispatching `Which` by the member id it baked at
// the cold compile. If the repaired pass produced a library under different ids, or bound the
// call against a stale copy of the library's surface, this is where it shows.
codeunit 72322 "RAD NsFree Ovl Caller"
{
    procedure Call(): Text
    var
        Lib: Codeunit "RAD NsFree Ovl Lib";
        Seed: Integer;
    begin
        Seed := 2;
        exit(Lib.Which(Seed));
    end;
}
