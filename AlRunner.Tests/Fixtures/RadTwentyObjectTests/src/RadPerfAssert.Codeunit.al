namespace AlRunner.Tests.RadTwentyObjectTests;

codeunit 71201 "RAD Perf Assert"
{
    procedure AreEqualInt(Expected: Integer; Actual: Integer; Message: Text)
    begin
        if Expected <> Actual then
            Error('%1 returned %2, expected %3', Message, Actual, Expected);
    end;

    procedure AreEqualText(Expected: Text; Actual: Text; Message: Text)
    begin
        if Expected <> Actual then
            Error('%1 returned %2, expected %3', Message, Actual, Expected);
    end;
}
