// The only thing this app exists for is to be the app the watch test EDITS, so that the
// app holding the subscriber and the test stays warm on one assembly across cycles.
// That is the shape the leak was found in: the edited app and the app owning the leaked
// binding were different apps.
codeunit 60970 "Watch Residency Lib"
{
    procedure Ping(): Integer
    begin
        exit(1);
    end;
}
