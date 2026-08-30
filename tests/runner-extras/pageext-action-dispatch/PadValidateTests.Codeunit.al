codeunit 64527 "Pad Validate Tests"
{
    Subtype = Test;
    TestPermissions = Disabled;

    var
        Assert: Codeunit "Pad Assert";

    [Test]
    procedure ModifiedFieldOnPrecompiledPageRunsOnAfterValidate()
    var
        PaymentTerm: Record "Payment Terms";
        PaymentTerms: TestPage "Payment Terms";
        Row: Record "Pad Row";
    begin
        Row.DeleteAll();
        PaymentTerm.DeleteAll();

        PaymentTerms.OpenEdit();
        PaymentTerms.New();
        PaymentTerms.Code.SetValue('PT1');
        PaymentTerms.Description.SetValue('From test page');
        PaymentTerms.Close();

        Assert.IsTrue(Row.Get('VALIDATE-PT1'),
            'SetValue must run a pageextension modify(control) OnAfterValidate trigger on a precompiled base page');
    end;

    [Test]
    procedure TableExtensionExistsFlowFieldFiltersPrecompiledSourceTable()
    var
        Item: Record Item;
        ItemVariant: Record "Item Variant";
    begin
        Item.Init();
        Item."No." := 'PAD-EXISTS';
        Item.Insert(false);

        ItemVariant.Init();
        ItemVariant."Item No." := Item."No.";
        ItemVariant.Code := 'V1';
        ItemVariant.Insert(false);

        ItemVariant.SetRange("Item No.", Item."No.");
        Assert.IsFalse(ItemVariant.IsEmpty(),
            'The normal source-table filter must see the inserted Item Variant');

        Item.Get(Item."No.");
        Item.CalcFields("Pad Has Variants");

        Assert.IsTrue(Item."Pad Has Variants",
            'An Exists FlowField contributed by a tableextension must filter a precompiled source table');
    end;

    [Test]
    procedure SetCurrentKeyOrdersTemporaryRowsBySecondaryKey()
    var
        Row: Record "Pad Sort Row" temporary;
    begin
        Row.Init();
        Row.Type := 'T';
        Row."Table" := 'X';
        Row.Value := 'A';
        Row."Sort Order" := 2;
        Row.Insert();

        Row.Init();
        Row.Type := 'T';
        Row."Table" := 'X';
        Row.Value := 'Z';
        Row."Sort Order" := 1;
        Row.Insert();

        Row.SetCurrentKey(Type, "Table", "Sort Order");
        Row.FindFirst();

        Assert.AreEqual('Z', Row.Value,
            'FindFirst on a temporary record must honor the selected secondary key');
    end;

    [Test]
    procedure SetCurrentKeyUsesPrimaryKeyToBreakSecondaryKeyTies()
    var
        Row: Record "Pad Sort Row" temporary;
    begin
        Row.Init();
        Row.Type := 'T';
        Row."Table" := 'X';
        Row.Value := 'Z';
        Row."Sort Order" := 0;
        Row.Insert();

        Row.Init();
        Row.Type := 'T';
        Row."Table" := 'X';
        Row.Value := 'A';
        Row."Sort Order" := 0;
        Row.Insert();

        Row.SetCurrentKey(Type, "Table", "Sort Order");
        Row.FindSet();

        Assert.AreEqual('A', Row.Value,
            'FindSet must order a secondary-key tie by the remaining primary-key fields');
        Row.Next();
        Assert.AreEqual('Z', Row.Value,
            'FindSet must preserve the selected key order while advancing');
    end;

}
