codeunit 60961 "Delta Bridge"
{
    procedure Answer(): Integer
    var
        Lib: Codeunit "Delta Lib Answer";
    begin
        exit(Lib.Answer());
    end;

    // Calls across the app boundary through a parameter, so that changing that parameter's
    // TYPE in Delta Lib moves the callee's member id while leaving this source valid — an
    // Integer argument widens to a Decimal parameter. That is the shape
    // Watch_MovingAMemberIdInOneApp_RebindsItsCrossAppCaller edits.
    procedure Scaled(): Integer
    var
        Lib: Codeunit "Delta Lib Scale";
        Factor: Integer;
    begin
        Factor := 2;
        exit(Lib.Scaled(Factor));
    end;

    // Passes an INTEGER to a method that currently only has a Decimal overload, so overload
    // resolution here is what changes when Delta Lib gains an Integer overload — while this
    // file stays byte-for-byte identical.
    procedure Pick(): Integer
    var
        Lib: Codeunit "Delta Lib Scale";
        Seed: Integer;
    begin
        Seed := 3;
        exit(Lib.Pick(Seed));
    end;
}
