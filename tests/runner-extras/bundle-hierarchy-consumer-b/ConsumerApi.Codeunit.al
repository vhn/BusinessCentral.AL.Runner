/// <summary>
/// Level-3 of the chain: forwards through its co-dependent HCA rather than calling the
/// shared library directly, so this app's +20 can only appear in a total that also
/// contains HCA's +1 and the library's x10. That is the transitive-depth claim — the
/// bundle had no A -> B -> C edge at all before this fixture.
///
/// ReadAlphaNoteFrom is the sideways data read: this app reads the extension field
/// HCA declared, off a row HCA inserted into a table a third app owns.
/// </summary>
codeunit 64580 "HCB Consumer Api"
{
    procedure Contribute(var Trail: Text; Value: Integer): Integer
    var
        Alpha: Codeunit "HCA Consumer Api";
    begin
        Trail += 'B';
        exit(Alpha.Contribute(Trail, Value) + 20);
    end;

    procedure Seed(EntryCode: Code[20])
    var
        Ledger: Record "HSL Shared Ledger";
    begin
        Ledger.Init();
        Ledger."Entry Code" := EntryCode;
        Ledger."Source App" := 'HCB';
        Ledger."Entry Weight" := 22;
        Ledger."HCB Beta Score" := 220;
        Ledger.Insert();
    end;

    procedure ReadAlphaNoteFrom(EntryCode: Code[20]): Text
    var
        Ledger: Record "HSL Shared Ledger";
    begin
        Ledger.Get(EntryCode);
        exit(Ledger."HCA Alpha Note");
    end;

    procedure OwnModuleName(): Text
    var
        Info: ModuleInfo;
    begin
        NavApp.GetCurrentModuleInfo(Info);
        exit(Info.Name());
    end;
}
