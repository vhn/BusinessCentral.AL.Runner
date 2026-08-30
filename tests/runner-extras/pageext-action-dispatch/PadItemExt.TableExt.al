tableextension 64528 "Pad Item Ext" extends Item
{
    fields
    {
        field(64528; "Pad Has Variants"; Boolean)
        {
            CalcFormula = Exist("Item Variant" where("Item No." = field("No.")));
            FieldClass = FlowField;
        }
    }
}
