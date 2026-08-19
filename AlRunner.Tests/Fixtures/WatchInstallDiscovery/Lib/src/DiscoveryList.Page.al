page 60989 "Watch Discovery List"
{
    PageType = List;
    SourceTable = "Watch Discovery Registry";
    Editable = false;

    layout
    {
        area(Content)
        {
            repeater(Entries)
            {
                field(Code; Rec.Code) { ApplicationArea = All; }
                field(Description; Rec.Description) { ApplicationArea = All; }
            }
        }
    }

    /// <summary>
    /// npcore's "NPR Discount Priority List" and "NPR POS Cross Ref. Setup" both re-register
    /// their rows from OnOpenPage, which is why the seventeen drifted tests are all
    /// page-opening tests: they delete the rows, open the page, and assert the page's own
    /// trigger put them back.
    /// </summary>
    trigger OnOpenPage()
    begin
        Rec.OnDiscoverEntries();
    end;
}
