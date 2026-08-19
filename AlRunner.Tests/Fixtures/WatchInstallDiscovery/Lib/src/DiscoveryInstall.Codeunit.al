codeunit 60992 "Watch Discovery Install"
{
    Access = Internal;
    Subtype = Install;

    trigger OnInstallAppPerCompany()
    var
        Registry: Record "Watch Discovery Registry";
    begin
        Registry.OnDiscoverEntries();
        InitTrailer();
    end;

    /// <summary>
    /// Seeded AFTER the discovery event, so it records whether the trigger ran to completion.
    /// A trigger that throws mid-way leaves the discovery rows behind but never this one —
    /// the npcore shape, where everything the install codeunit seeds after the failing event
    /// is silently absent.
    /// </summary>
    local procedure InitTrailer()
    var
        Registry: Record "Watch Discovery Registry";
    begin
        Registry.DiscoverEntry('TRAILER', 'Install completed');
    end;
}
