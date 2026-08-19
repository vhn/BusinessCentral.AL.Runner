using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// Every id-keyed registry the AL emitter fills is handled the same way by a watch reload:
/// preserved together, or cleared together.
///
/// <para>Page and XmlPort used to be in NEITHER branch of
/// <c>BcRuntime.ResetForNewBundleReload</c> — not preserved, not cleared, and nothing in
/// <c>AlRunner/</c> ever called their <c>Clear()</c>. That reads as harmless only while a live
/// RAD workspace is doing the bookkeeping, because <c>RadMetadataCapture.Drop</c> removes both
/// by id for an object a delta deleted. The path that has no workspace to ask is exactly the
/// one this covers: <c>RadWorkspace.Invalidate</c> clears the object map BEFORE the full
/// compile runs, so the deletion sweep in <c>RadEmitResult.Commit</c> walks nothing, and a
/// deleted page's or xmlport's metadata XML survived the rebuild that was supposed to be the
/// clean slate. <c>RunnerFormInit</c> and <c>RunnerXmlMetadataLoader</c> then answer for an
/// object the source no longer declares.</para>
///
/// <para>Both directions are asserted for every registry, because "cleared" alone would also
/// be satisfied by a reload that threw the warm metadata away on every cycle — which is the
/// thing the delta path exists to avoid.</para>
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class WatchReloadRegistryClearTests(BcEngineFixture engine)
{
    // Outside every fixture's range (RadFixture owns 71000–71199) so a registration made here
    // cannot be mistaken for, or clobber, another suite's.
    private const int PageId = 79101;
    private const int XmlPortId = 79102;
    private const int ReportId = 79103;
    private const int EnumId = 79104;

    [SkippableFact]
    public void ReloadWithoutPreservedCaptures_ClearsEveryEmitCapturedRegistry()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        Seed();
        BcRuntime.ResetForNewBundleReload(preserveEmitCaptures: false);

        Assert.False(AlPageMetadataRegistry.TryGet(PageId, out _));
        Assert.False(AlXmlPortMetadataRegistry.TryGet(XmlPortId, out _));
        Assert.False(AlReportMetadataRegistry.TryGet(ReportId, out _));
        Assert.False(AlEnumMetadataRegistry.TryGet(EnumId, out _));
        Assert.Empty(AlReportLayoutRegistry.Get(ReportId));
    }

    [SkippableFact]
    public void ReloadWithPreservedCaptures_KeepsEveryEmitCapturedRegistry()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        Seed();
        BcRuntime.ResetForNewBundleReload(preserveEmitCaptures: true);

        Assert.True(AlPageMetadataRegistry.TryGet(PageId, out var page));
        Assert.Equal("<Page ID=\"79101\" />", page);
        Assert.True(AlXmlPortMetadataRegistry.TryGet(XmlPortId, out var xmlPort));
        Assert.Equal("<XmlPort ID=\"79102\" />", xmlPort);
        Assert.True(AlReportMetadataRegistry.TryGet(ReportId, out var report));
        Assert.Equal("<Report ID=\"79103\" />", report);
        Assert.True(AlEnumMetadataRegistry.TryGet(EnumId, out var declaredEnum));
        Assert.Equal(new[] { "Warm" }, declaredEnum.Options);
        Assert.Equal("Warm Layout", Assert.Single(AlReportLayoutRegistry.Get(ReportId)).Name);

        // Leave nothing behind for the next test in this collection: these registries are
        // process-wide, and the delta suites diff snapshots of them.
        BcRuntime.ResetForNewBundleReload(preserveEmitCaptures: false);
    }

    private static void Seed()
    {
        AlPageMetadataRegistry.Register(PageId, "<Page ID=\"79101\" />");
        AlXmlPortMetadataRegistry.Register(XmlPortId, "<XmlPort ID=\"79102\" />");
        AlReportMetadataRegistry.Register(ReportId, "<Report ID=\"79103\" />");
        AlEnumMetadataRegistry.Register(EnumId, "Warm Enum", ["Warm"], [0]);
        AlReportLayoutRegistry.Register(new AlReportLayoutInfo(
            ReportId, "Warm Layout", "RDLC", string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty));

        // The seed itself has to be visible, or "cleared" below would pass against a registry
        // that never held anything.
        Assert.True(AlPageMetadataRegistry.TryGet(PageId, out _));
        Assert.True(AlXmlPortMetadataRegistry.TryGet(XmlPortId, out _));
        Assert.True(AlReportMetadataRegistry.TryGet(ReportId, out _));
        Assert.True(AlEnumMetadataRegistry.TryGet(EnumId, out _));
        Assert.Single(AlReportLayoutRegistry.Get(ReportId));
    }
}
