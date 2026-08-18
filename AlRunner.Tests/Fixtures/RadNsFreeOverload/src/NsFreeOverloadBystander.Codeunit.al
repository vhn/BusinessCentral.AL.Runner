// V — the BYSTANDER. Never edited by the scenario, so it is never compiled from source and is
// always read back out of the packaged baseline.
//
// `Hold`'s parameter carries the library BY NAME: the serialized `TypeDefinition.Subtype` is
// the string "RAD NsFree Ovl Lib". Once the delta strips the library, this parameter is the
// reference the packaged module symbol cannot resolve — it degrades to
// `__MissingTypeSymbol__`, and the caller's `Bystander.Hold(Lib)` then fails AL0133 against a
// tree that compiles clean from scratch.
//
// That is what makes this fixture a runtime test of the repair rather than of the rebind: the
// cycle only reaches a correct answer if the surface replacement put the library back.
codeunit 72321 "RAD NsFree Ovl Bystander"
{
    procedure Hold(Lib: Codeunit "RAD NsFree Ovl Lib"): Integer
    begin
        exit(Lib.Sibling(17));
    end;
}
