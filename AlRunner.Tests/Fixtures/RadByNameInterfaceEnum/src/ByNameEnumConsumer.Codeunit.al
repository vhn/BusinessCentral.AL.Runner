// W in the by-name triple (see RadByNameInterfaceEnumTests): edited (body-only) in the
// same delta as X (ByNameEnumContract.Interface.al). The enum-to-interface cast below is
// what forces the compiler to resolve V's (the untouched enum's) `ImplementedInterfaces`
// reference to X while X's packaged copy has been stripped out from under it.
codeunit 72043 "ByName Enum Consumer"
{
    procedure Dispatch(): Text
    var
        K: Enum "ByName Kind";
        C: Interface "ByName Enum Contract";
    begin
        K := K::Alpha;
        C := K;
        exit(C.Label());
    end;
}
