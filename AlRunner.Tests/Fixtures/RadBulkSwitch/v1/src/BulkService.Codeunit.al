namespace AlRunner.Tests.RadBulkSwitch;

codeunit 71205 "Bulk Switch Service"
{
    procedure Compute(): Integer
    var
        Helper: Codeunit "Bulk Switch Helper B";
    begin
        exit(Helper.Base() + 2);
    end;

    procedure LineWeight(): Integer
    var
        Line: Record "Bulk Switch Line";
    begin
        exit(Line.Weight());
    end;

    procedure HighestStatus(): Integer
    var
        Status: Enum "Bulk Switch Status";
    begin
        exit(Status::Closed.AsInteger());
    end;
}
