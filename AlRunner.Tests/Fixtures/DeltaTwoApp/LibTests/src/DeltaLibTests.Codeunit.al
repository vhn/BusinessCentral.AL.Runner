codeunit 60941 "Delta Lib Tests"
{
    Subtype = Test;

    [Test]
    procedure AnswerIsFortyTwo()
    var
        Assert: Codeunit "Delta Assert";
        Bridge: Codeunit "Delta Bridge";
        Base: Record "Delta Base";
        Seed: Record "Delta Install Seed";
    begin
        Seed.Get('READY');
        Assert.AreEqual(7, Seed.Value, 'Install seed');
        Base.Code := 'CHAIN';
        Base."Bridge Value" := 11;
        Base.Insert();
        Base.Get('CHAIN');
        Assert.AreEqual(11, Base."Bridge Value", 'Chained tableextension');
        Assert.AreEqual(42, Bridge.Answer(), 'Delta Lib Answer');
    end;
}
