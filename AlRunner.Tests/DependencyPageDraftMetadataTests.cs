using System.IO.Compression;
using System.Text;
using System.Xml;
using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

[Collection(CacheRootsSerialCollection.Name)]
public class DependencyPageDraftMetadataTests
{
    private const int MultipleLinesPageId = 88123431;
    private const int DefaultPageId = 88123432;

    private const string SymbolReference = """
        {
          "RuntimeVersion": "15.1",
          "Pages": [
            {
              "Id": 88123431,
              "Name": "Draft Metadata Lines",
              "Properties": [
                { "Name": "PageType", "Value": "ListPart" },
                { "Name": "SourceTable", "Value": "5741" },
                { "Name": "AutoSplitKey", "Value": "true" },
                { "Name": "MultipleNewLines", "Value": "true" }
              ]
            },
            {
              "Id": 88123432,
              "Name": "Default Draft Metadata Lines",
              "Properties": [
                { "Name": "PageType", "Value": "ListPart" },
                { "Name": "SourceTable", "Value": "5741" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void ExplicitDraftProperties_SurviveSymbolParsingAndMetadataXmlReconstruction()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var appPath = WriteApp(dir);
            var page = Assert.Single(BcAppSymbolCache.Get(appPath).Pages, page => page.Id == MultipleLinesPageId);

            Assert.True(page.AutoSplitKey);
            Assert.True(page.MultipleNewLines);

            RecordPatches.AddBcAppPath(appPath);
            var sourceObject = SourceObject(RecordPatches.TryBuildDependencyPageMetadata(MultipleLinesPageId));
            Assert.Equal("1", sourceObject.GetAttribute("AutoSplitKey"));
            Assert.Equal("1", sourceObject.GetAttribute("MultipleNewLines"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void OmittedDraftProperties_KeepAlDefaultsFalseAndStayOutOfMetadataXml()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var appPath = WriteApp(dir);
            var page = Assert.Single(BcAppSymbolCache.Get(appPath).Pages, page => page.Id == DefaultPageId);

            Assert.False(page.AutoSplitKey);
            Assert.False(page.MultipleNewLines);

            RecordPatches.AddBcAppPath(appPath);
            var sourceObject = SourceObject(RecordPatches.TryBuildDependencyPageMetadata(DefaultPageId));
            Assert.False(sourceObject.HasAttribute("AutoSplitKey"));
            Assert.False(sourceObject.HasAttribute("MultipleNewLines"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string WriteApp(string dir)
    {
        var appPath = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".app");
        using var zip = new FileStream(appPath, FileMode.Create);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("SymbolReference.json");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(SymbolReference);
        return appPath;
    }

    private static XmlElement SourceObject(string? xml)
    {
        Assert.NotNull(xml);
        var document = new XmlDocument();
        document.LoadXml(xml!);
        var namespaces = new XmlNamespaceManager(document.NameTable);
        namespaces.AddNamespace("m", "urn:schemas-microsoft-com:dynamics:NAV:MetaObjects");
        return (XmlElement)document.DocumentElement!
            .SelectSingleNode("m:Properties/m:SourceObject", namespaces)!;
    }
}
