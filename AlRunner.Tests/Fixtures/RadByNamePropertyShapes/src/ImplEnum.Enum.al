// enum-value Implementation, V — the BYSTANDER. Untouched, so the value's `Implementation`
// is read out of the packaged baseline, where it names the codeunit by name.
enum 72146 "BN Impl Enum" implements "BN Impl Contract"
{
    Extensible = true;

    value(0; Alpha)
    {
        Caption = 'Alpha';
        Implementation = "BN Impl Contract" = "BN Impl Alpha";
    }
}
