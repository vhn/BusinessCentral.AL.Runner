// report RelatedTable, W — the tightest bind this shape allows. The added column's source
// expression `Description` is resolved AGAINST the dataitem's table, and the only way the
// compiler knows which table that is, is the bystander's by-name `RelatedTable`. If that
// reference did not survive the strip, this column would not bind.
reportextension 72167 "BN Report Host Ext" extends "BN Report Host"
{
    dataset
    {
        add(Data)
        {
            column(BnReportExtraV1; Description) { }
        }
    }
}
