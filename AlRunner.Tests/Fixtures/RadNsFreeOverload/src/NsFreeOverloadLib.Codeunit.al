// NO `namespace` declaration, in this file or any other in the fixture. That is the whole
// point: BC binds a compilation unit with no namespace declaration through
// `LegacyInContainerBinder`, which resolves an object name against the PACKAGED module
// symbol's own copy of the surface — the copy that cannot see this app's source. Adding a
// namespace here would move every file in this fixture onto `NamespaceContainerBinder` and the
// scenario would stop reproducing.
//
// X — the only file the scenario edits, and the object that binds against the damaged surface.
// Both roles at once, copied in shape from npcore's `codeunit 6150705 "NPR POS Sale"`, which
// declares `_This: Codeunit "NPR POS Sale"` and hands it to untouched codeunits' methods.
//
// Two properties of the edit are load-bearing:
//
//  * it is BODY-ONLY, so the serialized surface does not move, so no direct user is rebound and
//    the bystander stays on the packaged baseline holding a reference to the codeunit this delta
//    strips. An edit that moved the surface would rebind every direct user, bystander included,
//    and leave nothing dangling to repair;
//  * the binding site is HERE. `_Bystander.Hold(_This)` is in this codeunit's own body, so
//    editing this file is what puts the question to the bystander's damaged parameter. A fixture
//    whose only binding site sits in an un-edited file never asks it, and goes green proving
//    nothing.
codeunit 72320 "RAD NsFree Ovl Lib"
{
    var
        _This: Codeunit "RAD NsFree Ovl Lib";
        _Bystander: Codeunit "RAD NsFree Ovl Bystander";

    procedure Which(Seed: Decimal): Text
    begin
        // The round trip that only a repaired pass can complete: out to the un-rebound
        // bystander, whose loaded IL then dispatches `Sibling` back into THIS re-emitted
        // codeunit by the member id it baked at the cold compile.
        if _Bystander.Hold(_This) <> 17 then
            exit('HOLD-WRONG');
        exit('DECIMAL');
    end;

    procedure Sibling(Value: Integer): Integer
    begin
        exit(Value);
    end;
}
