namespace AlRunner.Tests.RadByNameSelfSubtype;

// X — the object the delta strips out of the packaged baseline, AND the object that binds
// against the bystander's damaged surface. Both roles at once, which is the whole point:
// `_This` is a global of the hub's OWN type, so the argument it hands `Attach` names the
// very object this cycle removed from the packaged module.
//
// Copied in shape from npcore's `codeunit 6150705 "NPR POS Sale"`, which declares
// `_This: Codeunit "NPR POS Sale"` and passes it to four untouched codeunits' methods
// (`_SaleLine.Init(…, _This, …)`, `POSAfterSaleExecution.PosSaleCodeunitSet(_This)`).
codeunit 72120 "Self Subtype Hub"
{
    var
        _This: Codeunit "Self Subtype Hub";
        _Line: Codeunit "Self Subtype Line";

    procedure Bind(ThisIn: Codeunit "Self Subtype Hub")
    begin
        _This := ThisIn;
    end;

    procedure Start(): Integer
    begin
        exit(_Line.Attach(_This) + 1);
    end;
}
