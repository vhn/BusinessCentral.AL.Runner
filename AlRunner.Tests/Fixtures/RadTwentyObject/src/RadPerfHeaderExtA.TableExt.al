namespace AlRunner.Tests.RadTwentyObject;

tableextension 71000 "RAD Perf Header Ext A" extends "RAD Perf Header"
{
    fields
    {
        field(71000; "Extension A"; Text[30])
        {
            DataClassification = SystemMetadata;

            trigger OnValidate()
            begin
                Rec.Description := 'extension-a-v1';
            end;
        }
    }
}
