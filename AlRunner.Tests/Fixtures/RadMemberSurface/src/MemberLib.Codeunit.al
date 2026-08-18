namespace AlRunner.Tests.RadMemberSurface;

// The hub. Every edit in RadMemberSurfaceTests lands here, and the question each one asks is
// the same: does this change anything a CALLER was compiled against?
//
// Three members, chosen so that each test can edit one without disturbing the others:
//   * `Pick`  — the overload seat. Adding `Pick(Integer)` beside it moves no existing id, and
//                is exactly the edit that must still rebind.
//   * `Tag`   — the one the caller actually calls. Its access, attributes and parameter names
//                are the id-invisible contract changes.
//   * `Ids`   — called by nothing, so its return type can be retyped and it can be deleted
//                outright without breaking the tree.
codeunit 72400 "RAD Member Lib"
{
    procedure Pick(Seed: Decimal): Text
    begin
        exit('DECIMAL');
    end;

    procedure Tag(Prefix: Text): Text
    begin
        exit(Prefix + '-TAG');
    end;

    procedure Ids(): List of [Integer]
    var
        Found: List of [Integer];
    begin
        Found.Add(1);
        exit(Found);
    end;
}
