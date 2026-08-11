codeunit 60961 "Delta Bridge"
{
    procedure Answer(): Integer
    var
        Lib: Codeunit "Delta Lib Answer";
    begin
        exit(Lib.Answer());
    end;
}
