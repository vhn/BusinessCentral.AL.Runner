// LookupPageId/DrillDownPageId, W — in the same delta, and the only bind AL offers against
// an OBJECT-level property: declaring the bystander table forces its whole definition —
// including the two page names — to resolve out of the packaged baseline. There is no member
// to aim at the way `CalcFields` aims at a FlowField, and this comment says so rather than
// letting the test look tighter than it is.
codeunit 72142 "BN LookupPage Caller"
{
    procedure Describe(): Text[50]
    var
        Lookup: Record "BN LookupPage Table";
    begin
        Lookup."No." := 'lp-v1';
        Lookup.Description := 'lookup-caller-v1';
        exit(Lookup.Description);
    end;
}
