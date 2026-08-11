namespace AlRunner.Tests.RadTwentyObject;

query 71000 "RAD Perf Header Query"
{
    QueryType = Normal;
    TopNumberOfRows = 10;

    elements
    {
        dataitem(Header; "RAD Perf Header")
        {
            column(No; "No.") { }
            column(Description; Description) { }
        }
    }
}
