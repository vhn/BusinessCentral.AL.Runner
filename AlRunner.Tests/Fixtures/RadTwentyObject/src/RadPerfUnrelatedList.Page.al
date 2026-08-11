namespace AlRunner.Tests.RadTwentyObject;

page 71002 "RAD Perf Unrelated List"
{
    PageType = List;
    SourceTable = "RAD Perf Unrelated";

    layout
    {
        area(Content)
        {
            repeater(Rows)
            {
                field(Code; Rec.Code) { ApplicationArea = All; }
                field(Description; Rec.Description) { ApplicationArea = All; }
            }
        }
    }
}
