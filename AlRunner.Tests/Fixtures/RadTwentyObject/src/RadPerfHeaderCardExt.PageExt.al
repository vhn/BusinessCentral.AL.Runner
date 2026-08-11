namespace AlRunner.Tests.RadTwentyObject;

pageextension 71000 "RAD Perf Header Card Ext" extends "RAD Perf Header Card"
{
    layout
    {
        addlast(Content)
        {
            field(ExtensionA; Rec."Extension A") { ApplicationArea = All; }
        }
    }

    actions
    {
        addlast(Processing)
        {
            action(SetExtensionDescription)
            {
                ApplicationArea = All;

                trigger OnAction()
                begin
                    Rec.Description := 'pageextension-v1';
                end;
            }
        }
    }
}
