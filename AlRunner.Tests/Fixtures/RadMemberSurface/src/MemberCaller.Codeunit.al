namespace AlRunner.Tests.RadMemberSurface;

// The DIRECT caller — the object a moved surface on the library must pull in, and the one an
// unmoved surface must leave alone.
//
// `[NonDebuggable]` is not decoration. It takes no arguments, and an argument-less method
// attribute is the one place the two producers a delta compares disagree: the converter leaves
// `Arguments` null, the JSON round trip materialises it as `[]`. So when a library edit widens
// this cycle and THIS file is recompiled, the recursion step immediately re-asks whether the
// caller's own surface moved — against the exact shape that reads as changed if the member diff
// is built on the raw serialization. That is what makes
// WideningTheCaller_DoesNotThenWidenItsOwnCaller a real test rather than a restatement.
codeunit 72401 "RAD Member Caller"
{
    [NonDebuggable]
    procedure Call(): Text
    var
        Lib: Codeunit "RAD Member Lib";
    begin
        exit(Lib.Tag('CALL'));
    end;
}
