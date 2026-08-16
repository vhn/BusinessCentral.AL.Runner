// query RelatedTable, W — each column read is assigned to a local of the SOURCE FIELD's exact
// type, so the assignment only type-checks if the compiler can still follow the bystander's
// dataitem back to the stripped table. A column whose type degraded would fail here.
codeunit 72177 "BN Query Caller"
{
    procedure Read(): Text
    var
        Host: Query "BN Query Host";
        Number: Code[20];
        Described: Text[50];
    begin
        Host.Open();
        if Host.Read() then begin
            Number := Host.QueryNo;
            Described := Host.QueryDescription;
        end;
        Host.Close();
        exit('query-caller-v1' + Number + Described);
    end;
}
