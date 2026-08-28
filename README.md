# BusinessCentral.AL.Runner

[![Test Matrix](https://github.com/StefanMaron/BusinessCentral.AL.Runner/actions/workflows/test-matrix.yml/badge.svg)](https://github.com/StefanMaron/BusinessCentral.AL.Runner/actions/workflows/test-matrix.yml)
[![NuGet](https://img.shields.io/nuget/v/MSDyn365BC.AL.Runner)](https://www.nuget.org/packages/MSDyn365BC.AL.Runner)

Run Business Central AL unit tests in **milliseconds** — no BC service tier, no Docker, no SQL Server, no license required.

## Changes on this temporary performance fork

This is a temporary performance fork of
[`StefanMaron/BusinessCentral.AL.Runner`](https://github.com/StefanMaron/BusinessCentral.AL.Runner),
staged for upstreaming. It changes two things: the **first** compile of a large app, and what a
**save** costs once the runner is watching. The product-code surface is small for the size of
the diff — **50 files, ~8,000 lines** in `AlRunner/`; almost everything else is tests and AL
fixtures (~18,200 lines across 240 files). The `tests/al-language` corpus pin is untouched.

Measured on **NP Retail** — npcore, two apps, ~7,300 `.al` files, ~6,950 AL objects, BC 28.1 —
as one bundle on an otherwise idle 6-core / 12 GB Mac. The runner is exactly as shipped: no
environment overrides, no flags beyond `--watch --test Codeunit85257`, which selects one
30-test suite. Each row is one scripted edit in a single `--watch` session, timed from save to
results on screen; the cold row includes compiling the bundle's dependency apps from AL.

Every warm row re-runs the same **30 test functions**, worth ~2 s of each number below — so
these are compile-plus-run cycles, not compiler timings. A wider `--test` filter grows every
row while the compile work stays identical.

| What you do | this fork | what the cycle actually did |
|---|---:|---|
| **Cold compile** — fresh cache, nothing warm | **282 s** | full compile of both apps + the delta baseline snapshot |
| **`--watch` — change a codeunit** | **5 s** | `delta +0 ~1 -0` · 1 object re-emitted in 0.8 s |
| **`--watch` — add a codeunit** | **5 s** | `delta +1 ~0 -0` · the new object only |
| **`--watch` — delete a codeunit** | **6 s** | `delta +0 ~0 -1` · nothing re-emitted, the name is tombstoned |
| First edit after the runner starts | **8 s** | `delta +0 ~1 -0` — the cold compile left a persisted baseline, so the first edit already deltas |
| Copy-paste a `.al` file → duplicate object id | **1 s** | `AL0264` reported, workspace untouched — **no rebuild, no test run** |
| Copy-paste a procedure → duplicate method | **2 s** | `AL0440` reported — **no rebuild, no test run** |
| Rename an object (same id, new name) | **6 s** | `delta +0 ~1 -0` — a rename is a delta, not an add+remove |
| Add a field to an existing table | **5 s** | `delta +0 ~1 -0` |
| Change an existing table field | **5 s** | `delta +0 ~1 -0` |
| Add an action to an existing page | **5 s** | `delta +0 ~1 -0` |
| Save a file back to its original bytes | **4 s** | **no compile at all** — the tree re-hashes identical |

Rows 2 and 3 are the ones that prove a delta *ran* rather than merely compiled: flipping a
`Label` the suite asserts verbatim turns it **28 pass / 2 fail**, and reverting it returns
**30 / 0**. The delta-compiled application code really executed.

Those seconds are end-to-end, and compilation is the small part. On a representative warm
cycle (change a table field, 5 s on the clock) the delta compile — bind and generate C# for
the one changed object — is **0.65 s**, and **~2 s** is executing the 30 tests. The rest is
fixed per-cycle overhead the delta cannot touch: the quiescence debounce, symbol registration,
tree hashing, the dashboard, and the harness's one-second polling granularity.

### Cold-compile performance

Nothing here changes *what* is compiled — only how the work is scheduled, how often the same
bytes are read, and how much of the heap is live at the peak.

- **Run under Server GC** (`AlRunner.csproj` → `ServerGarbageCollection`) — the single biggest
  lever. A cold AL compile
  is GC-throughput-bound: the Application module's emit produces 165 MB of C# that becomes
  ~330 MB of UTF-16 and stays reachable until Roslyn finishes. Upstream sets no GC property and
  ships no `System.GC.Server` in `runtimeconfig.json`, so every user runs Workstation GC.
  Identical binary, GC flavour the only difference: BC AL emit 837.7 s → 331.3 s, Roslyn
  bind + IL 293.9 s → 103.8 s, wall 1,283 s → 611.5 s on the slowest measured leg and 432.6 s
  on the fastest. Faster *and* 0.5 GB smaller resident, because the Workstation run spends the
  difference thrashing. Regression cover: `AlRunner.Tests/ServerGcConfigTests` — asserting the
  *shipped* `runtimeconfig`, because a behavioural check passes for the wrong reason on any box
  already exporting `DOTNET_gcServer=1`.
- **Stop binding every tree twice** (`BcAssembler` → `CallSiteArgWrap.TryRewrite`). Upstream's
  `CallSiteArgWrap.Apply`
  builds its own `CSharpCompilation` and calls `GetDiagnostics()` over every tree purely to
  harvest the CS1503 `ByRef<T>` gaps BC's emitter leaves, then the real emit binds the identical
  trees again. On npcore that first bind costs **79.8 s over 6,947 trees and 139,495
  diagnostics, and performs 0 rewrites.** `CallSiteArgWrap.TryRewrite` now consumes the real
  emit's diagnostics and returns `null` when there is nothing to do, so the pass only costs
  anything on the compiles that actually need it.
- **Run BC's emit across threads** (`BcCompiler.ConcurrentEmitEnabled`). `ConcurrentEmit`
  defaults to false on both `CompilationOptions` and `EmitOptions`, so every one of npcore's
  objects reaches `AddApplicationObject` on one thread while the bind phase beside it uses the
  whole machine. With the arms interleaved under Server GC, the worst on-leg beats the best
  off-leg by **1.45x** (median 1.8x) with no heap penalty. `AL_RUNNER_BC_CONCURRENT_EMIT=0` is
  the escape hatch. The determinism this costs is stated in the code: object arrival order —
  hence the emitted assembly's member layout — is no longer fixed, so
  `BcCompiler.OrderCapturesDeterministically` sorts captures by (Name, Code) before use.
- **Parallelise the AL source-tree parse** (`RecordPatches.ParseSourceFilesIntoAllExtractors`).
  Registering the source tree is the largest wholly
  serial pass left, CPU-bound in the AL parser. Only the parse moves off the calling thread:
  reads stay serial so an unreadable file throws exactly the exception it always threw rather
  than an `AggregateException`, and the eight metadata extractors still run serially in file
  order, so every de-dup and last-writer-wins rule sees the identical sequence. Batched at 256
  files (`RecordPatches.SourceFileBatchSize`), so the extra live set is bounded by the batch,
  not the tree.
  Alongside it, the **declared-object census** runs in parallel (`Program.RunEmit`), and a
  parse that throws is **memoized as an answer** (`RecordPatches.AlSourceParser`)
  so one doomed file costs one attempt instead of one per extractor.
- **Parallelise the Roslyn half** (`BcAssembler`): per-source parse inside a `Parallel.For`
  with `DocumentationMode.None`, one `MetadataReference` per
  (path, mtime, length) shared process-wide — upstream creates them per compile, so
  ~80 unchanging assemblies were re-indexed per app group *and* on every watch cycle — and
  `ApplyPolyfillRedirects` as one left-to-right walk instead of 35 `String.Replace`
  sweeps per source.
- **One `.app` read answers both metadata questions, and the package scan fans out**
  (`AppLoader.ReadPackageMeta`; the `BcCompiler` package scan). `ReadManifest` and
  `HasSymbolReference` are
  answered from the same central directory but asked separately — 226 reads per scan of the
  113-package / 138 MB platform-apps directory. `HasSymbolReference` also stopped pulling the
  whole package into a `byte[]` to look for one entry: per read of
  `Microsoft_Base Application`, 439 MB allocated before, 45 MB after. That is what makes the
  scan safe to parallelise — reads fan out, dedup decisions stay serial and in the original
  sequence.
- **Release BC's compilation before the Roslyn compile** (`Program.RunEmit` →
  `BcCompiler.ReleaseLastCompilation`). The bound
  AL compilation and Roslyn's compilation of the C# it produced are consecutive, not concurrent,
  so holding the first reachable through the second doubles the peak on the phase that already
  sets it. The delta baseline is built as plain data immediately after the emit; only the
  *write* stays after the load, which is all the "a rejected candidate must not become a cache
  entry" invariant needs. **Ships with the next item or not at all** — see below.
- **Drop the emit-phase deadline and `AL_RUNNER_EMIT_TIMEOUT_SEC`** (`Program.RunEmit`) — the
  change that decides whether an app this size runs at all. How long an emit takes is a function
  of app size and host speed, and the runner can predict neither: npcore's Application group
  emits in 89 s on an idle machine and 333 s on a loaded one, so any fixed budget either aborts
  a legitimate compile or is too loose to catch a real hang. Upstream's budget is a flat 120 s
  with no `--watch` waiver; measured on this corpus, an unmodified upstream ends every cycle —
  cold and warm alike — in `EMIT-TIMEOUT after 120s`, and because the cold compile never
  finishes it never records a baseline for the next cycle to delta against either. The timeout
  also abandons the wait without cancelling, so the emit keeps burning cores and re-pins its
  bound compilation through a late `LastCompilation` assignment — which is why this and the
  release above are one change, not two.
- **`--no-cache` disables every on-disk cache, not just `al-out`** (`CacheRoots.DisableForRun`).
  Upstream leaves compiled dependency DLLs, the Cecil-rewritten `Ncl`, parsed `.app` symbol
  tables, extracted R2R chunks and the manifest index in place — worth tens of seconds in
  exactly the situation the flag is reached for. It redirects to a throwaway per-run directory
  rather than deleting anything, so it cannot erase `~/.cache/al-runner` or sabotage a
  concurrent run, and `--cache` / `--no-cache` are now last-wins.
- **Two whole-tree questions per cycle are deferred** (`GetOrderedDepIds` and
  `BcCompiler.BundleDeclaresQuery`, behind the AL-output cache gate).
  `GetOrderedDepIds` and `BundleDeclaresQuery` each feed one consumer, and both consumers sit
  behind the AL-output cache gate — yet both ran ahead of it, every cycle. One rebuilt a second
  `DependencyResolver` index by re-reading every `.app` manifest out of its zip; the other read
  the whole tree to prove an app declares no query (12.7 MB on npcore, and the common case).
- **Diagnostics and platform fixes that made the rest measurable.** The phase log reports a real
  peak RSS on macOS via `getrusage` (`PhaseLog.PeakRssBytes`) instead of the silent zero
  `Process.PeakWorkingSet64` returns there, plus a `server_gc` field on the process row — which
  is what makes "which phase costs seconds", "which phase holds gigabytes" and "which GC was
  this subprocess" separately answerable. `EXEC-FAIL` now names the app group in `Program.cs`;
  before, an app's entire test set could vanish from a run with the exec-fail counter still
  reading 0. Bundle discovery no longer collapses `Application` and `Test` into one module on
  case-insensitive filesystems. Two CI gate
  scripts parse VSTest's timestamps on Python 3.9 and hold the server-mode FIFO open without
  GNU `sleep infinity`, so they run on macOS at all.

### Watch-mode delta compilation

This is not a set of optimisations on upstream's watch loop — it is a different architecture.

Upstream keeps a per-`BcCompiler`-instance, in-memory `_radBaselines` dictionary as its baseline
(`BcCompiler.Incremental.cs`). On its happy path `TryEmitIncremental` swaps the changed object's
generated C# into the baseline's C# for **every** object and hands that whole union to Roslyn,
producing a new whole-module assembly per save. Its unit of change is one content-edited `.al`
file declaring exactly one id-bearing object whose `(Kind, Id, Name)` is unchanged; outside
that it has **14 distinct fallback triggers** to a full whole-module compile — an added, removed
or renamed file, an id-less object kind, a file declaring anything other than exactly one
object, a changed id or name, and, because the baseline lives only in memory, **the first cycle
of every process**.

This fork keeps a persistent per-app **workspace** (`AlRunner/Rad/`, 4,933 lines), compiles only
the changed objects, and loads the result as a small **overlay assembly beside** the generations
already loaded. On a delta cycle the module is never rebuilt and never reloaded. Both sides
drive BC's own `Compilation.CreateForRad`; what differs is what surrounds it.

```text
 editor     WatchSource     RadWorkspace      CreateForRad       Roslyn        CLR
    │            │                │                 │               │           │
    │ save BloomFilter.Codeunit.al│                 │               │           │
    ├────────────►                │                 │               │           │
    │            ├─ drain changed paths             │               │           │
    │            ├────────────────►                 │               │           │
    │            │                ├─ HashSourceTree — SHA-256 per file
    │            │                │  DiffFiles → 1 file moved   (none → NO COMPILE)
    │            │                │  declaration probe over the CHANGED FILE ONLY
    │            │                │    → RadObjectKey(Codeunit, <id>)           │
    │            │                │  + one reverse-ref hop, diffed MEMBER BY MEMBER
    │            │                │    (a body-only edit rebinds nobody)        │
    │            │                │  strip that key from the packaged ModuleDefinition
    │            │                │ 1 object + baseline symbols     │           │
    │            │                ├─────────────────►               │           │
    │            │                │                 ├─ BC's own RAD factory:    │
    │            │                │                 │  declaration diags FIRST, │
    │            │                │                 │  then bind + gen C# for   │
    │            │                │                 │  that ONE object          │
    │            │                │                 │  AL error → widen once,   │
    │            │                │                 │  retry; still bad →       │
    │            │                │                 │  FULL COMPILE, with a reason
    │            │                │                 │ C# for that ONE object    │
    │            │                │                 ├───────────────►           │
    │            │                │                 │               │ overlay assembly → gN
    │            │                │                 │               ├───────────►
    │            │                │ Commit — and ONLY now           │           │
    │            │                ◄─────────────────────────────────────────────┤
    │            │                ├─ register gN, tombstone removed CLR names,  │
    │            │                │  apply buffered metadata, merge into baseline
    │            │                │  (load failed? baseline untouched, save retries)
    │            │                │ run the selected tests in-process           │
    │            │                ├─────────────────────────────────────────────►
    │            │ results → dashboard, then AwaitChange            │           │
    │            ◄──────────────────────────────────────────────────────────────┤
```

Two properties of that flow carry most of the difference. The overlay is loaded *beside* the
previous generations, not instead of them: `Rad/AlObjectResolution` resolves each AL object to
the generation that owns it in O(1) (`AlObjectResolution.FindOwned`) and tombstones the CLR names
a delta removed (`AlObjectResolution.IsTombstoned`), so there is no whole-module reload and no
bound on how many saves can accumulate. And `Commit` is the only thing that advances the
workspace, running **after** the assembly loads (`RadEmitResult.Commit` →
`RadWorkspace.Commit`) — so an AL diagnostic, a
rejected C# candidate or a failed load leaves the last good baseline exactly where it was, and
the next save re-diffs against it.

Because .NET cannot unload an assembly, "loaded beside" has to be enforced at every point that
maps an AL id to a CLR type or scans loaded assemblies. Superseded-generation checks are
threaded through six runtime surfaces: event subscription
(`AlObjectResolution.IsSuperseded` — without it every `[EventSubscriber]` on a replaced
codeunit fires twice), test discovery (`TestExecutor`), install triggers (`InstallTriggerRunner`), the
id→type lookups for query/report/page/TestPage/codeunit (`CodeunitPatches`), and
the live-report set BC reads to decide a report exists
(`RecordPatches.ReportMetadataVirtualTable`).

Noteworthy changes, in the order they matter:

- **`--watch` is object-granular.** Delta compilation is what `--watch` does; `AL_RUNNER_RAD=0`
  (`RadWorkspaceStore.Enabled`) forces every cycle to full-compile as a bisect switch. Runtime metadata
  registered by an AL emit — page, report, xmlport and enum-registry writes that happen *before*
  Roslyn and before `Assembly.Load` — is buffered in `RadMetadataCapture`
  (`Rad/RadMetadataCapture.cs`, deferred from `BcCompiler`) and applied only once the
  generation loads, so a candidate the C# backend rejects cannot leave the live runtime
  describing objects whose code never loaded.
- **The baseline is persisted beside the cached AL output** (`Infrastructure/AlCacheSidecars`).
  An in-memory baseline cannot escape this trap: a cache HIT skips Emit+Compile entirely, so
  there is no compile left to build a baseline *from*, and the first edit of every session pays
  a full compile just to establish one. Two sidecars carry the state — `<key>.rad-symbols.json`
  is the `ModuleDefinition` in BC's serialized form, `<key>.rad-baseline.json` the envelope
  (schema version, signature, app root, per-file hashes, one-hop reference graph, cross-app
  edges, extension targets). Hydration re-hashes the tree and refuses unless every file matches
  (`RadBaselineSidecar.TryHydrate`), parking the reason on the workspace so the fallback
  explains itself. Both sidecars are deliberately excluded from the cache-completeness gate —
  requiring them would turn every entry written by a one-shot run, which is all of CI's, into a
  MISS.
- **A warm cycle re-parses only the files that moved** (`RecordPatches.SourceFileExtracts.cs`).
  `RecordPatches` is the runner's stand-in for BC's metadata service, so AL source is the only
  description of table/page/report/query/xmlport shape it has — and every save clears its
  dictionaries and re-derives all of them, re-reading and re-parsing the whole tree to service
  an edit to one file. That is the largest single line item in a warm cycle, several times the
  AL delta emit it exists to serve. Each of the eight extractors is split into a pure
  `Extract*(text)` half and a stateful `Apply*(records)` half, memoized on
  (path, content hash, preprocessor symbols), and the unchanged files' records are **replayed**
  in enumeration order — the `tableextension` dictionaries accumulate by base-table name in AL
  declaration order, so one file's contribution cannot be subtracted, only re-applied. On the
  corpus and host the table above was measured on, the stage goes **1.3–1.9 s → 0.24–0.61 s**
  per warm cycle.
- **Id-less object kinds delta instead of rebuilding the module** (`RadObjectKey.IsIdlessKind`).
  `interface`, `controladdin`, `profile`, `pagecustomization`, `profileextension` and
  `entitlement` have no object id. On NP Retail that is 84 files where any edit — a comment
  included — is a guaranteed whole-module rebuild upstream. `RadObjectKey` carries a `Name`
  used as the discriminator when the *kind* is id-less. They are binding
  contracts too, so the reverse dependency graph records edges onto them: widening an interface
  without touching its implementer must not report success, emit nothing, and leave the
  implementer bound to the old contract.
- **The rebind is decided member by member** (`ModuleDefinitionOps.CompareObjectSurface`).
  Comparing a modified codeunit's whole canonicalised
  symbol makes any edit that touches a codeunit at all re-emit its complete caller set. It now
  compares the shell wholesale and `Methods` as a multiset keyed on (Name, Id), rebinding only
  when a member is gone, a member's fingerprint moved, or a member was added under a name the
  object already had. It is never keyed on "did the id move?" — `CalculateMethodId` hashes only
  the name, the bare return kind and each parameter's (index, IsVar, kind), so access
  modifiers, attributes and parameter names all leave a member id bit-identical.
- **Bystanders that lose derived surface are rebound** (`TryRebindDamagedBystanders` →
  `DamagedBystanderFiles`).
  A delta strips every modified object from the packaged `ModuleDefinition` so the new source
  binds; what does not survive that is surface an *untouched* object holds only because the
  stripped object exists — a contributed field, an implemented interface's conformance, the
  `Run(Record)` overload `TableNo` confers. The damage is latent: it becomes real only when
  something in the same delta binds to the missing part, and then it is loud. So the repair
  hangs off all three points the delta can return an AL error, keyed on four structural rules,
  and a cycle that binds clean pays nothing. A second repair (`TryReplaceStrippedSurface`)
  covers the packaged surface going unresolvable, detected off the `__MissingTypeSymbol__`
  marker rather than reported as a spurious `AL0133`; failing both, the cycle fails closed to a
  full compile.
- **Cross-app rebinding** (`Rad/RadAppCohort.cs`). Generated AL calls bake Microsoft's
  member id, which is a hash of the callee's signature — so when app A re-emits a moved surface,
  every app that *calls* it is left executing IL that dispatches A's previous id. Loud when the
  retired id is gone; **completely silent** when it survives, because adding an overload moves
  which id the caller bakes without moving the callee's own. `RadAppCohort` maps compiler
  `AppId` to workspace identity for one bundle, edges into sibling *source* apps are retained by
  (app identity, `RadObjectKey`), and the generation watermark is published only from a committed
  emit (`RadEmitResult.Commit`) so a generation the C# backend rejects announces nothing. A
  bundle whose apps would compile under one `AppId` is refused outright
  (`RadAppCohort.Build`) — the reference graph could not tell their objects apart, so
  rebinding would silently bind one app's callers to the other's surface.
- **States that no longer cost a whole-module compile.** The overlay chain is unbounded in the
  `Program.cs` watch loop, so no save is silently the expensive one. `app.json` is hashed by
  *content* (`RadWorkspace.HashSourceTree`), so a branch switch, a checkout, an editor autosave —
  and on macOS/APFS even reading the tree with `File.Copy` — no longer charges the whole bundle
  a rebuild for a byte-identical manifest. And a **duplicate declaration** now emits `AL0264`
  for an id-keyed kind or `AL0197` for a name-keyed one, instead of surrendering the module:
  two objects in one
  app cannot share an id or a name, so the compiler's answer is always the same and the rebuild
  bought a diagnostic and nothing else — for the most ordinary way a developer starts a new
  object, copying an existing `.al` file.
- **A file that declares nothing costs nothing.** Creating an empty `.al` file, editing a
  comment-only one, or deleting it again compiles nothing at all. "Declares nothing" is read
  positively off BC's `ObjectSyntax` parser in `Program.cs` — `ObjectSyntax`
  is the base type of every AL top-level declaration — rather than as the absence of a symbol,
  which is equally what an unidentifiable declaration looks like.
- **The delta gets the app's file system** (`AppFileSystem`, passed to `CreateForRad`) so
  `AL0327` "missing file" for a `controladdin`'s scripts is answered rather than escalated. This
  is BC API-shape sensitive: the file system has to go in through `CreateForRad`'s `fileSystem`
  **constructor parameter**, because attaching it afterwards with `.WithFileSystem(...)` returns
  a compilation that has lost the packaged module.
- **Every fallback names its cause where the developer is looking.** The dashboard redirects
  both console streams while the `Program.cs` watch loop runs, so reasons written
  to stderr were being discarded in the one mode they exist for. `FullCompileBecause`
  (**18 call sites**) now writes to `RadCycleNotes` as well as the log, and the dashboard renders
  two panels above the test tree — yellow for a full recompile, blue for a delta rebind
  (`WatchDashboard.cs`, `FullCompileNotes` / `RebindNotes`).
- **The file watcher feeds the delta rather than just waking it.** `WatchSource` queues every
  changed path on a concurrent `ChangedPaths` queue instead of raising a
  bare signal, watches `app.json` alongside `*.al`, enqueues **both** ends of a rename,
  and arms its watchers once per process rather than per idle wait — a save landing while
  watchers are torn down is lost outright, since inotify has no backlog.
- **A warm cycle reports the same run as the cold one.** Four defects outside the delta path
  made the same unedited bundle report different test sets cold and warm: event dispatch armed
  after the install seed (`TestExecutor`), a table publisher's `IncludeSender`
  argument passed as null (`CodeunitEventDispatcher.IsNonCodeunitSenderParameter`), page and xmlport
  metadata cleared by neither branch of the reload (`BcRuntime.ResetForNewBundleReload`), and manual event
  bindings owned by a `SingleInstance` codeunit surviving its reset
  (`CodeunitEventDispatcher.ResetManualBindingCacheForReload`). The last two also
  affect non-watch runs. Separately, the enum registry is now keyed by extension name with
  `EnumMetadataPatches.Remove` / `RemoveExtension`; appending left a base enum
  carrying pre-edit *and* post-edit values after a warm cycle re-emitted an `enumextension`.
- **The reference-graph walk no longer swallows its own faults** (`MapObjectReferences`).
  A per-node `catch { }` absorbs any fault the walk's own `Parallel.For` introduces, and a lost
  edge is indistinguishable from an object that calls nothing: the symptom is not an error but a
  caller that should have rebound and did not, several cycles later, reported green. Faults now
  propagate via `ExceptionDispatchInfo` and both callers fail safe. Measured against BC 28.1 the
  case the catch existed for does not occur — six malformed shapes put 168 nodes through
  `GetSymbolInfo` and got 168 answers.
- **The suites were made to prove things.** Thirty RAD tests reported `Passed` while asserting
  nothing, and the drift guard meant to catch exactly that
  (`TestArtifactsGateTests.NoTestSilentlyReturnsWhenItsEnvironmentIsUnavailable`) could not see
  them — its `[^\n]*` could not
  cross the newline between the `WriteLine` and the `return`. The cross-app rebind claims, the
  by-name property shapes, the two symbol producers' member-for-member equivalence and the
  silent same-app overload hazard are each now pinned by a test verified to fail without its fix.

#### Known gaps

Open, and deliberately not presented as solved:

- **A cache HIT can walk past a cross-app rebind.** The AL-output cache key hashes only its own
  app's files, so a consumer served from cache never reaches the delta path and loads a DLL
  compiled against a sibling's previous member ids. Under `--watch` that is cycle 1 only; a
  one-shot run has no workspace, so every app is eligible every time.
- **The reference graph is one-way for id-less kinds.** `UniquelyKeyedObjects` returns only
  `IApplicationObjectTypeSymbol`, so `interface` / `controladdin` / `entitlement` are tracked as
  reference *targets* and never as reference *sources*.
- **A sidecar schema refusal does not heal.** The refusal is a whole-*bundle* compile, and the
  save path is guarded on paths only assigned while no generation is loaded — a cache HIT loads
  one immediately, so every later watch session pays it again.
- **First-edit-deltas has one measured exception**: an app that *another app in the bundle
  depends on* still pays a whole-module compile on the first edit after a one-shot run, because
  the one-shot publishes its symbols in a pre-pass whose reference set the watch path cannot
  reproduce.
- **Watcher-buffer overflow is detected but not acted on.** `WatchSource.Overflowed`
  is computed and never read, so a cycle can still run against a
  partially-reported tree without forcing a full pass.
- **The dispatch weights for the five new watch/RAD test collections are local, not CI**
  (`CollectionCostOrderer`), and local and CI disagree by up to 3x on
  the same class.

### CLI provisioning

- **`--auto-provision` now works from empty project caches.** The runner derives the Microsoft
  platform and test-app sets from every target `app.json`, downloads them for the selected full
  BC version, and caches them under
  `~/.local/share/al-runner/artifacts/<version>/{platform-apps,test-apps}`. A warm run checks and
  reuses those destinations before contacting the CDN, and adds them to dependency resolution
  automatically. In packaged builds with baked version metadata, provisioning pins the exact BC
  build this binary was compiled against, even if another minor from the same major is already
  cached. If that exact build cannot be obtained, provisioning stops instead of silently running
  against another minor; an explicit version remains available as a known-degraded override.
  Older builds without a full four-part baked version retain the legacy fallback. No
  `--artifact-path` or `--package-cache` is needed: for example,
  `al-runner <bundle> --watch --auto-provision --test 123456` provisions once and then stays
  resident.

### Upstream integration status

StefanMaron upstream through `b904dc73` is merged. Where both repositories fixed the same bug,
the merged tree uses upstream's broader implementation for stack-preserving rethrows, real-page
metadata reload, and call-stack capture. It keeps the fork's `IncludeSender` classification
because `ParameterType.IsInstanceOfType(publisher)` also handles page, report, query, and xmlport
publishers.

Upstream's narrower `BcCompiler.TryEmitIncremental` implementation and its tests remain in the
tree, but it has no production call sites. Production watch routing stays with the persistent
`RadWorkspace`; the two engines cannot both own the baseline. Any upstream PR for `AlRunner/Rad/`
must either keep `TryEmitIncremental` test-only or remove it with its tests.

The remaining fork-specific work is independently upstreamable in three tiers:

| Tier | Unit |
|---|---|
| **1** — self-contained | Server GC · the double-bind removal in `CallSiteArgWrap` · Roslyn 5.6.0 + the explicit `LanguageVersion` pin · the Roslyn-half parallelism and reference cache · `ReadPackageMeta` + parallel package scan · parallel AL parse + negative parse memo · `--no-cache` coverage · macOS peak RSS + `server_gc` · bundle-discovery case-sensitivity · `EXEC-FAIL` app naming · the `SingleInstance` event-binding leak · page/xmlport registry clearing · the enum-registry keying · event-dispatch ordering before the install seed · the `TestArtifactsGateTests` regex · the two CI gate scripts |
| **2** — ships as a pair | Releasing BC's compilation **+** dropping the emit deadline. With the deadline in place a timed-out emit re-pins the heap through a late `LastCompilation` assignment after the release, so the release alone buys nothing. |
| **3** — one indivisible unit | `AlRunner/Rad/` and everything that enforces it: the six superseded-generation checks, the cache sidecars, the dashboard note panels. `RadObjectKey`'s identity, `RadWorkspace`'s reference graph, `ModuleDefinitionOps`' canonicalisation and member-level compare, and the three chained repair retries are mutually dependent by construction. |

Two items are worth sending upstream ahead of the core, because they fix upstream's *own*
incremental path: `app.json` content-hashing (upstream falls back on any manifest write, even a
byte-identical one), and the `CreateForRad` `fileSystem`-constructor-vs-`WithFileSystem`
distinction, which upstream currently gets wrong in `TryEmitIncremental`. The
reason-reporting channel is portable too — upstream computes a `fallbackReason` and only shows
it under `--verbose`.

Concurrent BC emit sits between tiers: the emit change itself is self-contained, but its
`AsyncLocal` capture binding is only load-bearing once a delta-metadata registry exists.

The `LanguageVersion` pin is worth calling out as *not* a performance change — it is defensive.
`LanguageVersion.Default` tracks the referenced Roslyn's newest major, so the 4.14 → 5.6 bump
would silently reinterpret BC's generated C# as C# 14, which made `field` a contextual keyword
inside property accessor bodies — exactly the kind of identifier an AL-to-C# emitter produces.

## What It Is

AL Runner is a standalone test executor for Business Central AL code. It loads the **unmodified** Microsoft `Microsoft.Dynamics.Nav.*` DLLs (those shipped inside `.app` packages and BC artifacts), compiles your AL source through BC's own `Compilation.Emit` pipeline, and executes the resulting test codeunits in-process against the real BC business logic.

There is no service tier, no SQL Server, no NST, and no rendered UI. There is also no "mock layer" — types and method bodies inside the precompiled MS / ISV DLLs run exactly as MS / the ISV compiled them. Where the runner stands in for the service tier (database persistence, session state, table provider, event dispatch) it does so by patching the **runtime engine** (`Microsoft.Dynamics.Nav.Ncl.dll`) at load time, not by editing business-logic DLLs.

See [`docs/subsystems.md`](docs/subsystems.md) for the subsystem map, [`docs/cecil-migration.md`](docs/cecil-migration.md) for the Cecil-rewrite contract, [`docs/scope.md`](docs/scope.md) for what is in / out of scope, and [`docs/limitations.md`](docs/limitations.md) for the hard architectural limits.

## Architecture

```
AL source (.al files)
   |  BcCompiler           — BC's own Compilation.Emit() drives AL → IL
   |  BcAssembler          — Roslyn compiles the small C# polyfill bodies BC asks for
   |                         (call-site arg-wraps, lambda thunks); IL is byte-equivalent
   |                         to what BC's pipeline would produce
Test assembly (in-memory, cacheable)
   |  TestExecutor         — discovers [NavTest] methods, runs them with the chosen
   |                         isolation mode against real BC dispatch
Results in milliseconds
```

What the runner does **not** do:

- It does not rename BC types. No `NavRecordHandle → MockRecordHandle` substitution. The same `NavRecordHandle` that the precompiled BaseApp / SystemApp DLLs reference is the one your tests execute against.
- It does not rewrite method bodies in `*.SystemApplication.dll`, `*.BaseApplication.dll`, or any ISV business-logic DLL. Those bodies are the contract the runner exists to validate.
- It does not stub dependencies. MS apps (System App, Base App, test toolkit) load as their real DLLs; ISV dependencies load the same way.

What the runner **does** modify:

- `Microsoft.Dynamics.Nav.Ncl.dll` — rewritten once via Cecil at startup and cached at `~/.cache/al-runner/ncl-cecil/<key>.dll`. This is the runtime-engine layer. See `AlRunner/Infrastructure/NclCecilRewrite.cs`. The tool package does **not** ship this DLL (it's Microsoft's, resolved from your own BC artifact cache at runtime, same as the rest of the BC service-tier closure) — on first run for a given install + BC version, `AlRunner/Infrastructure/NclShadowRuntime.cs` builds a small shadow runtime directory containing the rewritten copy and re-execs into it once; that shadow dir is then reused on every later run.
- Remaining R2R-reachable entry points — patched via JMP-hooks installed by `BcRuntime.EnsureApplied()` (`AlRunner/BcRuntime.cs`, `AlRunner/Patches/*.cs`).

This is the **precompiled-DLL contract** described in `.claude/rules/precompiled-dll-respect.md`. AL output the runner emits is governed by the same contract once finalised — it is meant to be cacheable on disk and reusable like any MS or ISV DLL.

## Quick Start

### Prerequisites

.NET SDK 9 or 10 — download from [https://aka.ms/dotnet/download](https://aka.ms/dotnet/download).

**Linux:** none — the BC service-tier DLLs contain a handful of genuine Win32
P/Invokes (e.g. `kernel32`'s locale APIs, reached by anything that evaluates a
`TextConstant`, including the standard upgrade-tag install-trigger pattern) that
the runner redirects to a small shim. A prebuilt `libwin32_stubs.so` for `linux-x64`
and `linux-arm64` ships with the tool, so no C toolchain is required out of the box.
If you're on a RID the release pipeline didn't prebuild for, the runner falls back
to compiling `AlRunner/Win32Stubs/win32_stubs.c` on first use, which does need a C
compiler (`cc`, `gcc`, or `clang`) on `PATH`; without one it fails loudly and names
the missing tool and the two ways to fix it — install one (e.g.
`apt install build-essential`) or build the shim yourself and point
`AL_RUNNER_WIN32_STUBS_SO` at the resulting `.so`. Not needed on Windows or macOS.

### Install

```bash
dotnet tool install --global MSDyn365BC.AL.Runner
```

On first run, the AL compiler and the BC service-tier DLLs (around 11 MB via HTTP range requests) are downloaded and cached. Works on Windows, Linux, and macOS.

On Windows, exclude the runner's cache/output directories from real-time antivirus scanning if you hit slow cold-start times — Windows Defender locking a just-written DLL for scanning is a known source of first-run delay (worked around automatically with a bounded retry, but excluding the folder avoids the wait entirely).

### Run

The runner takes one or more **bundle directories**. A bundle is an `app.json`-rooted AL project (the same shape every BC extension has). The `tests/al-language/` submodule is a canonical example.

```bash
# Run a single bundle
al-runner tests/al-language/tests/al-language

# Multiple bundles
al-runner ./app1 ./app2

# Specify package caches for dependency resolution (repeatable)
al-runner --package-cache "$HOME/.al-runner/platform-apps" tests/al-language/tests/al-language

# Choose test isolation (matches BC's "Test Runner - Isol. Codeunit" by default)
al-runner --isolation codeunit ./my-bundle
al-runner --isolation test     ./my-bundle
al-runner --isolation disabled ./my-bundle

# Cache compiled AL output between invocations
al-runner --cache ~/.cache/al-runner/al-out ./my-bundle

# JSON classification output
al-runner --out results.json ./my-bundle

# Verbose internal logging
al-runner --verbose ./my-bundle
```

Besides the AL-output cache above, the runner keeps the result of the dependency apps'
`Install` triggers plus `Company-Initialize` (codeunit 2) at
`~/.cache/al-runner/install-baseline/<key>.bin`, keyed by the dependency assembly set, the
runner build and the BC version. It is the same seeding either way — reloading it just
skips re-running those AL bodies in every new process (measured: 6.3s → 0.8s on a warm
single-fixture run). `--cache <dir>` relocates it with the other caches; set
`AL_RUNNER_NO_DEP_COMPANY_CACHE=1` to bypass it entirely (no read, no write) and force the
full computation.

### Watch mode (live dashboard)

```bash
al-runner <bundle-dir> --watch [--package-cache PATH ...] [--cache DIR]
```

Stays resident with dependencies + BC patches loaded once, and re-runs the bundle
**in-process** when AL source or `app.json` changes.

With a cold output cache, the first cycle performs a normal full compile and records a
baseline. A cache HIT serves cycle 1 from the cached DLL *and* hydrates the baseline
persisted beside it, so the first edit is a delta too — with two exceptions: an entry
written before that baseline existed makes the first edit pay one full-bundle compile to
establish every app's baseline, and an app that *another app in the bundle depends on*
still pays a whole-module compile on the first edit after a one-shot run, because the
one-shot published its symbols in a pre-pass whose reference set the watch path cannot
reproduce. Later cycles hash the complete `.al` source tree and
recompile only the AL objects that actually changed — of any kind, including id-less ones
such as a `controladdin`, and ones the save added or deleted — via BC's
`Compilation.CreateForRad` plus a small C# overlay loaded beside the warm module. A save
that changes no AL object at all (a new empty file, a comment-only one) compiles nothing
whatsoever. Only a change the delta path cannot classify falls back to a full compile: a
new dependency, a changed app identity or preprocessor set, a `dotnet` package
declaration, or a duplicate object id/name. Every one of those says which file and which
change caused it, in the dashboard as well as the log. Point it at a directory holding an
app and its test app and it watches both:

```bash
al-runner --watch --package-cache <deps-dir> path/to/repo   # repo/Application + repo/Test
```

A bulk change — a branch switch, a rebase, a formatter run — is deltaed as one change: all of
it re-emits together, and nothing else does.

How it decides what to recompile, which changes are delta-able, and what forces a full
rebuild: [Watch-mode delta compilation](#watch-mode-delta-compilation).

On an interactive terminal `--watch` renders a **live, non-scrolling dashboard** that repaints in place on each cycle (like vitest / cargo-watch):

```text
╭──────────────────────────────────────────────────────────────────────────────╮
│ al-runner my-app  ·  ● watching  ·  last run 11.45.05 · 0,9s                  │
╰──────────────────────────────────────────────────────────────────────────────╯

╭────────────────────────────────┬────────┬────┬───────────────────────────────╮
│ Test                           │ Status │ ms │ Message                       │
├────────────────────────────────┼────────┼────┼───────────────────────────────┤
│ Codeunit60110.Insert_OnInsert… │ FAIL   │ 38 │ Assert.AreEqual failed.       │
│                                │        │    │ Expected:<1>. Actual:<9>.     │
╰────────────────────────────────┴────────┴────┴───────────────────────────────╯

0P / 1F / 0E  ·  1 total    Ctrl+C to quit
```

The header status flips to `⟳ running…` while a cycle compiles+runs (so the cold first run never looks frozen) and back to `● watching` when idle. Rendered cross-platform (Windows/macOS/Linux) via [Spectre.Console](https://spectreconsole.net/).

When stdout is **not** an interactive terminal (CI, a pipe, VS Code, a test harness), `--watch` automatically falls back to plain line output (`PASS`/`FAIL` per test + a `[watch] waiting for AL source changes…` marker) and emits no ANSI/cursor control. There is no separate UI flag — `--watch` itself is the dashboard.

### Server mode (warm daemon for editor integrations)

```bash
al-runner --server [--package-cache PATH ...] [--cache DIR]
```

A long-running JSON-RPC daemon over stdin/stdout. Dependencies and BC patches load once; each `runTests` request re-emits the bundle warm and runs it in-process (~19s→~4s). stdout carries only the newline-delimited JSON protocol; logs go to stderr. The VS Code extension uses this. Full protocol + the same-bundle reload contract: [docs/server-mode.md](docs/server-mode.md).

### Debug adapter mode (breakpoints + stepping)

```bash
al-runner --dap [PORT] <bundle-dir>
```

A real Debug Adapter Protocol server (default port 4711) over a TCP socket: set AL breakpoints, pause execution, step through the paused code (`next`/`stepIn`/`stepOut`), inspect locals. No new AL→source mapping — it reuses BC's own `StmtHit`/`[SourceSpans]` instrumentation, the same mechanism `--coverage` and `--capture-values` already consume. Full protocol + current limitations (no VS Code launch configuration in this repo): [docs/dap-mode.md](docs/dap-mode.md).

### Precompile a single `.app` to a DLL

```bash
al-runner --precompile MyApp.app --out MyApp.dll [--package-cache PATH ...]
```

This dispatches the single-app compile-to-DLL path. The output DLL is bit-compatible with what BC's `Compilation.Emit` would produce against the same dependency set.

### Build from source

```bash
git clone --recurse-submodules https://github.com/StefanMaron/BusinessCentral.AL.Runner
dotnet build AlRunner.slnx -c Release -p:AllowBcArtifactDownload=true
dotnet run --project AlRunner -c Release -- tests/al-language/tests/al-language
```

`-p:AllowBcArtifactDownload=true` is needed only until the BC service-tier DLLs are
present — the runner never downloads them implicitly, so without the opt-in a fresh
clone fails the build with the explicit download command. Later builds can drop it.
See [CONTRIBUTING.md](CONTRIBUTING.md#dev-loop) for provisioning them as a separate step.

## CLI Flags

| Flag | Effect |
|------|--------|
| `--out PATH` | Write classification JSON to PATH (default `v2-classification.json`). |
| `--package-cache PATH` | Extra `.app`-package cache directory. Repeatable. |
| `--cache PATH` | Cache compiled AL output keyed on source + dep set + runner mtime. |
| `--no-cache` | Disable **every** on-disk cache for this run — AL output plus compiled-deps, workspace-deps, ncl-cecil, bc-symbols, app-manifests, r2r-chunks and install-baseline — not just al-out. `~/.cache/al-runner` is left untouched. Slow on purpose; use it to measure or reproduce a genuinely cold compile. `--cache DIR` and `--no-cache` are last-wins. |
| `--auto-provision` | Download missing engine, Microsoft platform, and Microsoft test-app artifacts for the selected BC version, cache them in the runner-owned versioned artifact root, and reuse them on later runs. In packaged builds, no explicit version/path selects the exact baked BC build; it will not silently substitute another minor. Requirements come from the target `app.json` files, so empty `.alpackages` directories need no cache-path flags. |
| `--isolation codeunit\|test\|disabled` | Test isolation mode. Default `codeunit`. |
| `--watch` | Stay resident with warm dependencies; on `.al` or `app.json` changes, recompile only the AL objects that changed and run **in-process**. Debounces on quiescence (default 250ms of no further event, capped at 10s) so a bulk multi-file rewrite — a branch switch, a rebase, a formatter run — settles before a cycle starts, instead of firing mid-checkout. Tune with `AL_RUNNER_WATCH_QUIET_MS` / `AL_RUNNER_WATCH_MAX_WAIT_MS`. |
| `--server` | Long-running JSON-RPC daemon over stdin/stdout (warm deps → ~19s→~4s/run). See [docs/server-mode.md](docs/server-mode.md). |
| `--dap [PORT]` | Debug Adapter Protocol server (default port 4711): set AL breakpoints, pause execution, inspect locals. Requires exactly one bundle path. See [docs/dap-mode.md](docs/dap-mode.md). |
| `--per-suite` | Legacy per-suite compile mode (diagnostic). Default is bundled-per-bucket. |
| `--bundled` | No-op alias for backwards compatibility. |
| `--verbose` | Show internal `[Component]` diagnostic logs. Equivalent to `AL_RUNNER_VERBOSE=1`. |
| `--show-pass` | Include PASS lines in per-test output. Equivalent to `AL_RUNNER_SHOW_PASS=1`. |
| `--precompile <input.app>` | Subcommand: compile one `.app` to a DLL via `--out`. |

This table is a selection. **`al-runner --help` lists every flag** — including `--test`, `--bc-version`, `--define`, `--coverage`, `--expectations`, `--output-json`/`--output-junit` and the provisioning flags — and `al-runner --guide` is the operating manual.

Environment variables: `AL_RUNNER_VERBOSE=1`, `AL_RUNNER_SHOW_PASS=1`, `AL_RUNNER_TRACE_NRE=1` (logs every first-chance NRE before AL `asserterror` swallows it).

## Test Corpus

The canonical AL test corpus lives in [`tests/al-language/`](tests/al-language/) — a read-only git submodule pinned at [`StefanMaron/BusinessCentral.AL.Language.Tests`](https://github.com/StefanMaron/BusinessCentral.AL.Language.Tests). Each test is a behavioural contract validated against a real BC service tier. The runner runs that corpus unmodified; tests it cannot execute by design (SMTP, real HTTP, report rendering, etc.) are declared in [`tests/expectations/`](tests/expectations/) using the schema in [`docs/expectations.md`](docs/expectations.md).

Runner-specific positive tests (e.g. "this surface must throw `RunnerOutOfScopeException` with reason X") live in [`tests/runner-extras/`](tests/runner-extras/).

See `.claude/rules/al-language-submodule.md` for the read-only contract.

## What's Supported

The goal is broad AL-language compatibility: any AL code that can run without the BC service tier should compile and execute here. The runner targets the whole AL surface — records (CRUD, filters, keys, CalcFields, CalcSums, triggers), codeunits (interface dispatch, event subscribers, BC lifecycle events), test toolkit codeunits (`LibraryAssert` 130, `Any` 130500, etc.), test handlers (Confirm, Message, ModalPage, Request, Report, Notification), TestPage, RecordRef / FieldRef, BLOB / streams, JSON / XML, regex, in-process crypto, IsolatedStorage, TaskScheduler (synchronous dispatch).

Out of scope by design: SMTP, HTTP egress to external services, file I/O against external filesystems, OData / SOAP publishing endpoints, physical printers, background-job scheduling against a real scheduler, page / report **rendering** (handler callbacks fire; layout is not evaluated). These surfaces throw `RunnerOutOfScopeException` with a named API and reason — they never silently return defaults. See `.claude/rules/loud-failures.md` and [`docs/scope.md`](docs/scope.md).

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | All tests passed |
| `1` | Test assertion failures, runner errors, or argument error |
| `2` | Runner limitations only |
| `3` | AL compilation error |

## Knowledge graph (optional)

This repo — the C# runner and AL sources alike — can be indexed into a queryable knowledge
graph: communities, most-connected types, import cycles. Built with
[graphify](https://github.com/safishamsi/graphify).

**Install the AL-aware fork**, not the upstream package:

```bash
uv tool install --upgrade "git+https://github.com/ChristianHovenbitzer/graphify-al.git@al-support"
```

The fork adds `.al` to the file detector and an AL extractor. Upstream graphify has no AL
support, and it does not fail on `.al` files — it skips them silently, so a graph built with it
looks complete while containing none of the AL in this repo. Upstream is enough if you only ever
index the C# under `AlRunner/`, but anyone working on this project reaches AL sooner or later.
Install the fork once and the question does not come up.

Then, from the repo root:

```bash
graphify AlRunner              # index the C# runner
graphify tests/runner-extras   # or an AL tree (needs the fork)
graphify query "<question>"    # ask it something
graphify AlRunner --update     # refresh after changes
```

Output lands in `graphify-out/` (`graph.html`, `graph.json`, `GRAPH_REPORT.md`). It is gitignored
— it is derived, several MB, and goes stale quickly.

`AlRunner/` is code-only, so extraction is deterministic AST work and costs no LLM tokens.

One limit worth knowing before trusting it: the graph is static. A `Hook(...)` registration that
never fires and one that does look the same in it. For that question use `AL_RUNNER_HOOK_AUDIT=1`,
which measures at runtime.

## Reporting Gaps

If AL code fails to run and the reason is not in [`docs/limitations.md`](docs/limitations.md) or [`docs/scope.md`](docs/scope.md), that is a **runner gap**. Open an issue with `.github/ISSUE_TEMPLATE/runner-gap.md`. Silent workarounds are forbidden (`.claude/rules/file-issues-for-gaps.md`).

## License

MIT
