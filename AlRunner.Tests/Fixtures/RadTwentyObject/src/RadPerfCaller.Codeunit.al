namespace AlRunner.Tests.RadTwentyObject;

codeunit 71001 "RAD Perf Caller"
{
    procedure Value(): Integer
    var
        Service: Codeunit "RAD Perf Service";
    begin
        exit(Service.Coerce(0));
    end;
}
