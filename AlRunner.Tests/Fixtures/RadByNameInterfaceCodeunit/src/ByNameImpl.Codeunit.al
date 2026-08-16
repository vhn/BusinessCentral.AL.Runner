// V in the by-name triple (see RadByNameInterfaceCodeunitTests): UNTOUCHED by every test in
// that class. `implements "ByName Contract"` puts that interface's identity into this
// codeunit's own serialized surface (`ImplementedInterfaces`) — the by-name reference the
// delta path fails to re-validate when the interface is edited without this codeunit being
// edited too.
//
// `Ping` is not required by the checked-in interface — it is ordinary, unrelated surface.
// The test's edit to X adds exactly this signature as a second required interface member,
// so V (never touched) already satisfies the widened interface without a single byte of its
// own source changing. That is what keeps the edited tree legal AL while V stays untouched.
codeunit 72021 "ByName Impl" implements "ByName Contract"
{
    procedure Describe(): Text
    begin
        exit('impl-v1');
    end;

    procedure Ping(): Integer
    begin
        exit(1);
    end;
}
