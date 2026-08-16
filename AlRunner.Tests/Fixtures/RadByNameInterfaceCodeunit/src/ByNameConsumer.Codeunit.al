// W in the by-name triple (see RadByNameInterfaceCodeunitTests): edited (body-only) in the
// same delta as X (ByNameContract.Interface.al). The codeunit-to-interface assignment below
// is what forces the compiler to resolve V's (the untouched codeunit's) `ImplementedInterfaces`
// reference to X while X's packaged copy has been stripped out from under it.
codeunit 72022 "ByName Consumer"
{
    procedure Dispatch(): Text
    var
        C: Interface "ByName Contract";
        I: Codeunit "ByName Impl";
    begin
        C := I;
        exit(C.Describe());
    end;
}
