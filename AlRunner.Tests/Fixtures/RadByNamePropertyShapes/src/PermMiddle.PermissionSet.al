// IncludedPermissionSets, V — the bystander of a shape RadByNameCleanShapesTests deliberately
// does NOT test. This triple exists to record WHY.
//
// Measured against this pipeline: `IncludedPermissionSets = "<a set that does not exist>"`
// compiles silently. So a cold compile cannot distinguish a surviving by-name reference from a
// destroyed one, and this suite's oracle — "the delta says what a cold compile of the same tree
// says" — is satisfied by both. A test here would be green whether or not the shape works.
//
// Same conclusion as `Permissions` (PermsHolder.PermissionSet.al): permission-set references
// are bound leniently, so the compile-diagnostic oracle cannot adjudicate either of them.
permissionset 72156 "BN Perm Middle"
{
    Assignable = false;
    Caption = 'BN Perm Middle';
    IncludedPermissionSets = "BN Perm Base";
    Permissions = codeunit "BN Perm Probe" = X;
}
