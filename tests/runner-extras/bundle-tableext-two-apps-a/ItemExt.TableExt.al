/// <summary>
/// Peer app A's tableextension on the platform table Item (27). Item's own base
/// metadata comes from the precompiled Base Application .app — no AL source in this
/// bundle declares Item itself.
///
/// A Boolean and a Code[20] on purpose: peer app B contributes an Integer and a
/// Code[20] in a disjoint id range, so a merge that collapses the two apps' field
/// lists cannot pass by accident — the surviving field would have the wrong type or
/// the wrong name.
/// </summary>
tableextension 64530 "BTA Item Ext" extends Item
{
    fields
    {
        field(64531; "BTA Alpha Flag"; Boolean) { DataClassification = CustomerContent; }
        field(64532; "BTA Alpha Tag"; Code[20]) { DataClassification = CustomerContent; }
    }
}
