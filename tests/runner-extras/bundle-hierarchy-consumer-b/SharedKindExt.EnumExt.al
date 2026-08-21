/// <summary>
/// The second enumextension on the shared library's enum, from a different level of
/// the hierarchy than HCA Shared Kind Ext. Both values must be reachable from the
/// test app, which depends on the library and on both extending apps.
/// </summary>
enumextension 64582 "HCB Shared Kind Ext" extends "HSL Shared Kind"
{
    value(64585; "HCB Beta Kind") { Caption = 'HCB Beta Kind'; }
}
