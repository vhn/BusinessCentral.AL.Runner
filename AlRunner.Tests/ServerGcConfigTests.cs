// ServerGcConfigTests — the shipped runner must run under Server GC.
//
// A cold AL compile is GC-throughput-bound: the Application module's emit produces
// 165 MB of C# that becomes ~330 MB of UTF-16, and every copy stays reachable until
// Roslyn finishes. One Workstation GC heap cannot keep up. Measured on the NP Retail
// corpus (7,053 AL files / 6,949 objects), same binary, DOTNET_gcServer the only
// difference: 1283.1s wall against 611.5s, and the BC emit phase alone 837.7s against
// 331.3s. See AlRunner.csproj's ServerGarbageCollection note for the full table.
//
// Why a CONFIG test and not only the behavioural one
// --------------------------------------------------
// PhaseLogIntegrationTests.TheRunnerProcess_RunsUnderServerGc spawns a real runner and
// reads GCSettings.IsServerGC back out of its phase log — that is the stronger claim,
// because it proves the setting reached the process rather than merely being written
// somewhere. But it is also satisfiable for the WRONG reason: a developer or CI runner
// with DOTNET_gcServer=1 exported in its environment makes it pass with the csproj
// property deleted. This suite closes that hole by asserting the shipped
// runtimeconfig.json — the artifact `dotnet al-runner.dll` and the packaged tool both
// read, and the only thing a user who sets no environment variables gets.
//
// The two together are the two directions of one claim: the config says Server GC, AND a
// process launched from it observes Server GC.
//
// There is deliberately NO test asserting background/concurrent GC stays on. Its value is
// the .NET default and nothing here sets it, so such a test would assert nothing on every
// run that matters — vacuous-pass noise by tdd.md's own standard. The reasoning for leaving
// that knob alone lives in AlRunner.csproj beside the property it is about.
using System.Text.Json;
using Xunit;

namespace AlRunner.Tests;

public sealed class ServerGcConfigTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    /// <summary>
    /// The runtimeconfig.json beside the al-runner.dll the tests actually spawn — the same
    /// path <see cref="TestBuildConfig"/> resolves, so this can never assert about a
    /// different build than the rest of the suite exercises.
    /// </summary>
    private static string RuntimeConfigPath => Path.Combine(
        RepoRoot, "AlRunner", "bin", TestBuildConfig.Configuration,
        TestBuildConfig.Framework, "al-runner.runtimeconfig.json");

    [Fact]
    public void ShippedRuntimeConfig_EnablesServerGc()
    {
        Assert.True(File.Exists(RuntimeConfigPath),
            $"no runtimeconfig at '{RuntimeConfigPath}' — build AlRunner before running this suite");

        using var doc = JsonDocument.Parse(File.ReadAllText(RuntimeConfigPath));
        var props = doc.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        // Absent is a failure in its own right, and a distinct one: it means the csproj
        // property was removed and every user silently fell back to Workstation GC, which
        // is exactly the state this repo sat in while every benchmark harness exported
        // DOTNET_gcServer=1 by hand and no shipped run ever got it.
        Assert.True(props.TryGetProperty("System.GC.Server", out var serverGc),
            "System.GC.Server is absent from the shipped runtimeconfig — restore "
            + "<ServerGarbageCollection>true</ServerGarbageCollection> in AlRunner.csproj");
        Assert.Equal(JsonValueKind.True, serverGc.ValueKind);
    }
}
