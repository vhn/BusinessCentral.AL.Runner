namespace AlRunner.Tests.RadSameAppOverload;

// The CALLEE. `Which` ships with a single Decimal overload, and the caller's Integer
// argument binds to it by widening.
//
// Adding `Which(Seed: Integer)` beside it is the whole experiment. `CalculateMethodId` is
// method-local, so THIS method keeps its id and keeps its `case` label in the re-emitted
// codeunit — what moves is the id the CALLER bakes. An un-rebound caller therefore
// dispatches a member that still exists and gets the previous overload's answer, with no
// exception and no diagnostic to announce it.
//
// `Sibling` is never called by anything and exists for the id-contract control: it is the
// "different method on the same object" whose id adding an overload must not move, and the
// one whose parameter type can be retyped without breaking any call site.
codeunit 72300 "RAD Ovl Lib"
{
    procedure Which(Seed: Decimal): Text
    begin
        exit('DECIMAL');
    end;

    procedure Sibling(Value: Integer): Integer
    begin
        exit(Value);
    end;
}
