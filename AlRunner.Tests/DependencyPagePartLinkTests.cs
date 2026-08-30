using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

public sealed class DependencyPagePartLinkTests
{
    [Fact]
    public void ParseSubPageLinkText_ResolvesFieldAndConstantClauses()
    {
        var partFields = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Document No."] = 1,
            ["Derived From Line No."] = 6,
        };
        var parentFields = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["No."] = 1,
        };

        var links = RecordPatches.ParseSubPageLinkText(
            "\"Document No.\" = field(\"No.\"), \"Derived From Line No.\" = const(0)",
            partFields,
            parentFields);

        Assert.Collection(
            links,
            link =>
            {
                Assert.Equal(TestPagePartLinkKind.Field, link.Kind);
                Assert.Equal(1, link.PartFieldNo);
                Assert.Equal(1, link.ParentFieldNo);
                Assert.Null(link.Value);
            },
            link =>
            {
                Assert.Equal(TestPagePartLinkKind.Const, link.Kind);
                Assert.Equal(6, link.PartFieldNo);
                Assert.Equal(0, link.ParentFieldNo);
                Assert.Equal("0", link.Value);
            });
    }
}
