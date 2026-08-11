namespace AlRunner.Tests.RadTwentyObject;

page 71001 "RAD Perf Line List"
{
    PageType = List;
    SourceTable = "RAD Perf Line";

    layout
    {
        area(Content)
        {
            repeater(Lines)
            {
                field(EntryNo; Rec."Entry No.") { ApplicationArea = All; }
                field(HeaderNo; Rec."Header No.") { ApplicationArea = All; }
            }
        }
    }
}
