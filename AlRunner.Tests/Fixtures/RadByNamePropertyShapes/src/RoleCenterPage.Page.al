// RoleCenter, X — the page the bystander profile names. Stripped by the edit.
//
// This is the shipped branch nothing pinned: RadProfileApp has a profile with a `RoleCenter`,
// but no test ever touches the page it names, so the delta path's handling of a stripped
// role-centre page was never exercised.
page 72150 "BN RoleCenter Page"
{
    PageType = RoleCenter;
    Caption = 'rolecenter-v1';

    layout
    {
        area(RoleCenter)
        {
        }
    }
}
