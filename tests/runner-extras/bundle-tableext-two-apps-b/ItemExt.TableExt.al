/// <summary>
/// Peer app B's tableextension on the same platform table Item (27) that peer app A
/// extends. Field ids are in B's own declared range, disjoint from A's, exactly as two
/// separately-published extensions would be in a real tenant.
/// </summary>
tableextension 64540 "BTB Item Ext" extends Item
{
    fields
    {
        field(64541; "BTB Beta Count"; Integer) { DataClassification = CustomerContent; }
        field(64542; "BTB Beta Tag"; Code[20]) { DataClassification = CustomerContent; }
    }
}
