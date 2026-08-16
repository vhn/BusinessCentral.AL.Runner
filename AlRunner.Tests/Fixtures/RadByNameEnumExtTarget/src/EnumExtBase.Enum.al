namespace AlRunner.Tests.RadByNameEnumExtTarget;

// X — the object the delta strips out of the packaged baseline. An enum is never admitted
// by `changedSurfaces` (only codeunits and id-less kinds are), so every untouched object
// whose serialized surface names it BY NAME — like an enumextension's `TargetObject` — is
// resolved against a packaged module that no longer carries it.
enum 72080 "EnumExt Base"
{
    Extensible = true;

    value(1; Value1) { }
    value(2; Value2) { }
}
