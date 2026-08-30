using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace AlRunner.Tests;

public sealed class X509CertificateInteropTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "al-runner-x509-interop", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string CreatePfxBase64(string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=AL Runner certificate interop test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var pfx = certificate.Export(X509ContentType.Pkcs12, password);

        using var imported = new X509Certificate2(pfx, password, X509KeyStorageFlags.Exportable);
        Assert.True(imported.HasPrivateKey);

        return Convert.ToBase64String(pfx);
    }

    private void WriteBundle()
    {
        const string password = "vivelafrance";
        var pfxBase64 = CreatePfxBase64(password);

        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.json"), """
        {
          "id": "cf98e457-b14f-4c2c-9bde-4b3551283aad",
          "name": "Runner Mechanism - X509 Interop",
          "publisher": "AL Runner",
          "version": "1.0.0.0",
          "dependencies": [],
          "platform": "1.0.0.0",
          "application": "1.0.0.0",
          "idRanges": [ { "from": 62750, "to": 62759 } ],
          "runtime": "14.0"
        }
        """);

        var source = """
        using System.Security.Encryption;

        table 62751 "X509 Interop Setup"
        {
            fields
            {
                field(1; "Primary Key"; Code[10]) { }
                field(2; Password; Text[250]) { }
                field(3; Payload; Blob) { }
            }

            keys
            {
                key(PK; "Primary Key") { Clustered = true; }
            }
        }

        codeunit 62750 "X509 Interop Tests"
        {
            Subtype = Test;
            TestPermissions = Disabled;

            [Test]
            procedure GetAutoCalculatesBlobBeforeCertificateImport()
            var
                Certificate: Codeunit X509Certificate2;
                Password: SecretText;
                Setup: Record "X509 Interop Setup";
                TempBlob: Codeunit "Temp Blob";
                Input: InStream;
                Output: OutStream;
                PfxBase64: Text;
            begin
                Setup."Primary Key" := 'CERT';
                Setup.Password := '__PASSWORD__';
                TempBlob.CreateOutStream(Output, TextEncoding::UTF8);
                Output.WriteText('__PFX_BASE64__');
                TempBlob.CreateInStream(Input);
                Setup.Payload.CreateOutStream(Output);
                CopyStream(Output, Input);
                Setup.Insert();

                Clear(Setup);
                Setup.SetAutoCalcFields(Payload);
                Setup.Get('CERT');
                Password := Setup.Password;
                Setup.Payload.CreateInStream(Input, TextEncoding::UTF8);
                Input.ReadText(PfxBase64);
                if Password.Unwrap() <> '__PASSWORD__' then
                    Error('The combined path lost the certificate password.');
                if PfxBase64 <> '__PFX_BASE64__' then
                    Error(
                        'The combined path changed the Base64 certificate text: expected length %1, actual length %2, actual prefix %3.',
                        StrLen('__PFX_BASE64__'), StrLen(PfxBase64), CopyStr(PfxBase64, 1, 12));
                if not Certificate.VerifyCertificate(PfxBase64, Password, "X509 Content Type"::Cert) then
                    Error('System Application did not verify the certificate.');
            end;
        }
        """
            .Replace("__PASSWORD__", password, StringComparison.Ordinal)
            .Replace("__PFX_BASE64__", pfxBase64, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(_root, "X509InteropTests.Codeunit.al"), source);
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
            Environment =
            {
                ["AL_RUNNER_NO_DEP_COMPANY_CACHE"] = "1",
            },
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
            throw new TimeoutException("runner hung while checking X509 certificate interop");
        }
        process.WaitForExit();
        lock (output) return (output.ToString(), process.ExitCode);
    }

    [SkippableFact]
    public void SystemApplication_ImportsPasswordProtectedPfx()
    {
        TestArtifacts.SkipIfMissing();
        WriteBundle();

        var (output, exitCode) = RunBundle();

        Assert.True(exitCode == 0, $"Expected the System Application X509 wrapper to import a PFX; exit={exitCode}\n{output}");
        Assert.Contains("1P/0F/0E across 1 tests", output);
    }
}
