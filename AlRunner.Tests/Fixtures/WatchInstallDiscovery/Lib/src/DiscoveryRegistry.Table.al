table 60990 "Watch Discovery Registry"
{
    fields
    {
        field(1; Code; Code[20]) { }
        field(2; Description; Text[50]) { }
    }

    keys
    {
        key(PK; Code) { Clustered = true; }
    }

    /// <summary>
    /// A table-declared integration event whose subscribers write through the publishing
    /// record. This is the npcore "NPR POS Sales Workflow".OnDiscoverPOSSalesWorkflows shape:
    /// the sender is the record itself, so a subscriber that receives a null sender throws
    /// NullReferenceException rather than quietly doing nothing.
    /// </summary>
    [IntegrationEvent(true, false)]
    internal procedure OnDiscoverEntries()
    begin
    end;

    internal procedure DiscoverEntry(NewCode: Code[20]; NewDescription: Text[50])
    begin
        if not Get(NewCode) then begin
            Init();
            Code := NewCode;
            Description := NewDescription;
            Insert(true);
        end;
    end;
}
