namespace AlRunner.Tests.RadBulkSwitch;

page 71200 "Bulk Switch Header Card"
{
    PageType = Card;
    SourceTable = "Bulk Switch Header";
    Caption = 'Bulk Switch Header (v1)';

    layout
    {
        area(Content)
        {
            group(General)
            {
                field("Code"; Rec.Code) { ApplicationArea = All; }
                field(Value; Rec.Value) { ApplicationArea = All; }
            }
        }
    }
}
