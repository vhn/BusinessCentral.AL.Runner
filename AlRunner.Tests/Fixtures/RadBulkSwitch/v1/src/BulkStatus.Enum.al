namespace AlRunner.Tests.RadBulkSwitch;

enum 71200 "Bulk Switch Status"
{
    Extensible = true;

    value(1; Open) { Caption = 'Open'; }
    value(2; Closed) { Caption = 'Closed'; }
}
