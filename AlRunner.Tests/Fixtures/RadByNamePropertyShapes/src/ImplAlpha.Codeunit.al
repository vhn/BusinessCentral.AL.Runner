// enum-value Implementation, X — the codeunit the bystander enum's value names.
//
// Only the BODY is edited by the test. A codeunit whose serialized surface moves is admitted
// to `changedSurfaces`, which would rebind its direct users and could drag the enum into the
// same delta — and a bystander that gets recompiled from source proves nothing.
codeunit 72145 "BN Impl Alpha" implements "BN Impl Contract"
{
    procedure Answer(): Integer
    begin
        exit(145);
    end;
}
