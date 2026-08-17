namespace AlRunner.Tests.RadProducerSurface;

// The second subtype a parameter names — `Codeunit "Producer Helper"` — and the object the
// "edit something else" scenario touches, so the probe is carried forward untouched.
codeunit 72201 "Producer Helper"
{
    procedure Helped(): Integer
    begin
        exit(7);
    end;
}
