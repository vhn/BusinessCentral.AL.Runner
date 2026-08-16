// RunObject, W — in the same delta, and `modify` names the very action whose `RunObject`
// points at the stripped page. Resolving the modify target is what pulls that action — the
// by-name reference and all — out of the bystander's packaged definition.
pageextension 72137 "BN RunObject Host Ext" extends "BN RunObject Host"
{
    actions
    {
        modify(OpenTarget)
        {
            Visible = true;
        }
    }
}
