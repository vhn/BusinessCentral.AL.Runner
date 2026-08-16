// IncludedPermissionSets, W — the W this shape WOULD use: including the bystander is what
// would force its own include list to resolve. No test drives it; see
// PermMiddle.PermissionSet.al.
permissionset 72157 "BN Perm Top"
{
    Assignable = true;
    Caption = 'perm-top-v1';
    IncludedPermissionSets = "BN Perm Middle";
    Permissions = codeunit "BN Perm Probe" = X;
}
