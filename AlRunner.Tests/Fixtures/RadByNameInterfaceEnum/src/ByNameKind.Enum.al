// V in the by-name triple (see RadByNameInterfaceEnumTests): UNTOUCHED by every test in
// that class. `implements "ByName Enum Contract"` puts that interface's identity into
// this enum's own serialized surface (`ImplementedInterfaces`) — the by-name reference
// the delta path fails to re-validate when the interface is edited without this enum
// being edited too, because a modified Enum never enters `changedSurfaces`
// (BcCompiler.Rad.cs:754-776).
enum 72041 "ByName Kind" implements "ByName Enum Contract"
{
    Extensible = false;

    value(0; Alpha)
    {
        Implementation = "ByName Enum Contract" = "ByName Enum Impl Alpha";
    }
}
