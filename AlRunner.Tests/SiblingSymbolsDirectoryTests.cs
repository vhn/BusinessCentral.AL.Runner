using AlRunner.Infrastructure;
using Xunit;

namespace AlRunner.Tests;

public sealed class SiblingSymbolsDirectoryTests
{
    [Fact]
    public void ForBundle_IsStableWithinOneRunnerProcess()
    {
        var bundle = Path.Combine(Path.GetTempPath(), "fixture", "bundle");

        Assert.Equal(
            SiblingSymbolsDirectory.ForBundle(bundle, "process-a"),
            SiblingSymbolsDirectory.ForBundle(bundle, "process-a"));
    }

    [Fact]
    public void ForBundle_IsolatedAcrossConcurrentRunnerProcesses()
    {
        var bundle = Path.Combine(Path.GetTempPath(), "fixture", "bundle");

        Assert.NotEqual(
            SiblingSymbolsDirectory.ForBundle(bundle, "process-a"),
            SiblingSymbolsDirectory.ForBundle(bundle, "process-b"));
    }

    [Fact]
    public void ForBundle_IsolatedAcrossDifferentPathsWithTheSameBasename()
    {
        var first = Path.Combine(Path.GetTempPath(), "first", "Test");
        var second = Path.Combine(Path.GetTempPath(), "second", "Test");

        Assert.NotEqual(
            SiblingSymbolsDirectory.ForBundle(first, "process-a"),
            SiblingSymbolsDirectory.ForBundle(second, "process-a"));
    }
}
