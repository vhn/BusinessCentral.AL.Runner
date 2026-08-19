// CacheRootsTests — unit-level coverage of AlRunner.Infrastructure.CacheRoots.Resolve
// (issue #1821). This is the cheap "is the path computed correctly" half; the
// decisive "does it actually WRITE there and not to the shared real location" half is
// CacheRootsIsolationTests.cs's end-to-end subprocess proof — see that file's header
// for why a path-only test alone would not be sufficient evidence for this bug.
//
// All test methods stay in ONE class so they run sequentially (xunit's default within
// a class/collection) despite this project's parallelizeTestCollections=true. CacheRoots
// is process-global mutable state, and several BcAppSymbolCache test classes touch it
// too — indirectly, via BcAppSymbolCache.Get()/GetTableExtensions() in-process calling
// CacheRoots.Resolve("bc-symbols") — so this class joins CacheRootsSerialCollection
// alongside them (see that file's header) rather than relying on being the only one.
// Each test still resets the override in a try/finally, belt-and-braces.

using AlRunner.Infrastructure;
using Xunit;

namespace AlRunner.Tests;

[Collection(CacheRootsSerialCollection.Name)]
public sealed class CacheRootsTests
{
    [Fact]
    public void Resolve_NoOverride_DefaultsToRealUserProfileCacheDir()
    {
        CacheRoots.ResetForTests();
        try
        {
            var expectedRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "al-runner");

            Assert.Equal(Path.Combine(expectedRoot, "compiled-deps"), CacheRoots.Resolve("compiled-deps"));
            Assert.Equal(Path.Combine(expectedRoot, "workspace-deps"), CacheRoots.Resolve("workspace-deps"));
            Assert.Equal(Path.Combine(expectedRoot, "ncl-cecil"), CacheRoots.Resolve("ncl-cecil"));
            Assert.Equal(Path.Combine(expectedRoot, "bc-symbols"), CacheRoots.Resolve("bc-symbols"));
        }
        finally { CacheRoots.ResetForTests(); }
    }

    [Fact]
    public void Resolve_WithOverride_NestsEachCacheUnderTheGivenDir()
    {
        var overrideDir = Path.Combine(Path.GetTempPath(), "al-runner-cacheroots-unit", Guid.NewGuid().ToString("N"));
        CacheRoots.SetOverride(overrideDir);
        try
        {
            Assert.Equal(Path.Combine(overrideDir, "compiled-deps"), CacheRoots.Resolve("compiled-deps"));
            Assert.Equal(Path.Combine(overrideDir, "workspace-deps"), CacheRoots.Resolve("workspace-deps"));
            Assert.Equal(Path.Combine(overrideDir, "ncl-cecil"), CacheRoots.Resolve("ncl-cecil"));
            Assert.Equal(Path.Combine(overrideDir, "bc-symbols"), CacheRoots.Resolve("bc-symbols"));
        }
        finally { CacheRoots.ResetForTests(); }
    }

    [Fact]
    public void Resolve_TwoDifferentOverrides_NeverCollide()
    {
        var dirA = Path.Combine(Path.GetTempPath(), "al-runner-cacheroots-unit", Guid.NewGuid().ToString("N"));
        var dirB = Path.Combine(Path.GetTempPath(), "al-runner-cacheroots-unit", Guid.NewGuid().ToString("N"));
        try
        {
            CacheRoots.SetOverride(dirA);
            var resolvedA = CacheRoots.Resolve("compiled-deps");

            CacheRoots.SetOverride(dirB);
            var resolvedB = CacheRoots.Resolve("compiled-deps");

            Assert.NotEqual(resolvedA, resolvedB);
            Assert.StartsWith(dirA, resolvedA);
            Assert.StartsWith(dirB, resolvedB);
        }
        finally { CacheRoots.ResetForTests(); }
    }

    [Fact]
    public void Resolve_SetOverrideNull_RevertsToDefault()
    {
        var overrideDir = Path.Combine(Path.GetTempPath(), "al-runner-cacheroots-unit", Guid.NewGuid().ToString("N"));
        try
        {
            CacheRoots.SetOverride(overrideDir);
            Assert.StartsWith(overrideDir, CacheRoots.Resolve("bc-symbols"));

            // Mirrors Program.cs: no --cache flag at all means cacheRootOverride is never
            // set, so a null SetOverride call must behave exactly like ResetForTests —
            // reverting to the real ~/.cache/al-runner default.
            CacheRoots.SetOverride(null);
            var expectedDefault = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "al-runner", "bc-symbols");
            Assert.Equal(expectedDefault, CacheRoots.Resolve("bc-symbols"));
        }
        finally { CacheRoots.ResetForTests(); }
    }

    // ── --no-cache ──────────────────────────────────────────────────────────────────────
    //
    // The behavioural half of this claim — that a --no-cache run really does start cold
    // rather than merely computing a different path string — is CacheRootsNoCacheIsolation-
    // Tests.cs. These pin the path arithmetic that half depends on.

    [Fact]
    public void DisableForRun_MovesEveryCacheOffTheRealRoot_UnderOneThrowawayDir()
    {
        try
        {
            Environment.SetEnvironmentVariable("AL_RUNNER_NOCACHE_ROOT", null);
            var realRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "al-runner");

            var throwaway = CacheRoots.DisableForRun();

            // Every named cache moves, not just the ones #1821 originally listed: the point of
            // --no-cache is that NOTHING is served from a previous run, and one cache left
            // pointing at the shared root is one cache still handing this run a warm answer.
            foreach (var name in new[]
                     {
                         "compiled-deps", "workspace-deps", "ncl-cecil", "bc-symbols",
                         "app-manifests", "r2r-chunks", "install-baseline",
                     })
            {
                Assert.Equal(Path.Combine(throwaway, name), CacheRoots.Resolve(name));
                Assert.DoesNotContain(realRoot, CacheRoots.Resolve(name), StringComparison.Ordinal);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("AL_RUNNER_NOCACHE_ROOT", null);
            CacheRoots.ResetForTests();
        }
    }

    [Fact]
    public void DisableForRun_GivesAFreshRootEachTime()
    {
        try
        {
            Environment.SetEnvironmentVariable("AL_RUNNER_NOCACHE_ROOT", null);
            var first = CacheRoots.DisableForRun();
            Environment.SetEnvironmentVariable("AL_RUNNER_NOCACHE_ROOT", null);
            var second = CacheRoots.DisableForRun();

            // A fixed "no-cache" directory would be a cache: the second --no-cache run on the
            // machine would be served everything the first one computed, which is the exact
            // defect this flag is supposed to not have.
            Assert.NotEqual(first, second);
            Assert.StartsWith(second, CacheRoots.Resolve("compiled-deps"), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AL_RUNNER_NOCACHE_ROOT", null);
            CacheRoots.ResetForTests();
        }
    }

    [Fact]
    public void DisableForRun_InAReexecedChild_AdoptsTheParentsRoot()
    {
        try
        {
            Environment.SetEnvironmentVariable("AL_RUNNER_NOCACHE_ROOT", null);
            var parentRoot = CacheRoots.DisableForRun();

            // Stand in for the re-exec'd child: a fresh process starts with no in-memory
            // override but DOES inherit the parent's environment, which is how the root
            // crosses. It must land on the SAME root — the child exists so the rewritten Ncl
            // loads from a cache HIT, and a fresh root would MISS, rewrite a second time, and
            // then take the very rewrite-then-load-in-process path (BadImageFormatException
            // 0x80131124) that the re-exec was added to avoid.
            var inheritedEnv = Environment.GetEnvironmentVariable("AL_RUNNER_NOCACHE_ROOT");
            Assert.Equal(parentRoot, inheritedEnv);
            CacheRoots.ResetForTests();
            Environment.SetEnvironmentVariable("AL_RUNNER_NOCACHE_ROOT", inheritedEnv);
            var childRoot = CacheRoots.DisableForRun();

            Assert.Equal(parentRoot, childRoot);
            Assert.Equal(Path.Combine(parentRoot, "ncl-cecil"), CacheRoots.Resolve("ncl-cecil"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AL_RUNNER_NOCACHE_ROOT", null);
            CacheRoots.ResetForTests();
        }
    }

    [Fact]
    public void DisableForRun_DoesNotTouchTheRealCache()
    {
        try
        {
            var realRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "al-runner");
            var before = Directory.Exists(realRoot)
                ? Directory.GetFileSystemEntries(realRoot).OrderBy(x => x, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();

            CacheRoots.DisableForRun();

            // Redirect, never delete. Erasing the shared cache would be a destructive side
            // effect of a flag that reads as "don't use the cache", and it would break any
            // other al-runner running at the same time — CI runs four.
            var after = Directory.Exists(realRoot)
                ? Directory.GetFileSystemEntries(realRoot).OrderBy(x => x, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
            Assert.Equal(before, after);
        }
        finally { CacheRoots.ResetForTests(); }
    }
}
