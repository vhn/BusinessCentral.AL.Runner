// RunObject, V — the BYSTANDER. Untouched, so its action set is resolved from the packaged
// baseline, and the `OpenTarget` action's `RunObject` names the target page by name.
page 72136 "BN RunObject Host"
{
    PageType = Card;

    layout
    {
        area(Content)
        {
            group(General)
            {
                field(HostMarker; HostMarker)
                {
                    ApplicationArea = All;
                    Caption = 'Host Marker';
                }
            }
        }
    }

    actions
    {
        area(Processing)
        {
            action(OpenTarget)
            {
                ApplicationArea = All;
                Caption = 'Open Target';
                RunObject = page "BN RunObject Target";
            }
        }
    }

    var
        HostMarker: Integer;
}
