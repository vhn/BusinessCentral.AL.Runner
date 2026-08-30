namespace AlRunner.Infrastructure;

internal static class SiblingSymbolsDirectory
{
    private static readonly string ProcessNonce = Guid.NewGuid().ToString("N");

    internal static string ForBundle(string bundlePath)
        => ForBundle(bundlePath, ProcessNonce);

    internal static string ForBundle(string bundlePath, string processNonce)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bundlePath));
        if (OperatingSystem.IsWindows())
            normalizedPath = normalizedPath.ToUpperInvariant();
        var bundleName = Path.GetFileName(normalizedPath);
        var pathHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalizedPath)))[..12].ToLowerInvariant();
        return Path.Combine(
            Path.GetTempPath(),
            "al-runner-sibling-symbols",
            $"{bundleName}-{pathHash}-{processNonce}");
    }
}
