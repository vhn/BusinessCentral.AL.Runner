xmlport 60988 "Watch Discovery Xml"
{
    Direction = Export;
    Format = Xml;

    /// <summary>
    /// Exists so a warm --watch cycle has an xmlport to export through. See the test that
    /// drives it for why: RecordPatches.RealXmlPortMetadata carries the same uncleared
    /// "already loaded" set that broke pages across a reload, and this is what establishes
    /// that the export nonetheless survives.
    /// </summary>
    schema
    {
        textelement(Root)
        {
            tableelement(Entry; "Watch Discovery Registry")
            {
                fieldelement(Code; Entry.Code) { }
                fieldelement(Description; Entry.Description) { }
            }
        }
    }
}
