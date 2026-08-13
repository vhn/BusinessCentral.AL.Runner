namespace AlRunner.Tests.RadProfileApp;

page 71403 "RAD Profile Card"
{
    PageType = Card;

    layout
    {
        area(Content)
        {
            group(General)
            {
                field(Amount; Amount)
                {
                    ApplicationArea = All;
                    Caption = 'Amount';
                }
            }
        }
    }

    var
        Amount: Integer;
}
