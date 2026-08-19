namespace AlRunner.Infrastructure;

/// <summary>
/// Process-global override for where the four caches that used to hardcode
/// <c>~/.cache/al-runner/&lt;name&gt;</c> actually write (issue #1821):
/// <c>compiled-deps</c> (<see cref="AlRunner.DependencyLoader"/>), <c>workspace-deps</c>
/// (<c>Program.cs</c>'s layered-workspace synthesis, two call sites), <c>ncl-cecil</c>
/// (<see cref="NclCecilRewrite"/>), and <c>bc-symbols</c>
/// (<see cref="AlRunner.Patches.BcAppSymbolCache"/>).
///
/// Only the AL-output cache (<c>Program.cs</c>'s <c>alCacheDir</c>, driven by
/// <c>--cache</c>/<c>--no-cache</c>) ever respected the <c>--cache</c> flag. The other
/// four always wrote to the real, shared, unscoped
/// <c>~/.cache/al-runner/&lt;name&gt;</c> regardless — so a caller that passes
/// <c>--cache &lt;isolated-dir&gt;</c> expecting per-invocation isolation (e.g. a test
/// using a fresh temp dir so each run starts from a clean slate) only actually got
/// isolation for AL output; the other four caches were silently shared, process-wide,
/// with every other <c>al-runner</c> invocation on the machine.
///
/// <para><b>Deliberately NOT wired into <c>alCacheDir</c> itself.</b> <c>alCacheDir</c>
/// keeps its pre-existing exact-directory semantics — writing straight into whatever
/// directory <c>--cache</c> names, no subfolder — because existing callers (tests,
/// <c>--watch</c>) already pass <c>&lt;root&gt;/al-out</c> as that value specifically for
/// AL-output isolation. This class instead resolves the OTHER four caches as named
/// subdirectories of that same <c>--cache</c> value (<c>&lt;dir&gt;/compiled-deps</c>,
/// etc.), so passing <c>--cache &lt;dir&gt;</c> isolates all five caches under one root
/// without changing what <c>al-out</c> alone has always done with that same value.</para>
///
/// <para>Set once at startup from the same value Program.cs assigns to <c>alCacheDir</c>
/// on a <c>--cache &lt;dir&gt;</c> flag. No flag at all (a bare-default run) means
/// <see cref="Resolve"/> falls back to exactly the same
/// <c>~/.cache/al-runner/&lt;name&gt;</c> path every one of these caches already
/// used before this issue was fixed — the default behaviour, including CI's own
/// caching (e.g. the <c>smoke</c> job's <c>rm -rf ~/.cache/al-runner/ncl-cecil/</c>,
/// which never passes <c>--cache</c>), is unchanged.</para>
///
/// <para><b><c>--no-cache</c> now means what it says</b> — see <see cref="DisableForRun"/>.
/// It used to disable the AL-output cache alone, so a "cold" run still had every derived
/// artifact handed to it: the compiled dependency DLLs, the Cecil-rewritten Ncl, the parsed
/// <c>.app</c> symbol tables, the extracted R2R chunks and the manifest index. That made the
/// flag misleading in the one situation it is reached for — measuring or reproducing a cold
/// compile, where the caches it left alone are worth tens of seconds.</para>
/// </summary>
public static class CacheRoots
{
    private static string? _override;

    /// <summary>
    /// Sets the process-global cache-root override for this run. Pass the exact value
    /// Program.cs's <c>--cache &lt;dir&gt;</c> parsing assigned to <c>alCacheDir</c>, or
    /// <c>null</c> when <c>--cache</c> was not given. Idempotent to call more than once; the
    /// last call wins, mirroring how <c>alCacheDir</c> itself is just a plain mutable local
    /// reassigned by whichever <c>--cache</c>/<c>--no-cache</c> argument appears last on the
    /// command line.
    /// </summary>
    public static void SetOverride(string? cacheDir) => _override = cacheDir;

    /// <summary>
    /// Points every named cache at a throwaway directory unique to this process, so the run
    /// reuses nothing from a previous one and leaves nothing behind for the next. What
    /// <c>--no-cache</c> means.
    ///
    /// <para><b>Redirected, not deleted.</b> Erasing <c>~/.cache/al-runner</c> would be a
    /// destructive side effect of a read-only-sounding flag, and it would sabotage any other
    /// <c>al-runner</c> running concurrently on the machine — CI runs four at once. Redirecting
    /// gives the same guarantee (this run starts cold) with none of that.</para>
    ///
    /// <para><b>Within the run the caches still work.</b> One invocation legitimately asks the
    /// same cache for the same key more than once — the R2R chunks of a package are consumed by
    /// several app groups, <c>ncl-cecil</c> is read after it is written — so a throwaway root
    /// keeps a run internally consistent while a fresh one per RUN is what makes it cold.
    /// Disabling reads outright would change behaviour, not just reuse.</para>
    ///
    /// <para><b>One root per RUN, not per process.</b> A cold run is not always one process:
    /// after a fresh Cecil rewrite the runner re-execs itself, because a process that rewrites
    /// Ncl and then loads the byte-identical result in-process intermittently dies with
    /// <c>BadImageFormatException 0x80131124</c> — the child exists precisely so the load comes
    /// off a cache HIT (see <c>NclCecilRewrite.RewriteInPlace</c> and the re-exec in
    /// Program.cs). Handing that child a NEW throwaway root would defeat the entire manoeuvre:
    /// it would MISS again, rewrite again, and then take the very load path the re-exec exists
    /// to avoid. So the root is published in the environment and the child adopts it. The first
    /// process still pays the full rewrite, which is what "cold" has to mean.</para>
    ///
    /// <para>Returns the directory so the caller can say where it went; a run that is suddenly
    /// tens of seconds slower should not leave the reader guessing why.</para>
    /// </summary>
    public static string DisableForRun()
    {
        var inherited = Environment.GetEnvironmentVariable(InheritedRootVariable);
        if (!string.IsNullOrEmpty(inherited))
        {
            // A re-exec'd child: adopt the parent's root and register no cleanup — the parent
            // waits for this process and owns the directory's lifetime.
            _override = inherited;
            return inherited;
        }

        var dir = Path.Combine(Path.GetTempPath(), "al-runner-nocache",
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        _override = dir;
        Environment.SetEnvironmentVariable(InheritedRootVariable, dir);
        // Best-effort: the run owns this directory outright, so removing it on the way out is
        // safe, and it runs after the re-exec'd child has been waited on. It can still fail —
        // an OS that locks a loaded assembly's file will refuse, and a hard kill skips the
        // handler entirely — so the name carries the pid and everything under
        // al-runner-nocache/ is disposable by construction.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        };
        return dir;
    }

    /// <summary>Carries the throwaway root across the Cecil re-exec. Not a user-facing knob:
    /// setting it by hand only makes a <c>--no-cache</c> run reuse that directory, which is the
    /// opposite of what the flag is for.</summary>
    private const string InheritedRootVariable = "AL_RUNNER_NOCACHE_ROOT";

    /// <summary>
    /// Resolves the on-disk directory for the named cache (e.g. <c>"compiled-deps"</c>).
    /// Returns <c>&lt;override&gt;/&lt;name&gt;</c> when <see cref="SetOverride"/> was
    /// last called with a non-null directory; otherwise falls back to
    /// <c>~/.cache/al-runner/&lt;name&gt;</c>, the pre-#1821 hardcoded default every one
    /// of these four caches used unconditionally.
    /// </summary>
    public static string Resolve(string name)
    {
        var root = _override ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "al-runner");
        return Path.Combine(root, name);
    }

    /// <summary>Test-only: resets the override so test processes/hosts that share this
    /// static (e.g. in-process unit tests, as opposed to the spawned-subprocess
    /// integration tests that get natural per-process isolation) don't leak state
    /// between cases.</summary>
    internal static void ResetForTests()
    {
        _override = null;
        // Also the re-exec hand-off, or one test's DisableForRun would make the next test's
        // look like a child adopting a parent's root.
        Environment.SetEnvironmentVariable(InheritedRootVariable, null);
    }
}
