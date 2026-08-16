// RunObject, X — the page the bystander's action runs. Stripped by the edit.
page 72135 "BN RunObject Target"
{
    PageType = Card;

    layout
    {
        area(Content)
        {
            group(General)
            {
                field(Marker; Marker)
                {
                    ApplicationArea = All;
                    Caption = 'Marker';
                }
            }
        }
    }

    var
        Marker: Integer;

    trigger OnOpenPage()
    begin
        Marker := 1;
    end;
}
