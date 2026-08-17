// Two Manual subscribers, identical except for which marker row they write and — the
// point of having two — WHO OWNS THE BOUND INSTANCE:
//
//   A  is a global of the TEST codeunit. TestExecutor disposes the test codeunit at the
//      end of its run, and BC's own disposal is what removes the binding, so A was never
//      the leak. It is here so a regression in that path fails a test instead of being
//      assumed.
//   B  is a global of a SingleInstance codeunit. Those are not disposed with the test
//      codeunit — they are cached for the session, and BcRuntime.ResetSingleInstanceCache
//      only forgets them, so BC never unbinds anything they own. Until the isolation
//      boundary swept Session.EventBindings itself, B stayed bound for the life of the
//      process.
//
// Both write to a table rather than to their own instance state: the observation has to
// survive a generation boundary, and after a reload the leaked instance belongs to the
// previous assembly, which no AL variable in the new one can reach.
codeunit 60983 "Watch Residency Injector A"
{
    EventSubscriberInstance = Manual;

    [EventSubscriber(ObjectType::Codeunit, Codeunit::"Watch Residency Publisher", 'OnResidencyProbe', '', false, false)]
    local procedure MarkFired()
    var
        Marker: Record "Watch Residency Marker";
    begin
        if Marker.Get('A') then
            exit;
        Marker.Init();
        Marker."No." := 'A';
        Marker.Insert();
    end;
}

codeunit 60986 "Watch Residency Injector B"
{
    EventSubscriberInstance = Manual;

    [EventSubscriber(ObjectType::Codeunit, Codeunit::"Watch Residency Publisher", 'OnResidencyProbe', '', false, false)]
    local procedure MarkFired()
    var
        Marker: Record "Watch Residency Marker";
    begin
        if Marker.Get('B') then
            exit;
        Marker.Init();
        Marker."No." := 'B';
        Marker.Insert();
    end;
}
