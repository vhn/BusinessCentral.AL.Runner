namespace AlRunner.Tests.RadProducerSurface;

// The subtype a parameter names: `Record "Producer Target"` serializes the table as a
// string on the probe's surface, so both producers have to spell it the same way.
table 72200 "Producer Target"
{
    DataClassification = ToBeClassified;

    fields
    {
        field(1; "Entry No."; Integer) { DataClassification = SystemMetadata; }
        field(2; Description; Text[50]) { DataClassification = SystemMetadata; }
    }

    keys
    {
        key(PK; "Entry No.") { Clustered = true; }
    }
}
