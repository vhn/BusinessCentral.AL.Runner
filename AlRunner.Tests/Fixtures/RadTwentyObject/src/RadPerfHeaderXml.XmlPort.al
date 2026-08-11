namespace AlRunner.Tests.RadTwentyObject;

xmlport 71000 "RAD Perf Header Xml"
{
    Direction = Export;
    Format = Xml;

    schema
    {
        textelement(Root)
        {
            tableelement(Header; "RAD Perf Header")
            {
                fieldelement(No; Header."No.") { }
                fieldelement(Description; Header.Description) { }
            }
        }
    }

    procedure Marker(): Integer
    begin
        exit(1);
    end;
}
