tableextension 61883 "DTB Item FlowField Ext" extends Item
{
    fields
    {
        field(61883; "DTB Has Variants"; Boolean)
        {
            CalcFormula = Exist("Item Variant" where("Item No." = field("No.")));
            FieldClass = FlowField;
        }
    }
}
