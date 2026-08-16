// PageExtension.TargetObject, W — in the same delta, and it modifies the control the
// BYSTANDER contributed. That control is only on the target page because the bystander's
// `TargetObject` resolved, so this modify is the by-name reference becoming observable.
pageextension 72172 "BN PageExt Caller" extends "BN PageExt Page"
{
    layout
    {
        modify(BystanderMarker)
        {
            Visible = true;
        }
    }
}
