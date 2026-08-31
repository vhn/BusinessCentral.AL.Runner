using System.Reflection;
using AlRunner.Patches;
using Microsoft.Dynamics.Nav.Runtime;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class RecordPatchesEnumFieldCaptionTests : IDisposable
{
    private const int TableId = 94680;
    private const int EnumId = 94681;
    private readonly BcEngineFixture _engine;
    private readonly string _root;

    public RecordPatchesEnumFieldCaptionTests(BcEngineFixture engine)
    {
        _engine = engine;
        _root = Path.Combine(Path.GetTempPath(), "al-runner-enum-field-caption-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        AlEnumMetadataRegistry.Remove(EnumId);
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [SkippableFact]
    public void BuildNCLMetaTable_PreservesEnumValueCaptionsOnFields()
    {
        TestArtifacts.SkipIf(!_engine.Ready,
            _engine.SkipReason ?? "the in-process BC engine is not ready (see BcEngineCollection).");

        AlEnumMetadataRegistry.Register(
            EnumId,
            "Enum Field Caption Probe",
            ["DISABLED"],
            [0],
            captions: ["Disabled"]);

        File.WriteAllText(Path.Combine(_root, "EnumFieldCaptionProbe.al"), $$"""
            table {{TableId}} "Enum Field Caption Probe"
            {
                fields
                {
                    field(1; State; Enum "Enum Field Caption Probe") { }
                }
            }
            """);
        RecordPatches.AddSourceDir(_root);

        var table = RecordPatches.NCLMetadata_GetMetaTableById(
            BcRuntime.SkeletonNCLMetadata!, TableId, false, 0);
        Assert.True(table.TryGetFieldByNo(1, out var field));

        var metadataField = typeof(NCLMetaField).GetField(
            "fieldOptionMetadata", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NCLMetaField.fieldOptionMetadata not found — Ncl shape changed.");
        var metadata = Assert.IsAssignableFrom<NCLOptionMetadata>(metadataField.GetValue(field));

        Assert.Equal("Disabled", metadata.GetCaptionFromIndex(0));
    }
}
