using System.Diagnostics;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public sealed class NewCompanyPricingFeatureTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "al-runner-new-company-pricing", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void WriteBundle()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.json"), """
        {
          "id": "1b886b6f-060d-497f-a69d-f7e039ffce80",
          "name": "Runner Mechanism - New Company Pricing",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 62740, "to": 62749 } ],
          "runtime": "14.0"
        }
        """);

        File.WriteAllText(Path.Combine(_root, "NewCompanyPricingTests.Codeunit.al"), """
        using Microsoft.Pricing.Calculation;
        using System.Reflection;

        codeunit 62740 "New Company Pricing Tests"
        {
            Subtype = Test;
            TestPermissions = Disabled;

            [Test]
            procedure FreshCompanyUsesExtendedPriceCalculation()
            var
                PriceCalculationMgt: Codeunit "Price Calculation Mgt.";
            begin
                if not PriceCalculationMgt.IsExtendedPriceCalculationEnabled() then
                    Error('A fresh company must use the new sales pricing experience.');
            end;

            [Test]
            procedure FreshCompanyExposesPrecompiledCodeunitMetadata()
            var
                CodeunitMetadata: Record "CodeUnit Metadata";
            begin
                if not CodeunitMetadata.Get(6303) then
                    Error('Precompiled Base Application codeunit 6303 is absent from CodeUnit Metadata.');
                if CodeunitMetadata.Name = '' then
                    Error('CodeUnit Metadata returned codeunit 6303 without its name.');
            end;

            [Test]
            procedure FindsActiveAllCustomerDiscount()
            var
                Item: Record Item;
                SalesHeader: Record "Sales Header";
                SalesLine: Record "Sales Line";
                PriceListLine: Record "Price List Line";
                TempFoundPriceListLine: Record "Price List Line" temporary;
                TempPriceSource: Record "Price Source" temporary;
                PriceCalculationSetup: Record "Price Calculation Setup";
                PriceCalculationMgt: Codeunit "Price Calculation Mgt.";
                PriceCalculationBufferMgt: Codeunit "Price Calculation Buffer Mgt.";
                LineWithPrice: Interface "Line With Price";
                PriceCalculation: Interface "Price Calculation";
            begin
                Item."No." := 'ITEM';
                Item.Insert();

                PriceListLine."Price Type" := PriceListLine."Price Type"::Sale;
                PriceListLine."Amount Type" := PriceListLine."Amount Type"::Discount;
                PriceListLine."Source Type" := PriceListLine."Source Type"::"All Customers";
                PriceListLine.Validate("Asset Type", PriceListLine."Asset Type"::Item);
                PriceListLine.Validate("Asset No.", Item."No.");
                PriceListLine."Line Discount %" := 10;
                PriceListLine."Starting Date" := Today() - 7;
                PriceListLine.Status := PriceListLine.Status::Active;
                PriceListLine.Insert();

                SalesHeader."Posting Date" := Today();
                SalesLine.Type := SalesLine.Type::Item;
                SalesLine."No." := Item."No.";
                SalesLine.Quantity := 1;
                SalesLine."Allow Line Disc." := true;
                SalesLine.GetLineWithPrice(LineWithPrice);
                LineWithPrice.SetLine(PriceListLine."Price Type"::Sale, SalesHeader, SalesLine);

                PriceListLine.Reset();
                PriceListLine.SetRange(Status, PriceListLine.Status::Active);
                PriceListLine.SetRange("Price Type", PriceListLine."Price Type"::Sale);
                PriceListLine.SetRange("Amount Type", PriceListLine."Amount Type"::Discount);
                PriceListLine.SetRange("Source Type", PriceListLine."Source Type"::"All Customers");
                PriceListLine.SetRange("Asset Type", PriceListLine."Asset Type"::Item);
                PriceListLine.SetRange("Asset No.", Item."No.");
                if PriceListLine.IsEmpty() then
                    Error('The inserted discount does not survive direct table filters.');

                if not LineWithPrice.CopyToBuffer(PriceCalculationBufferMgt) then
                    Error('Sales Line - Price did not copy to the calculation buffer.');
                if not PriceCalculationBufferMgt.GetSources(TempPriceSource) then
                    Error('The price calculation buffer has no sources.');
                TempPriceSource.SetRange("Source Type", TempPriceSource."Source Type"::"All Customers");
                if TempPriceSource.IsEmpty() then
                    Error('The price calculation buffer lost the all-customers source.');

                if not PriceCalculationMgt.FindSetup(LineWithPrice, PriceCalculationSetup) then
                    Error('No price calculation setup was selected for the sales line.');
                if PriceCalculationSetup.Implementation <> PriceCalculationSetup.Implementation::"Business Central (Version 16.0)" then
                    Error('The V16 price calculation setup was not selected.');

                PriceCalculationMgt.GetHandler(LineWithPrice, PriceCalculation);
                if not PriceCalculation.FindDiscount(TempFoundPriceListLine, true) then
                    Error('The active all-customer discount was not found even with ShowAll.');
                if not PriceCalculation.FindDiscount(TempFoundPriceListLine, false) then
                    Error('The active all-customer discount was not found.');
            end;
        }
        """);
    }

    private (string Output, int ExitCode) RunBundle()
    {
        var args = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
        args.Append(TestBuildConfig.BcVersionArg);
        args.Append(" --package-cache \"").Append(TestArtifacts.PlatformAppsDir()).Append('"');
        args.Append(" \"").Append(_root).Append('"');

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot,
            Environment = { ["AL_RUNNER_NO_DEP_COMPANY_CACHE"] = "1" },
        };
        var output = new StringBuilder();
        using var process = Process.Start(startInfo)!;
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(300_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("runner hung while checking new-company pricing defaults");
        }
        process.WaitForExit();
        lock (output) return (output.ToString(), process.ExitCode);
    }

    [SkippableFact]
    public void FreshCompany_EnablesExtendedPriceCalculation()
    {
        TestArtifacts.SkipIfMissing();
        WriteBundle();

        var (output, exitCode) = RunBundle();

        Assert.True(exitCode == 0, $"Expected a fresh company to enable extended pricing; exit={exitCode}\n{output}");
        Assert.Contains("3P/0F/0E across 3 tests", output);
    }
}
