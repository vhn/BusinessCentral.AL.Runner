// PageExtension.TargetObject, X — the page the bystander pageextension targets. Stripped by
// the edit; `TargetObject` is the by-name reference under test.
page 72170 "BN PageExt Page"
{
    PageType = Card;
    Caption = 'pageext-page-v1';

    layout
    {
        area(Content)
        {
            group(General)
            {
                field(BaseMarker; BaseMarker)
                {
                    ApplicationArea = All;
                    Caption = 'Base Marker';
                }
            }
        }
    }

    var
        BaseMarker: Integer;
}
