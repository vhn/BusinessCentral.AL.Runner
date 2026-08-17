namespace AlRunner.Tests.RadProducerSurface;

// A direct caller of the probe. It is what turns a producer disagreement into an observable
// cost: `changedSurfaces` compares the probe's fingerprint across the two producers, and any
// difference pulls this file into the same cycle and re-emits it.
codeunit 72203 "Producer Caller"
{
    procedure Call(): Integer
    var
        Probe: Codeunit "Producer Probe";
    begin
        exit(Probe.NoParams() + Probe.Pick(9));
    end;
}
