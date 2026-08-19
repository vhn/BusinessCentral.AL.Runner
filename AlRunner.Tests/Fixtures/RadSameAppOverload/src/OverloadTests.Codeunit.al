namespace AlRunner.Tests.RadSameAppOverload;

// Publishes the overload the caller actually dispatched, BY VALUE.
//
// The staleness this fixture exists for is silent — a caller left bound to the old id
// dispatches a member that still exists — so the returned value is the only witness there
// is. Reporting it in the error text means a wrong answer names itself
// (`BOUND-TO=DECIMAL`) instead of only failing.
//
// The expectation is deliberately the POST-edit one: with the library as checked in, this
// test fails with `BOUND-TO=DECIMAL`, which is the pre-edit runtime answer measured rather
// than assumed. It passes once the Integer overload exists AND the caller was rebound to
// it.
codeunit 72302 "RAD Ovl Tests"
{
    Subtype = Test;

    [Test]
    procedure CallerBindsTheIntegerOverload()
    var
        Caller: Codeunit "RAD Ovl Caller";
        Bound: Text;
    begin
        Bound := Caller.Call();
        if Bound <> 'INTEGER' then
            Error('BOUND-TO=' + Bound);
    end;
}
