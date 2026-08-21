// RunnerVersion — the single answer to "which build of the runner is this?".
using System.Reflection;

namespace AlRunner.Infrastructure;

internal static class RunnerVersion
{
    /// <summary>The version string <c>--version</c> prints for <paramref name="asm"/>.</summary>
    internal static string Describe(Assembly asm) =>
        Describe(asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                 asm.GetName().Version?.ToString());

    /// <summary>
    /// Pure form, so both branches are testable without constructing an assembly.
    ///
    /// <para><c>AssemblyVersion</c> is a numeric quad and cannot carry a prerelease suffix —
    /// .NET strips it. So reading it prints <c>2.0.0.0</c> for <c>2.0.0-preview.1</c> and for
    /// a fork build stamped <c>2.1.2-performance</c> alike, i.e. it drops exactly the part that
    /// identifies WHICH build someone is holding. <c>AssemblyInformationalVersion</c> keeps the
    /// suffix, so it leads; the numeric version is the fallback for an assembly that carries no
    /// informational attribute at all.</para>
    /// </summary>
    internal static string Describe(string? informationalVersion, string? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion)) return informationalVersion;
        return string.IsNullOrWhiteSpace(assemblyVersion) ? "unknown" : assemblyVersion;
    }
}
