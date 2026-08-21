using System.Reflection;
using AlRunner.Infrastructure;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// `--version` is the only way to tell WHICH build of the runner you are holding, and the
/// part that distinguishes builds is the prerelease suffix: `2.0.0-preview.1`,
/// `2.1.2-performance`. .NET strips that suffix from <c>AssemblyVersion</c> (a purely
/// numeric quad), so reading <c>Assembly.GetName().Version</c> — as Program.cs did — prints
/// `2.0.0.0` and drops exactly the identifying part. A fork build handed to someone for
/// testing then reports the same string as any other build of the same numeric version.
///
/// <c>AssemblyInformationalVersion</c> is where the suffix survives, so that is the primary
/// source, with the numeric version as the fallback for an assembly that carries no
/// informational attribute at all.
/// </summary>
public class RunnerVersionTests
{
    /// <summary>The suffix is the whole point: it must survive.</summary>
    [Fact]
    public void Describe_PrefersTheInformationalVersion_SoThePrereleaseSuffixSurvives()
    {
        Assert.Equal("2.1.2-performance",
            RunnerVersion.Describe(informationalVersion: "2.1.2-performance", assemblyVersion: "2.1.2.0"));
    }

    /// <summary>
    /// NEGATIVE — no informational version to read (an assembly built without the
    /// attribute). The numeric version is still better than nothing, and must be used
    /// rather than printing an empty string.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Describe_FallsBackToTheAssemblyVersion_WhenThereIsNoInformationalVersion(string? informational)
    {
        Assert.Equal("2.1.2.0",
            RunnerVersion.Describe(informationalVersion: informational, assemblyVersion: "2.1.2.0"));
    }

    /// <summary>NEGATIVE — neither source available must say so, not print an empty version.</summary>
    [Fact]
    public void Describe_IsUnknown_WhenNeitherVersionIsAvailable()
    {
        Assert.Equal("unknown", RunnerVersion.Describe(informationalVersion: null, assemblyVersion: null));
    }

    /// <summary>
    /// Against the real runner assembly, not a constructed pair: the value printed must be
    /// the informational version this build actually carries, and — while the two differ —
    /// must NOT be the numeric quad that loses the suffix.
    /// </summary>
    [Fact]
    public void Describe_OnTheRunnerAssembly_ReportsItsInformationalVersion()
    {
        var runner = typeof(AlRunner.AppLoader).Assembly;
        var informational = runner.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var numeric = runner.GetName().Version?.ToString();

        Assert.False(string.IsNullOrWhiteSpace(informational),
            "the runner assembly must carry an AssemblyInformationalVersion — <Version> in AlRunner.csproj sets it.");

        var described = RunnerVersion.Describe(runner);

        Assert.Equal(informational, described);
        if (informational != numeric)
            Assert.NotEqual(numeric, described);
    }
}
