codeunit 60991 "Watch Discovery Source"
{
    Access = Internal;

    [EventSubscriber(ObjectType::Table, Database::"Watch Discovery Registry", 'OnDiscoverEntries', '', true, true)]
    local procedure OnDiscoverEntries(var Sender: Record "Watch Discovery Registry")
    begin
        Sender.DiscoverEntry('ALPHA', 'Alpha entry');
        Sender.DiscoverEntry('BETA', 'Beta entry');
    end;
}
