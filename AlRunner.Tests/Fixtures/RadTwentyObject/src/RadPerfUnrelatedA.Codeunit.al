namespace AlRunner.Tests.RadTwentyObject;

codeunit 71002 "RAD Perf Unrelated A"
{
    procedure Value(): Integer
    var
        Caller: Codeunit "RAD Perf Caller";
    begin
        exit(Caller.Value());
    end;
}
