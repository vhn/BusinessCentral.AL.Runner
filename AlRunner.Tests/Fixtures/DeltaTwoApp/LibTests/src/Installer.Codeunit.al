codeunit 60952 "Delta Installer"
{
    Subtype = Install;

    trigger OnInstallAppPerCompany()
    var
        Seed: Record "Delta Install Seed";
    begin
        Seed.Code := 'READY';
        Seed.Value := 7;
        Seed.Insert();
    end;
}
