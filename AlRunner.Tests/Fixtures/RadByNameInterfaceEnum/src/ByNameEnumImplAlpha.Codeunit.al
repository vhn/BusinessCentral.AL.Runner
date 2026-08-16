// The fourth object RadByNameInterfaceEnumTests' class doc calls out: the codeunit V's
// (ByNameKind.Enum.al) `Alpha` value names in its `Implementation` property, so that
// casting the enum to the interface has a concrete implementer to dispatch to. Untouched
// by every test here — its own identity plays no part in the bug, only V's reference to
// the interface does.
codeunit 72042 "ByName Enum Impl Alpha" implements "ByName Enum Contract"
{
    procedure Label(): Text
    begin
        exit('alpha');
    end;
}
