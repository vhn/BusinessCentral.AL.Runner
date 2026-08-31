codeunit 72482 "RPX Request Page XML Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        LibraryReportDataset: Codeunit "Library - Report Dataset";
        ParametersFileName: Text;

    [Test]
    [HandlerFunctions('RequestPageHandler')]
    procedure SaveAsXml_UsesRequestPageGlobalAndTemporaryDataset()
    var
        ParametersFile: File;
        ParametersInStream: InStream;
        ParametersXml: Text;
        Segment: Text;
        Value: Variant;
    begin
        Report.Run(Report::"RPX Request Page XML", true, false);
        ParametersFile.Open(ParametersFileName);
        ParametersFile.CreateInStream(ParametersInStream);
        while not ParametersInStream.EOS() do begin
            ParametersInStream.ReadText(Segment);
            ParametersXml += Segment;
        end;
        ParametersFile.Close();
        Assert.IsTrue(
            ParametersXml.Contains('ArrayOfReportParameter'),
            'Microsoft did not produce its report-parameter XML document.');

        LibraryReportDataset.LoadDataSetFile();
        LibraryReportDataset.Reset();

        while LibraryReportDataset.GetNextRow() do
            if LibraryReportDataset.CurrentRowHasElement('RenderedValue') then begin
                LibraryReportDataset.FindCurrentRowValue('RenderedValue', Value);
                if Format(Value) = 'selected-row' then
                    exit;
            end;

        Error('The Microsoft XML dataset did not contain the request-page value from the temporary report row.');
    end;

    [Test]
    [HandlerFunctions('CancelRequestPageHandler')]
    procedure CancelledRequestPage_DoesNotRunReportBody()
    begin
        Report.Run(Report::"RPX Request Page XML", true, false);
    end;

    [Test]
    [HandlerFunctions('PdfRequestPageHandler')]
    procedure UnsupportedRequestPageOutput_FailsLoudly()
    begin
        asserterror Report.Run(Report::"RPX Request Page XML", true, false);
        Assert.ExpectedError('report-request-page-output');
    end;

    [RequestPageHandler]
    procedure RequestPageHandler(var RequestPage: TestRequestPage "RPX Request Page XML")
    begin
        RequestPage.Prefix.SetValue('selected');
        ParametersFileName := LibraryReportDataset.GetParametersFileName();
        RequestPage.SaveAsXml(
            ParametersFileName,
            LibraryReportDataset.GetFileName());
    end;

    [RequestPageHandler]
    procedure CancelRequestPageHandler(var RequestPage: TestRequestPage "RPX Request Page XML")
    begin
        RequestPage.FailOnPre.SetValue(true);
        RequestPage.Cancel().Invoke();
    end;

    [RequestPageHandler]
    procedure PdfRequestPageHandler(var RequestPage: TestRequestPage "RPX Request Page XML")
    begin
        RequestPage.SaveAsPdf('report.pdf');
    end;
}
