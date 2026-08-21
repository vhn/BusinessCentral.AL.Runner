/// <summary>
/// The apex. ComputeTotal reaches the shared library twice in one call — once via HCB
/// (which goes through HCA) and once via HCA directly — so the trail it returns is a
/// transcript of the whole diamond, and the total is the sum of every app's own
/// constant along both paths. No single edge in the graph can produce either value on
/// its own.
///
/// Seed writes BOTH ancestors' extension fields on the shared library's table, which
/// only compiles if this app's symbol closure carries the tableextensions of two
/// different siblings.
/// </summary>
codeunit 64590 "HCC Consumer Api"
{
    procedure ComputeTotal(var Trail: Text; Value: Integer): Integer
    var
        Alpha: Codeunit "HCA Consumer Api";
        Beta: Codeunit "HCB Consumer Api";
        ViaBeta: Integer;
        ViaAlpha: Integer;
    begin
        Trail += 'C';
        ViaBeta := Beta.Contribute(Trail, Value);
        ViaAlpha := Alpha.Contribute(Trail, Value);
        exit(ViaBeta + ViaAlpha + 300);
    end;

    procedure Seed(EntryCode: Code[20])
    var
        Ledger: Record "HSL Shared Ledger";
    begin
        Ledger.Init();
        Ledger."Entry Code" := EntryCode;
        Ledger."Source App" := 'HCC';
        Ledger."Entry Weight" := 33;
        Ledger."HCA Alpha Note" := 'APEX-WROTE-A';
        Ledger."HCB Beta Score" := 330;
        Ledger.Insert();
    end;

    procedure OwnModuleName(): Text
    var
        Info: ModuleInfo;
    begin
        NavApp.GetCurrentModuleInfo(Info);
        exit(Info.Name());
    end;

    /// <summary>
    /// The shared library's own module identity, asked for from three levels above it.
    /// A library reached through a deep sibling chain must still answer with its own
    /// name, not the apex's.
    /// </summary>
    procedure SharedLibModuleNameSeenFromApex(): Text
    var
        Math: Codeunit "HSL Shared Math";
    begin
        exit(Math.OwnModuleName());
    end;
}
