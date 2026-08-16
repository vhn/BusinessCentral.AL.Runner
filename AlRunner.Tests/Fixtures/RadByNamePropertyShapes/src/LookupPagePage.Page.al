// LookupPageId/DrillDownPageId, X — the page both properties on the bystander table name.
// Stripped by the edit.
page 72140 "BN LookupPage Page"
{
    PageType = List;
    SourceTable = "BN LookupPage Table";
    Caption = 'lookup-v1';

    layout
    {
        area(Content)
        {
            repeater(Lines)
            {
                field(No; Rec."No.") { ApplicationArea = All; }
                field(Description; Rec.Description) { ApplicationArea = All; }
            }
        }
    }
}
