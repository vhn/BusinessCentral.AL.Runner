// The probe-then-dirty body, shared by both test codeunits so the same claim is made at
// two different boundaries without two copies of it drifting apart.
//
// `InjectorA` is passed in by var rather than held here: it has to be a GLOBAL of the
// calling test codeunit for the A-path claim to mean anything. A codeunit variable local
// to a test method dies with the method, so binding one would prove nothing about the
// isolation boundary — it would prove AL scoping.
codeunit 60988 "Watch Residency Cycle"
{
    procedure ProbeThenDirty(var InjectorA: Codeunit "Watch Residency Injector A"; Origin: Text)
    var
        Publisher: Codeunit "Watch Residency Publisher";
        State: Codeunit "Watch Residency State";
        Lib: Codeunit "Watch Residency Lib";
        Marker: Record "Watch Residency Marker";
        Row: Record "Watch Residency Row";
    begin
        // The dependency the watch test edits between cycles. Asserted so the two-app
        // wiring is load-bearing rather than decorative: if the edited app stopped being
        // reachable from here, this would say so instead of quietly measuring nothing.
        if Lib.Ping() <> 1 then
            Error('RESIDENCY-PROBE-BROKEN dependency (%1): "Watch Residency Lib".Ping() returned %2, expected 1.', Origin, Lib.Ping());

        // ── PROBE: this must look like a process that has never run this ────────────
        Marker.DeleteAll();
        Publisher.Raise();
        if Marker.Get('A') then
            Error('RESIDENCY-LEAK event-binding (%1; bound instance owned by the test codeunit): publishing with nothing bound in THIS execution still reached the Manual subscriber, so a BindSubscription from an earlier execution is still live.', Origin);
        if Marker.Get('B') then
            Error('RESIDENCY-LEAK event-binding (%1; bound instance owned by a SingleInstance codeunit): publishing with nothing bound in THIS execution still reached the Manual subscriber, so a BindSubscription from an earlier execution is still live.', Origin);

        if State.BumpCount() <> 0 then
            Error('RESIDENCY-LEAK single-instance (%1): the SingleInstance codeunit reports %2 bumps at the start, so its instance state survived an earlier execution.', Origin, State.BumpCount());

        if not Row.IsEmpty() then
            Error('RESIDENCY-LEAK table-content (%1): "Watch Residency Row" is not empty at the start, so committed rows survived an earlier execution.', Origin);

        // ── DIRTY: leave one of each behind, with no cleanup ─────────────────────────
        BindSubscription(InjectorA);
        State.Arm();
        State.Bump();
        Row.Init();
        Row."No." := 'DIRTY';
        Row.Insert();

        // ── PROVE THE PROBE CAN ACTUALLY SEE EACH KIND OF STATE ──────────────────────
        Marker.DeleteAll();
        Publisher.Raise();
        if not Marker.Get('A') then
            Error('RESIDENCY-PROBE-BROKEN event-binding A (%1): a subscriber bound moments ago by the test codeunit did not fire, so the probe above cannot detect a leaked binding either.', Origin);
        if not Marker.Get('B') then
            Error('RESIDENCY-PROBE-BROKEN event-binding B (%1): a subscriber bound moments ago by the SingleInstance codeunit did not fire, so the probe above cannot detect a leaked binding either.', Origin);

        if State.BumpCount() <> 1 then
            Error('RESIDENCY-PROBE-BROKEN single-instance (%1): expected 1 bump immediately after bumping, got %2, so the probe above cannot detect leaked instance state either.', Origin, State.BumpCount());

        if Row.IsEmpty() then
            Error('RESIDENCY-PROBE-BROKEN table-content (%1): the row inserted moments ago is not readable, so the probe above cannot detect leaked rows either.', Origin);
    end;
}
