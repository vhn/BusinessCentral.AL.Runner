/// <summary>
/// This app's enumextension on the shared library's enum. HCB Shared Kind Ext adds a
/// second value to the same base enum from a different level of the hierarchy; both
/// must survive the merge in `EnumMetadataPatches.TryGet`.
/// </summary>
enumextension 64572 "HCA Shared Kind Ext" extends "HSL Shared Kind"
{
    value(64575; "HCA Alpha Kind") { Caption = 'HCA Alpha Kind'; }
}
