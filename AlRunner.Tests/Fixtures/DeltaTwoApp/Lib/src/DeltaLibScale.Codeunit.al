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
}
