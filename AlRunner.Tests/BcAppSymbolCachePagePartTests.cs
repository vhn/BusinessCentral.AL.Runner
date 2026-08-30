using System.IO.Compression;
using System.Text;
using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

[Collection(CacheRootsSerialCollection.Name)]
public sealed class BcAppSymbolCachePagePartTests
{
    [Fact]
    public void Pages_CaptureDependencyPartTargetAndSubPageLink()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var appPath = WriteApp(dir, """
                {
                  "RuntimeVersion": "15.1",
                  "Pages": [
                    {
                      "Id": 5740,
                      "Name": "Transfer Order",
                      "Properties": [
                        { "Name": "SourceTable", "Value": "5740" }
                      ],
                      "Controls": [
                        {
                          "Kind": 1,
                          "Controls": [
                            {
                              "Kind": 6,
                              "RelatedPagePartId": { "Name": "", "Id": 5741 },
                              "Properties": [
                                {
                                  "Name": "SubPageLink",
                                  "Value": "\"Document No.\" = field(\"No.\"), \"Derived From Line No.\" = const(0)"
                                }
                              ],
                              "Id": 757941893,
                              "Name": "TransferLines"
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
                """);

            var page = Assert.Single(BcAppSymbolCache.Get(appPath).Pages);
            var part = Assert.Single(page.Parts!);

            Assert.Equal(757941893, part.Id);
            Assert.Equal("TransferLines", part.Name);
            Assert.Equal(5741, part.PagePartId);
            Assert.Equal(
                "\"Document No.\" = field(\"No.\"), \"Derived From Line No.\" = const(0)",
                part.SubPageLink);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string WriteApp(string dir, string symbolReferenceJson)
    {
        var appPath = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".app");
        using var zip = new FileStream(appPath, FileMode.Create);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("SymbolReference.json");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(symbolReferenceJson);
        return appPath;
    }
}
