namespace AlRunner.Tests.RadByNameEnumExtTarget;

// W — the CALLER. Edited in the same cycle as the enum, so it is recompiled from source and
// has to bind the enum-value access against the bystander's contribution as the packaged
// baseline reports it. That is the moment a damaged extension target becomes observable:
// "Extended" is not a value X declares itself, only one V's enumextension adds to it.
codeunit 72082 "EnumExt Consumer"
{
    procedure Call(): Integer
    begin
        exit("EnumExt Base"::Extended.AsInteger());
    end;
}
