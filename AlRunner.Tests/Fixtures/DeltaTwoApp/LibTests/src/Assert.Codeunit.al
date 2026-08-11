codeunit 60950 "Delta Assert"
{
    procedure AreEqual(Expected: Integer; Actual: Integer; Message: Text)
    begin
        if Expected <> Actual then
            Error('%1 returned %2, expected %3', Message, Actual, Expected);
    end;
}
