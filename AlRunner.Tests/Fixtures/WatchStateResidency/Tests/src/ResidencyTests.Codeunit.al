// Two test codeunits running the SAME probe-then-dirty body (see "Watch Residency
// Cycle"), which asks one question at two different boundaries:
//
//   * Whichever of the two runs SECOND is asking about the per-codeunit isolation
//     boundary inside a single run. Their relative order is not fixed and does not need
//     to be — one of them is always second, and that one carries the claim.
//   * BOTH ask about the --watch cycle boundary from the second cycle onward, because by
//     then every earlier cycle has run both of them.
//
// Run once with nothing before them and the probe half is trivially satisfied; what it
// asserts then is only that the test is green when state genuinely is fresh. The DIRTY
// half never cleans up, on purpose — every AL test in the corpus that binds a subscriber
// also unbinds it, which is why a binding leak could sit in the runner unnoticed. An
// unbalanced BindSubscription is the shape that was never covered.
//
// Neither app object here is ever what the watch test edits — "Watch Residency Lib" is.
// That separation is load-bearing, not tidiness: it is the shape the leak was found in.
// When the app owning a subscriber is itself re-emitted, the leaked instance belongs to
// the superseded assembly and stops matching the freshly-registered subscription, so the
// leak hides. Keeping this app warm on one assembly across cycles is what keeps a leaked
// binding reachable — exactly the real case, where the edit landed in the app under
// development and the fault-injection subscriber lived in the untouched test app.
codeunit 60985 "Watch Residency Tests A"
{
    Subtype = Test;

    var
        // Global, not local: a local would let the runner argue the binding died with the
        // method's scope. A codeunit global lives as long as the test-codeunit instance,
        // so the only thing that can drop it is the runner's isolation boundary.
        InjectorA: Codeunit "Watch Residency Injector A";

    [Test]
    procedure NoStateSurvivesAnEarlierExecution_A()
    var
        Cycle: Codeunit "Watch Residency Cycle";
    begin
        Cycle.ProbeThenDirty(InjectorA, 'Tests A');
    end;
}

codeunit 60987 "Watch Residency Tests B"
{
    Subtype = Test;

    var
        InjectorA: Codeunit "Watch Residency Injector A";

    [Test]
    procedure NoStateSurvivesAnEarlierExecution_B()
    var
        Cycle: Codeunit "Watch Residency Cycle";
    begin
        Cycle.ProbeThenDirty(InjectorA, 'Tests B');
    end;
}
