// Publishes what the un-rebound callers actually dispatched, BY VALUE.
//
// Everything this fixture guards against is silent. A repaired pass that emitted the stale body,
// or that re-emitted the library under different member ids, produces a green compile either
// way — the returned string is the only witness. Reporting it in the error text means a wrong
// answer names itself (`GOT=DECIMAL` for the stale body, `GOT=HOLD-WRONG` for a broken round
// trip) instead of only failing.
//
// The expectation is deliberately the POST-edit one: with the library as checked in this test
// fails with `GOT=DECIMAL`, which is the pre-edit runtime answer measured rather than assumed.
codeunit 72323 "RAD NsFree Ovl Tests"
{
    Subtype = Test;

    [Test]
    procedure UnreboundCallersStillDispatchTheRepairedLibrary()
    var
        Caller: Codeunit "RAD NsFree Ovl Caller";
        Got: Text;
    begin
        Got := Caller.Call();
        if Got <> 'DECIMAL-V2' then
            Error('GOT=' + Got);
    end;
}
