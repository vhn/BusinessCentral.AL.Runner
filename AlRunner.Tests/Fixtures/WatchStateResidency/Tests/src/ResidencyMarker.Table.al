// Written by the manual-binding subscriber when it fires. A row here after the test
// published its probe event means SOME instance of the subscriber was bound at that
// moment — which is the whole observation this bundle exists to make.
table 60980 "Watch Residency Marker"
{
    fields
    {
        field(1; "No."; Code[20]) { }
    }

    keys
    {
        key(PK; "No.") { Clustered = true; }
    }
}
