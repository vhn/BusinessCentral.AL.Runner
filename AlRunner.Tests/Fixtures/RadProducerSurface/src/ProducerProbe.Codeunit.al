namespace AlRunner.Tests.RadProducerSurface;

// One codeunit carrying one instance of every method shape a member-level surface diff has
// to align across the two producers that describe it (RadProducerEquivalenceTests). Nothing
// here is about what the methods DO — the bodies are constants — only about what their
// serialized symbol looks like on each side.
//
// Shapes, in declaration order: no parameters · a `var` parameter · a Record subtype · a
// Codeunit subtype · a generic List return · a generic Dictionary return · [TryFunction] ·
// [NonDebuggable] · [IntegrationEvent] · [EventSubscriber] · the same subscriber declared
// `local` · internal · local · and two overloads sharing the name `Pick`.
codeunit 72202 "Producer Probe"
{
    procedure NoParams(): Integer
    begin
        exit(1);
    end;

    procedure VarParam(var Value: Integer)
    begin
        Value := 2;
    end;

    procedure RecordSubtype(var Target: Record "Producer Target"): Integer
    begin
        exit(Target."Entry No.");
    end;

    procedure CodeunitSubtype(Helper: Codeunit "Producer Helper"): Integer
    begin
        exit(Helper.Helped());
    end;

    procedure GenericList(): List of [Integer]
    var
        Numbers: List of [Integer];
    begin
        Numbers.Add(3);
        exit(Numbers);
    end;

    procedure GenericDictionary(): Dictionary of [Text, Integer]
    var
        Map: Dictionary of [Text, Integer];
    begin
        Map.Add('four', 4);
        exit(Map);
    end;

    [TryFunction]
    procedure TryIt(Value: Integer)
    begin
        if Value < 0 then
            Error('negative');
    end;

    [NonDebuggable]
    procedure Hidden(): Integer
    begin
        exit(5);
    end;

    [IntegrationEvent(false, false)]
    procedure OnProbed(Value: Integer)
    begin
    end;

    // On the surface, because it is not local — and the only attribute here whose arguments
    // are STRINGS, one of them empty. An empty string is the other shape a JSON round trip
    // could plausibly normalise, so it has to be measured rather than assumed.
    [EventSubscriber(ObjectType::Codeunit, Codeunit::"Producer Probe", 'OnProbed', '', false, false)]
    procedure HandleProbed(Value: Integer)
    begin
    end;

    // The same attribute on a LOCAL method, which is absent from the surface entirely: an
    // attribute does not put a local method on the module's exported symbol surface.
    [EventSubscriber(ObjectType::Codeunit, Codeunit::"Producer Probe", 'OnProbed', '', false, false)]
    local procedure HandleProbedLocally(Value: Integer)
    begin
    end;

    internal procedure InternalOnly(): Integer
    begin
        exit(6);
    end;

    local procedure LocalOnly(): Integer
    begin
        exit(7);
    end;

    procedure Pick(Seed: Decimal): Integer
    begin
        exit(8);
    end;

    procedure Pick(Seed: Integer): Integer
    begin
        exit(9);
    end;
}
