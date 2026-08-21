/// <summary>
/// Extensible enum owned by the shared library and extended by two of its dependents
/// (HCA Shared Kind Ext adds 64575, HCB Shared Kind Ext adds 64585). The runner tracks
/// enumextensions in a registry keyed on the TARGET base enum id with no app qualifier
/// (`EnumMetadataPatches._extByTargetId`), merging every registered extension on read —
/// so "two apps extend one sibling-owned enum" is a shape whose correctness depends on
/// that merge keeping both apps' values, which is what HTS Hierarchy Tests pins.
/// </summary>
enum 64563 "HSL Shared Kind"
{
    Extensible = true;

    value(0; None) { Caption = 'None'; }
    value(1; Core) { Caption = 'Core'; }
}
