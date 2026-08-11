namespace AlRunner.Tests.RadTwentyObject;

codeunit 71000 "RAD Perf Service"
{
    procedure Value(): Integer
    begin
        exit(40);
    end;

    procedure Coerce(Input: Integer): Integer
    begin
        exit(39);
    end;
}
