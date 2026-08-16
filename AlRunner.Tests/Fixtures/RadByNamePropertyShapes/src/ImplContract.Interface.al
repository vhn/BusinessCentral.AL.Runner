// enum-value Implementation — the contract the enum value maps to a codeunit.
// No namespace, deliberately: an id-less object loses its namespace in a delta, and this
// fixture is not the place to re-test that.
interface "BN Impl Contract"
{
    procedure Answer(): Integer;
}
