// A second callable surface in the library app, deliberately in a file of its own.
// Watch_EditingTheLibraryApp_RecompilesOnlyThatObject_AndRunsTheNewCode overwrites
// DeltaLib.Codeunit.al wholesale, and the cross-app caller in Delta Bridge must not be
// something that overwrite can delete.
codeunit 60922 "Delta Lib Scale"
{
    procedure Scaled(Factor: Integer): Integer
    begin
        exit(42 * Factor);
    end;

    // One overload only, deliberately. Delta Bridge calls this with an INTEGER, which binds
    // here by widening — so adding a `Pick(Seed: Integer)` overload later moves which id the
    // caller bakes WITHOUT moving this method's own id, and this method's `case` label
    // survives in the callee. That is the silent half of the cross-app staleness bug:
    // Watch_AddingAnOverloadInOneApp_RebindsItsCrossAppCaller.
    procedure Pick(Seed: Decimal): Integer
    begin
        exit(1);
    end;
}
