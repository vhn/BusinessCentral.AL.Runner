// report RelatedTable, V — the BYSTANDER. Untouched, so its dataitem is resolved from the
// packaged baseline, where the source table is recorded by name.
report 72166 "BN Report Host"
{
    ProcessingOnly = true;
    Caption = 'BN Report Host';

    dataset
    {
        dataitem(Data; "BN Report Table")
        {
            column(HostNo; "No.") { }
        }
    }
}
