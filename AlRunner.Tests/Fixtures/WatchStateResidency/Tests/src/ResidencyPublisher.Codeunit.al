codeunit 60982 "Watch Residency Publisher"
{
    procedure Raise()
    begin
        OnResidencyProbe();
    end;

    [IntegrationEvent(false, false)]
    local procedure OnResidencyProbe()
    begin
    end;
}
