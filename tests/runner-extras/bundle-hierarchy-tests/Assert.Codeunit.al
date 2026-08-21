codeunit 64600 "HTS Assert"
{
    procedure AreEqual(Expected: Integer; Actual: Integer; Msg: Text)
    begin
        if Expected <> Actual then
            Error('Expected %1 but got %2: %3', Expected, Actual, Msg);
    end;

    procedure AreEqual(Expected: Text; Actual: Text; Msg: Text)
    begin
        if Expected <> Actual then
            Error('Expected ''%1'' but got ''%2'': %3', Expected, Actual, Msg);
    end;

    procedure IsTrue(Actual: Boolean; Msg: Text)
    begin
        if not Actual then
            Error('Expected true but got false: %1', Msg);
    end;

    procedure ExpectedErrorContains(ErrorText: Text; Fragment: Text; Msg: Text)
    begin
        if not ErrorText.Contains(Fragment) then
            Error('Expected error text to contain ''%1'' but got ''%2'': %3', Fragment, ErrorText, Msg);
    end;
}
