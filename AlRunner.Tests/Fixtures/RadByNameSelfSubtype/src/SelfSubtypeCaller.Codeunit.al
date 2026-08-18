namespace AlRunner.Tests.RadByNameSelfSubtype;

// W — the CALLER, and the control for "is the self-reference the load-bearing part?".
// It binds `Attach` exactly as the hub does, but the argument is a local of the hub's type
// rather than the hub itself, so a cycle that edits W instead of the hub reproduces
// RadByNameSubtypeTests' triple with one variable changed: the bystander's parameter is a
// Codeunit subtype, not a Record subtype.
codeunit 72122 "Self Subtype Caller"
{
    procedure Call(): Integer
    var
        Line: Codeunit "Self Subtype Line";
        Hub: Codeunit "Self Subtype Hub";
    begin
        exit(Line.Attach(Hub) + 1);
    end;
}
