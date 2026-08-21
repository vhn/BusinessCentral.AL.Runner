/// <summary>
/// Subscriber #3 of 3, from the app furthest from the publisher in dependency terms.
/// The shared library declares no dependency on this app at all, so a Broadcast from
/// the library reaching this body is dispatch running against the loaded set rather
/// than against the library's own declared closure.
/// </summary>
codeunit 64591 "HCC Broadcast Subscriber"
{
    EventSubscriberInstance = StaticAutomatic;

    [EventSubscriber(ObjectType::Codeunit, Codeunit::"HSL Shared Bus", 'OnBroadcast', '', false, false)]
    local procedure HandleBroadcast(var Visits: Text)
    begin
        Visits += 'C';
    end;
}
