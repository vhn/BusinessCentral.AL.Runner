codeunit 60993 "Watch Discovery Assert"
{
    procedure IsTrue(Actual: Boolean; Message: Text)
    begin
        if not Actual then
            Error('%1 was false, expected true', Message);
    end;

    procedure IsFalse(Actual: Boolean; Message: Text)
    begin
        if Actual then
            Error('%1 was true, expected false', Message);
    end;

    procedure AreEqual(Expected: Text; Actual: Text; Message: Text)
    begin
        if Expected <> Actual then
            Error('%1 returned %2, expected %3', Message, Actual, Expected);
    end;

    /// <summary>
    /// Substring match against the last error, the way BC's own Assert.ExpectedError works.
    /// An exact compare cannot be used here: BC renders `\` in an error message as a line
    /// break, so the text a literal in this file can express is never the text
    /// GetLastErrorText() returns for a multi-line platform error.
    /// </summary>
    procedure ExpectedError(Expected: Text; Message: Text)
    begin
        if StrPos(GetLastErrorText(), Expected) = 0 then
            Error('%1 reported "%2", expected it to contain "%3"', Message, GetLastErrorText(), Expected);
    end;
}
