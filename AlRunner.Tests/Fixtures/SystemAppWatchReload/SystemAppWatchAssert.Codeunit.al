codeunit 72500 "SystemPackage Watch Assert"
{
    procedure IsTrue(Value: Boolean; Message: Text)
    begin
        if not Value then
            Error('Assert.IsTrue failed. %1', Message);
    end;

    procedure IsFalse(Value: Boolean; Message: Text)
    begin
        if Value then
            Error('Assert.IsFalse failed. %1', Message);
    end;
}
