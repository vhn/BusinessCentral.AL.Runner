/// <summary>
/// Subscriber #2 of 3. Bound to the same publisher in the shared library as the
/// subscribers in HCA and HCC, from an app one level further down the hierarchy.
/// </summary>
codeunit 64581 "HCB Broadcast Subscriber"
{
    EventSubscriberInstance = StaticAutomatic;

    [EventSubscriber(ObjectType::Codeunit, Codeunit::"HSL Shared Bus", 'OnBroadcast', '', false, false)]
    local procedure HandleBroadcast(var Visits: Text)
    begin
        Visits += 'B';
    end;
}
