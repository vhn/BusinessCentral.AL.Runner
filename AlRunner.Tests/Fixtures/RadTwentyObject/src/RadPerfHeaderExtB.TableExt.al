namespace AlRunner.Tests.RadTwentyObject;

tableextension 71001 "RAD Perf Header Ext B" extends "RAD Perf Header"
{
    fields
    {
        field(71001; "Extension B"; Text[30])
        {
            DataClassification = SystemMetadata;

            trigger OnValidate()
            begin
                Rec.Description := 'extension-b-v1';
            end;
        }
    }
}
