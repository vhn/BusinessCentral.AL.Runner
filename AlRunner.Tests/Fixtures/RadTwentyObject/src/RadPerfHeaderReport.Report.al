namespace AlRunner.Tests.RadTwentyObject;

report 71000 "RAD Perf Header Report"
{
    ProcessingOnly = true;

    dataset
    {
        dataitem(Header; "RAD Perf Header")
        {
            column(No; "No.") { }
            column(Description; Description) { }
        }
    }

    trigger OnPreReport()
    begin
        Marker := 1;
    end;

    var
        Marker: Integer;
}
