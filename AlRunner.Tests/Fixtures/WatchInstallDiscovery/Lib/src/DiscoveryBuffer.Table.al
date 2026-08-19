table 60987 "Watch Discovery Buffer"
{
    /// <summary>
    /// Somewhere to point an xmlport's OutStream at. A Blob field rather than a System
    /// Application "Temp Blob" because this bundle resolves no Microsoft dependencies — see
    /// its app.json.
    /// </summary>
    fields
    {
        field(1; Code; Code[20]) { }
        field(2; Payload; Blob) { }
    }

    keys
    {
        key(PK; Code) { Clustered = true; }
    }
}
