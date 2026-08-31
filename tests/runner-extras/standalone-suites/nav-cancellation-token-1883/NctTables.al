/// Minimal fixture table for nav-cancellation-token-1883 (own ID range, see NctTests.Codeunit.al
/// for why this cluster is deleted rather than redirected).
table 60706 "NCT Item"
{
    DataClassification = SystemMetadata;

    fields
    {
        field(1; "No."; Code[20]) { }
        field(2; Value; Integer) { }
        field(3; "Owner Id"; Guid) { }
        field(4; Amount; Decimal) { }
        field(5; "Owned Amount"; Decimal)
        {
            FieldClass = FlowField;
            CalcFormula = sum("NCT Item".Amount where("Owner Id" = field(SystemId)));
        }
    }

    keys
    {
        key(PK; "No.") { Clustered = true; }
    }
}
