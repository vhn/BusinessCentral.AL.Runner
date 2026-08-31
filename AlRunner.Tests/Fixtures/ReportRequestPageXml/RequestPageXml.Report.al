report 72481 "RPX Request Page XML"
{
    UsageCategory = None;

    dataset
    {
        dataitem(Row; "RPX Temp Row")
        {
            UseTemporary = true;

            column(RenderedValue; RenderedValue)
            {
            }

            trigger OnPreDataItem()
            begin
                Row.Init();
                Row."Entry No." := 1;
                Row.Value := 'row';
                Row.Insert();
            end;
        }
    }

    requestpage
    {
        layout
        {
            area(Content)
            {
                group(Options)
                {
                    field(Prefix; Prefix)
                    {
                        ApplicationArea = All;
                    }
                    field(FailOnPre; FailOnPre)
                    {
                        ApplicationArea = All;
                    }
                }
            }
        }
    }

    trigger OnPreReport()
    begin
        if FailOnPre then
            Error('OnPreReport must not run after the request page is cancelled.');
        RenderedValue := Prefix + '-row';
    end;

    var
        Prefix: Text[20];
        FailOnPre: Boolean;
        RenderedValue: Text[30];
}
