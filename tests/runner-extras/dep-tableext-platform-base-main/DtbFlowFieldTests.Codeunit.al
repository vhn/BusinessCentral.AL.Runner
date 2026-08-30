codeunit 63412 "DTB FlowField Tests"
{
    Subtype = Test;
    TestPermissions = Disabled;

    var
        Assert: Codeunit "DTB Assert";

    [Test]
    procedure DependencyTableExtensionExistsFlowFieldFiltersPrecompiledSourceTable()
    var
        Item: Record Item;
        ItemVariant: Record "Item Variant";
    begin
        Item.Init();
        Item."No." := 'DTB-EXISTS';
        Item.Insert(false);

        ItemVariant.Init();
        ItemVariant."Item No." := Item."No.";
        ItemVariant.Code := 'V1';
        ItemVariant.Insert(false);

        ItemVariant.SetRange("Item No.", Item."No.");
        Assert.AreEqual(false, ItemVariant.IsEmpty(),
            'The normal source-table filter must see the inserted Item Variant');

        Item.Get(Item."No.");
        Item.CalcFields("DTB Has Variants");

        Assert.AreEqual(true, Item."DTB Has Variants",
            'A dependency tableextension Exists FlowField must preserve its CalcFormula');
    end;
}
