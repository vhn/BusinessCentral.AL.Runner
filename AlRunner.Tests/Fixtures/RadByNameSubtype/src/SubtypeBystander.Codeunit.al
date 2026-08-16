namespace AlRunner.Tests.RadByNameSubtype;

// V — the BYSTANDER. Never edited, so it is never in a delta and is always resolved from
// the packaged baseline. Its serialized surface names the table by name: the parameter's
// `TypeDefinition.Subtype` is the string "Subtype Target". That single by-name reference
// is the whole shape under test.
codeunit 72001 "Subtype Bystander"
{
    procedure Take(var Target: Record "Subtype Target"): Integer
    begin
        exit(Target."Entry No.");
    end;
}
