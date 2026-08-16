namespace AlRunner.Tests.RadByNameEnumExtTarget;

// V — the BYSTANDER. Never edited, so it is never in a delta and is always resolved from
// the packaged baseline. Its serialized `TargetObject` names the enum BY NAME ("EnumExt
// Base"), and the value it contributes — "Extended" — only exists on the merged surface of
// that target. That by-name reference is the whole shape under test.
enumextension 72081 "EnumExt Ext" extends "EnumExt Base"
{
    value(72081; Extended) { }
}
