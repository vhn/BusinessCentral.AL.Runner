/// <summary>
/// The bottom of the transitive call chain. Every consumer's Contribute reaches
/// Visit, so the chain HTS -> HCC -> HCB -> HCA -> HSL crosses four app boundaries
/// before arriving here.
///
/// Visit appends its own tag to the by-ref trail before returning, which is what
/// makes the traversal observable as a literal transcript rather than only as a
/// sum: the apex consumer reaches HCA twice (directly and through HCB), so a
/// correct run records this library twice in one call.
/// </summary>
codeunit 64561 "HSL Shared Math"
{
    procedure Visit(var Trail: Text; Value: Integer): Integer
    begin
        Trail += 'L';
        exit(Value * 10);
    end;

    procedure OwnModuleName(): Text
    var
        Info: ModuleInfo;
    begin
        NavApp.GetCurrentModuleInfo(Info);
        exit(Info.Name());
    end;
}
