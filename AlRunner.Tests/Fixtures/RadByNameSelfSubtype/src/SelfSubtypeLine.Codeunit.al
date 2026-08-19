namespace AlRunner.Tests.RadByNameSelfSubtype;

// V — the BYSTANDER. Never edited, so it is never compiled from source and is always read
// back out of the packaged baseline. `Attach`'s parameter carries the hub by name: the
// serialized `TypeDefinition.Subtype` is the string "Self Subtype Hub". That one by-name
// reference is the shape under test, and it differs from RadByNameSubtypeTests in exactly
// one respect — the subtype is a Codeunit, not a Record.
codeunit 72121 "Self Subtype Line"
{
    var
        _Hub: Codeunit "Self Subtype Hub";

    procedure Attach(Hub: Codeunit "Self Subtype Hub"): Integer
    begin
        _Hub := Hub;
        exit(17);
    end;
}
