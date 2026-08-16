// Permissions, X — the table a bystander permission set grants access to by name. NOT
// EXERCISED BY ANY TEST; the reason is on the bystander, PermsHolder.PermissionSet.al.
table 72160 "BN Perms Table"
{
    DataClassification = CustomerContent;

    fields
    {
        field(1; "Entry No."; Integer) { DataClassification = CustomerContent; }
        field(2; Description; Text[50]) { DataClassification = CustomerContent; }
    }

    keys
    {
        key(PK; "Entry No.") { Clustered = true; }
    }

    trigger OnInsert()
    begin
        Description := 'perms-table-v1';
    end;
}
