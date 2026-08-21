/// <summary>
/// Level-2 of the chain. Contribute tags the trail and forwards into the shared
/// library, adding +1 so the constant is attributable to this app alone: any total
/// that omits 1 per traversal of this app failed to run its body.
///
/// Seed writes into the shared library's table, setting this app's OWN extension
/// field on it — a dependent extending and populating a sibling app's table.
/// </summary>
codeunit 64570 "HCA Consumer Api"
{
    procedure Contribute(var Trail: Text; Value: Integer): Integer
    var
        Math: Codeunit "HSL Shared Math";
    begin
        Trail += 'A';
        exit(Math.Visit(Trail, Value) + 1);
    end;

    procedure Seed(EntryCode: Code[20])
    var
        Ledger: Record "HSL Shared Ledger";
    begin
        Ledger.Init();
        Ledger."Entry Code" := EntryCode;
        Ledger."Source App" := 'HCA';
        Ledger."Entry Weight" := 11;
        Ledger."HCA Alpha Note" := 'NOTE-FROM-A';
        Ledger.Insert();
    end;

    procedure OwnModuleName(): Text
    var
        Info: ModuleInfo;
    begin
        NavApp.GetCurrentModuleInfo(Info);
        exit(Info.Name());
    end;
}
