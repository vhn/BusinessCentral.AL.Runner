// PageExtension.TargetObject, V — the BYSTANDER. Untouched, so the delta reads its serialized
// PageExtensionDefinition, whose `TargetObject` names the stripped page by name. What it
// contributes — the `BystanderMarker` control — only reaches the target page through that
// reference.
pageextension 72171 "BN PageExt Bystander" extends "BN PageExt Page"
{
    layout
    {
        addlast(General)
        {
            field(BystanderMarker; BystanderMarker)
            {
                ApplicationArea = All;
                Caption = 'Bystander Marker';
            }
        }
    }

    var
        BystanderMarker: Integer;
}
