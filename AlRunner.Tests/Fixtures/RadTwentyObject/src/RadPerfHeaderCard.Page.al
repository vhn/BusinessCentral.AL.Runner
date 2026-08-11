namespace AlRunner.Tests.RadTwentyObject;

page 71000 "RAD Perf Header Card"
{
    PageType = Card;
    SourceTable = "RAD Perf Header";

    layout
    {
        area(Content)
        {
            field(No; Rec."No.") { ApplicationArea = All; }
            field(Description; Rec.Description) { ApplicationArea = All; }
        }
    }

    actions
    {
        area(Processing)
        {
            action(SetDescription)
            {
                ApplicationArea = All;

                trigger OnAction()
                begin
                    Rec.Description := 'page-v1';
                end;
            }
        }
    }
}
