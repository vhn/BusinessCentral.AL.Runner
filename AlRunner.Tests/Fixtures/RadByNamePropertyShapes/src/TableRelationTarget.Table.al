// TableRelation, X — NOT EXERCISED BY ANY TEST. The reason is on the bystander,
// TableRelationBystander.Table.al.
table 72125 "BN TableRelation Target"
{
    DataClassification = CustomerContent;

    fields
    {
        field(1; "Code"; Code[20]) { DataClassification = CustomerContent; }
        field(2; Description; Text[50]) { DataClassification = CustomerContent; }
    }

    keys
    {
        key(PK; "Code") { Clustered = true; }
    }

    trigger OnInsert()
    begin
        Description := 'tablerelation-v1';
    end;
}
