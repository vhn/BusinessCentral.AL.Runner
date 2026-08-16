// Permissions, V — the bystander of a shape RadByNameCleanShapesTests deliberately does NOT
// test. This triple exists to record WHY.
//
// Measured against this pipeline: `Permissions = tabledata "<a table that does not exist>" =
// RIMD;` compiles silently. So a cold compile cannot distinguish a surviving by-name reference
// from a destroyed one, and this suite's oracle — "the delta says what a cold compile of the
// same tree says" — is satisfied by both. A test here would be green whether or not the shape
// works, which is exactly the failure mode the triple design exists to prevent.
//
// A table rather than a codeunit is nonetheless the right target to have left here: a modified
// codeunit whose surface fingerprint moves is admitted to `changedSurfaces`, so it could widen
// the cycle and recompile the bystander. Whoever builds a real oracle for this shape inherits
// that decision already made.
permissionset 72161 "BN Perms Holder"
{
    Assignable = false;
    Caption = 'BN Perms Holder';
    Permissions = tabledata "BN Perms Table" = RIMD;
}
