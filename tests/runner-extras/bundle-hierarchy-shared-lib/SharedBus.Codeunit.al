/// <summary>
/// One publisher in the shared library, three StaticAutomatic subscribers in three
/// separate dependent apps — the runtime half of the fan-out this fixture exists for.
/// Each subscriber appends its own single-character tag to the by-ref text, so the
/// caller can assert one fire per dependent app without depending on the order the
/// three emitted assemblies register in (nothing guarantees an order between apps
/// that declare no dependency on each other).
/// </summary>
codeunit 64562 "HSL Shared Bus"
{
    procedure Broadcast(var Visits: Text)
    begin
        OnBroadcast(Visits);
    end;

    [IntegrationEvent(false, false)]
    local procedure OnBroadcast(var Visits: Text)
    begin
    end;
}
