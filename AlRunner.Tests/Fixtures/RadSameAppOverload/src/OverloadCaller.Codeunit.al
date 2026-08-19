namespace AlRunner.Tests.RadSameAppOverload;

// The CALLER, in the SAME app as the callee — the path a member-level surface diff
// rewrites, and the one the cross-app suite (RadDeltaWatchTests) does not cover.
//
// It passes an INTEGER to a method that today only has a Decimal overload, so overload
// resolution HERE is what changes when the library gains an Integer one — while this file
// stays byte-for-byte identical. It is therefore only ever re-emitted because the delta
// decided it must be.
codeunit 72301 "RAD Ovl Caller"
{
    procedure Call(): Text
    var
        Lib: Codeunit "RAD Ovl Lib";
        Seed: Integer;
    begin
        Seed := 2;
        exit(Lib.Which(Seed));
    end;
}
