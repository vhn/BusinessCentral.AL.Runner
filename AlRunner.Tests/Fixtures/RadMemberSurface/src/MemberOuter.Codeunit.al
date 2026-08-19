namespace AlRunner.Tests.RadMemberSurface;

// The TRANSITIVE caller, and the third object the by-name harness insists every fixture has.
// It references the caller and never the library, so it is only ever re-emitted if a widened
// cycle widened itself a second time — which is the cascade the one-hop rule exists to stop.
// No test in this suite ever expects to see it.
codeunit 72402 "RAD Member Outer"
{
    procedure Outer(): Text
    var
        Caller: Codeunit "RAD Member Caller";
    begin
        exit(Caller.Call());
    end;
}
