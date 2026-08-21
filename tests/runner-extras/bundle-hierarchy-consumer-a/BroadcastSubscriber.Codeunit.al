/// <summary>
/// Subscriber #1 of 3, one per dependent app, all bound to the same publisher in the
/// shared library. Appends 'A' exactly once per Broadcast.
/// </summary>
codeunit 64571 "HCA Broadcast Subscriber"
{
    EventSubscriberInstance = StaticAutomatic;

    [EventSubscriber(ObjectType::Codeunit, Codeunit::"HSL Shared Bus", 'OnBroadcast', '', false, false)]
    local procedure HandleBroadcast(var Visits: Text)
    begin
        Visits += 'A';
    end;
}
