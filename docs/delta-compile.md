# Delta compilation (`--watch`)

Delta compilation is what `--watch` does. With a cold output cache, the first watch cycle
performs a normal full compile and records a baseline for each source app. Subsequent cycles
use those baselines when an edit is safe to load as a small overlay. Other runner modes use
the normal compile path.

## How it fits together

The runner's normal pipeline compiles a whole module per run. `--watch` keeps that pipeline
and inserts one decision in front of the AL emit: **has this app got a usable baseline, and
is the change since the last cycle one a delta can express?** Everything downstream of the
emit — Roslyn, `Assembly.Load`, the test executor — is unchanged; a delta simply hands it a
much smaller assembly.

```text
                        ┌─────────────────────── one --watch process ───────────────────────┐
                        │                                                                   │
  al-runner --watch ────┤ WatchSource.ArmSourceWatch — FileSystemWatchers armed ONCE,        │
                        │   for the life of the process; every event is queued as a path     │
                        │   (ChangedPaths) and stamped on a WatchActivity                    │
                        │                            │                                       │
                        │      ┌─────────────────────▼─────────── cycle ──────────────────┐  │
                        │      │ drain ChangedPaths → RadWorkspaceStore.PrepareBundleReload│ │
                        │      │   (may warm metadata survive this reload?)               │  │
                        │      │                     │                                    │  │
                        │      │  per app group:  RunEmit ──► BcCompiler.EmitIncremental   │ │
                        │      │                     │            (see the cycle below)    │  │
                        │      │                     ▼                                     │ │
                        │      │  BcAssembler/Roslyn — compile the emitted C#              │  │
                        │      │  Assembly.Load — overlay (delta) or whole module (full)   │  │
                        │      │  RadEmitResult.Commit — only now does the workspace move  │  │
                        │      │  TestExecutor — run the selected tests in-process         │  │
                        │      └─────────────────────┬────────────────────────────────────┘  │
                        │                            │ dashboard repaint / results           │
                        │      AwaitChange: block on the signal, then wait for quiescence ───┘
                        └───────────────────────────────────────────────────────────────────┘
```

The cycle's own decision, per app, is a funnel — each step can only send work *down* to a
full compile, never up:

```text
   .al files on disk
        │
        ├─► RadWorkspace.HashSourceTree      SHA-256 per file (whole tree, ~0.14 s / 7k files)
        │
        ├─► ReferenceSignature + ws.ArmFor   app identity, preprocessor symbols, resolved
        │        │                            dep ids — a move here invalidates everything
        │        └── changed ──────────────────────────────────────────────► FULL COMPILE
        │
        ├─► ws.DiffFiles(hashes)             which files moved since the last commit
        │        └── none ─────────────────────────────────────────► NO COMPILE AT ALL
        │
        ├─► declaration probe                a Compilation over the CHANGED FILES ONLY
        │     (Compilation.Create)            answers "what do these files declare NOW"
        │        ├── nothing at all ──────────────────► record hashes, no compiler runs
        │        ├── a dotnet package ────────────────────────────────────► FULL COMPILE
        │        └── a key an untouched file owns ────────────────────────► FULL COMPILE
        │
        ├─► classify → RadChangeSet          added / modified / removed RadObjectKeys
        │        + one hop of reverse references when a callable surface moved
        │        + every permission set when an entitlement changed
        │
        ├─► ModuleDefinitionOps.WithoutObjects
        │        strip the changed objects from the packaged baseline, or their stale
        │        symbols shadow the new source (AL0126)
        │
        ├─► Compilation.CreateForRad         Microsoft's own RAD factory: bind + generate
        │        │                            C# for the changed objects only, resolving
        │        │                            everything else from the baseline symbols
        │        ├── GetDeclarationDiagnostics() FIRST — a dangling reference is one
        │        │     AL0185, not a throw out of codegen
        │        └── threw / miscounted / merge failed ────────────────────► FULL COMPILE
        │
        └─► RadEmitResult (a candidate, nothing committed yet)
                 │
                 ▼  Roslyn compiles it, the overlay assembly loads
             Commit: register the generation, tombstone removed CLR names, apply the
             buffered runtime metadata, merge the delta back into the module definition
```

`Commit` is the only thing that advances the workspace, and it runs *after* the assembly
loads. An AL diagnostic, a rejected C# candidate or a failed load therefore leaves the last
good baseline exactly where it was, and the next cycle retries the same edit.

## The parts

Everything delta-specific lives in `AlRunner/Rad/`. Nothing outside it changes shape when
delta compilation is off — `AL_RUNNER_RAD=0`, or any mode other than `--watch`, simply never
constructs a workspace.

| Part | What it owns |
|---|---|
| `Rad/RadWorkspace.cs` | The per-app baseline, and `RadWorkspaceStore` — the process-wide map of them |
| `Rad/RadObjectKey.cs` | AL object identity: `(Kind, Id)`, or `(Kind, Name)` for the kinds with no id |
| `Rad/BcCompiler.Rad.cs` | The cycle itself: `EmitIncremental` → `DeltaCompile` / `FullCompile`, and the baseline snapshot + merge |
| `Rad/ModuleDefinitionOps.cs` | Symbol-reference surgery on BC's `ModuleDefinition` (strip objects, count them) |
| `Rad/AlObjectResolution.cs` | Which loaded generation owns each AL CLR type, and which names are tombstoned |
| `Rad/RadMetadataCapture.cs` | Buffers the runtime metadata an AL emit registers, until the generation is committed |
| `Rad/RadBaselineSidecar.cs` | Persists / restores a baseline beside the cached AL output |
| `Rad/RadCycleNotes.cs` | Collects "why this cycle compiled in full", for the dashboard |
| `WatchSource.cs` | Arms the watchers once per process; queues changed paths; quiescence debounce |
| `Program.cs` (watch loop) | Drains the paths, decides warm-reload eligibility, wires cache HIT → hydrate |
| `WatchDashboard.cs` | Renders the yellow full-recompile panel above the test tree |

### `Rad/RadWorkspace.cs` — the baseline, and what may be committed to it

One `RadWorkspace` per app, keyed in `RadWorkspaceStore` by module name + `AppId`, living for
the whole watch process. It holds the SHA-256 of every `.al` file, the objects each file
declares, the reverse one-hop reference graph, the extension→target edges, the compiler's
symbol baseline, the reference signature it was armed under, and the loaded assembly
generations.

Its shape is the load-bearing part. Everything except the loaded assemblies is
`RadWorkspaceUpdate` — the token `Commit` takes and `Snapshot()` hands back — so a map added
to the workspace but not to the token cannot be committed at all. That is what keeps "what a
delta reads", "what a cycle commits" and "what the sidecar persists" from drifting apart.

`ArmFor(signature)` is the gate: a workspace that was armed under a different reference
signature has its baseline dropped, with `DescribeSignatureChange` naming the facet that
moved. `Invalidate(reason)` reports in the same cycle; `PendingFullCompileReason` parks a
reason for a *later* cycle to report, for the case where the compile that discovered the
problem is not the compile that pays for it.

### `Rad/RadObjectKey.cs` — identity, including for the id-less kinds

`(Kind, Id)` for everything BC gives an object id. For `interface`, `controladdin`,
`profile`, `pagecustomization`, `profileextension` and `entitlement` there is no id — those
key on `(Kind, Name)`, with the name decoded (AL escapes a quote by doubling it) and
case-folded (AL identifiers are case-insensitive). The `Name` field is empty for every
id-bearing kind, so adding it did not change any existing key's equality or hash.

Keying an id-less object on its id alone was not a theoretical problem: a `profile` satisfies
BC's `ISymbolWithId` and then reports id 0, so two profiles in one app produced two objects
with one key — which threw out of the baseline snapshot and left the app with no baseline at
all, silently, because that throw is caught and logged.

### `Rad/BcCompiler.Rad.cs` — the cycle

A partial class over `BcCompiler`, so a delta reuses the same reference loader, the same
`.NET` resolver factory and the same compilation options as a full compile — an important
property, because the two must not disagree about what the app is compiled against.

- `EmitIncremental` — hash, arm, diff, then delegate. Public entry point.
- `DeltaCompile` — the probe compilation, the change classification, the reverse-reference
  hop, `CreateForRad`, and the merge back into the module definition. Returns `null` to mean
  "not expressible as a delta", which the caller turns into a full compile.
- `FullCompile` — the ordinary `Emit`, plus `TryBuildBaselineSnapshot` to record a baseline
  from it. A compile whose emit-retry excluded objects deliberately records **no** baseline.
- `MergeRadBaseline` — Microsoft's `WriteSymbolReference` merges the delta into the previous
  `ModuleDefinition` and the result is read back with `SymbolReferenceJsonReader`. From the
  first delta onward the live baseline is itself a JSON-reconstituted definition, which is
  exactly why persisting one to disk is not a new capability.

### `Rad/ModuleDefinitionOps.cs` — stripping the baseline

`WithoutObjects` removes the changed objects from the packaged module definition before
`CreateForRad` binds the new source; left in, the stale symbol shadows the edit and a changed
object calling another changed object fails to bind. Extensions are the exception — what a
`tableextension` contributes is only visible on its target, so stripping one produces AL0132s
against fields declared in its own file.

`CountObjects` exists for the tests: a *stale* copy of an object counts as one just as a
fresh copy does, so a suite that only asserted "it is still there" would pass over a merged
definition holding both.

### `Rad/AlObjectResolution.cs` — which generation answers

.NET cannot unload an assembly, so every cycle leaves the previous generation's
`Codeunit60901` / `Record50000` beside the new one. The runner's type finders resolve an AL
object by scanning `AppDomain.CurrentDomain.GetAssemblies()` and taking the first name match,
biased towards the executing assembly — enough for one app, not enough for a bundle, where
app B's tests are running while the call goes into app A.

Measured before this existed: editing a library app's `Answer()` from 42 to 43 left the test
asserting 42 **green**. So ownership is recorded rather than inferred, and a name a module
used to declare and no longer does is tombstoned, so a deleted object resolves to nothing
instead of resurrecting from the still-loaded previous generation.

### `Rad/RadMetadataCapture.cs` — metadata that moves with the objects

A page, report, xmlport or enum is not only its CLR type: BC resolves it through runtime
metadata the AL emitter writes into process-wide registries as a *side effect* of the emit —
which happens before Roslyn runs and before anything loads. On the delta path those writes
are buffered here and applied only when the generation is committed, so a candidate the C#
backend rejects leaks nothing into the live runtime.

### `Rad/RadBaselineSidecar.cs` — a cache HIT that arrives delta-ready

See [the next section](#the-al-output-cache-serves-the-first-cycle-and-can-serve-it-delta-ready)
— it is the reason a HIT no longer costs a whole-module compile on the first edit.

### `Rad/RadCycleNotes.cs` — why a cycle was slow, where it can be seen

Every fallback already writes a `[watch]` line to stderr, and the interactive dashboard
redirects both streams to `TextWriter.Null` while the bundle loop runs so its painted frame
is not scrolled away — so in the one mode a developer actually watches, every reason was
discarded. Notes are collected process-wide, drained by the watch loop after the streams are
restored, and rendered as a yellow panel above the test tree.

### `WatchSource.cs` and the watch loop

Two things the delta path needs from the loop, beyond a "something changed" bit:

- **The paths.** `PrepareBundleReload` has to know whether *every* change was `.al` source
  under an app with a warm baseline before it keeps metadata warm across the reload. Events
  are queued (`ChangedPaths`) and drained at the top of each cycle, because they arrive on
  threadpool threads while the cycle thread is compiling.
- **Arming once, not per idle wait.** Watchers are armed before the first cycle and stay
  armed for the life of the process, with the signal reset at the top of each cycle. Arming
  them only when a cycle goes idle drops any save landing between "cycle finished" and
  "watchers armed" — and a dropped save is invisible: the developer sees the previous run's
  results and no sign that their edit was ignored.

The debounce is quiescence-based (`WaitForQuiescence`, from #1904): the wait re-arms on every
further event and releases only after `AL_RUNNER_WATCH_QUIET_MS` of silence, capped at
`AL_RUNNER_WATCH_MAX_WAIT_MS`. That matters more with delta compilation than without it,
because a cycle is now short enough that a burst which used to be absorbed by a long compile
would otherwise start one against a half-applied checkout.

## The AL-output cache serves the first cycle, and can serve it delta-ready

The cache answers cycle 1 and nothing after it: starting a watch on an unchanged tree should
cost a load rather than minutes of compiling. Once a generation is loaded the workspace owns
the module — a later cache key that still matches (an `app.json`-only change does not move
it, since the key hashes `.al` sources) must never resurrect the pre-edit DLL over the
running one.

A cached DLL on its own is not enough to delta against, so a HIT used to leave the workspace
with no baseline and the developer's **first edit** paid one whole-module compile to
establish one — 761–862 s on NP Retail, at exactly the moment they are blocked waiting for a
result. So the baseline is cached too, in two artifacts beside the DLL:

```text
<key>.dll               the module
<key>.rad-baseline.json the object map, per-file hashes, one-hop reference graph,
                        extension targets and the reference signature
<key>.rad-symbols.json  the compiler's ModuleDefinition, in BC's own serialized form
```

`AlRunner/Rad/RadBaselineSidecar.cs` writes them and restores them. That the symbol baseline
survives a round trip is not an assumption: the delta path already reads every merged
baseline back through `SymbolReferenceJsonReader` (see `MergeRadBaseline`), and
`RadBaselineSidecarTests` asserts a restored full-compile baseline re-serializes
**byte-identically** to the one the compile produced.

What is persisted is the whole commit token (`RadWorkspace.Snapshot`), not just the module
definition — deliberately the same type `Commit` takes, so a map added to the workspace and
not to `RadWorkspaceUpdate` cannot be committed at all and the persisted set cannot fall
behind the set a delta reads. Three of those maps are absent from the module definition, and
two of the three fail *silently* if dropped: without the reference graph a moved callable
surface rebinds nobody and its callers keep executing the previous contract, and without the
extension targets a renamed enumextension leaves its old registration behind.

**Hydration is validated and fails closed.** The pair is refused — leaving the workspace
exactly as it was, so the cycle behaves as it did before any of this existed — when either
file is missing, the envelope is of another schema or another module, or its per-file content
hashes do not describe the tree now on disk. That last check is the substantive one: a
hydrated baseline that disagreed with the source could bind a delta against symbols for code
that is not there, which is worse than being slow. The refusal is *parked* on the workspace
(`PendingFullCompileReason`), so the full compile it costs names the cause instead of looking
like an unexplained stall.

A fifth guard falls out of existing machinery: hydration restores the **reference signature**
the baseline was built under, so the next cycle's `ArmFor` compares it and reports which facet
moved. That is not belt-and-braces — the cache key hashes the module name, the preprocessor
symbols, the resolved dependency ids and the `.al` contents, but **not** the app version,
publisher or id, so a HIT can legitimately serve a tree whose `app.json` identity has changed.

### One-shot writes it too, so switching modes stays fast

Switching between one-shot and `--watch` over one tree has to feel like one tool, so the baseline
is produced by **whichever of the two compiled** — not only by `--watch`. Both go through
`BcCompiler.TryBuildBaselineSnapshot`, so a baseline written by one is indistinguishable from the
other's:

| Mode | Writes it |
|---|---|
| `--watch`, cycle that compiles in full | from the committed workspace, after the generation loads |
| one-shot | `Program.PersistRadBaseline`, after `Assembly.Load` |
| `--server` | **no** — see the third limit below |

So the ordinary path — run the suite one-shot, then start `--watch` — arrives delta-ready, and
the first edit costs one object. Pinned by
`WatchTests.Watch_AfterAOneShotRun_ServesTheCache_AndDeltasTheFirstEdit`. The reverse direction
(watch, then one-shot) was already a plain cache HIT and is unaffected.

It costs almost nothing to produce. Measured on `tests/runner-extras` (25 compiled apps, BC 28.1,
`BCCOMPILER_TIMING=1`): **105 ms total**, against 17.4 s of compiling and a 47.4 s run — 0.6% of
compile time, 0.2% of wall clock. Nearly all of it is one phase:

| Snapshot phase | 25 apps |
|---|---:|
| reference graph (a semantic model per tree, `GetSymbolInfo` per node) | 102 ms |
| object map | 3 ms |
| file declarations / extension targets / module definition | <1 ms each |

Three limits, all stated because each looks like a bug from outside:

- **A HIT never writes it** — nothing compiled, so there is no compilation to snapshot. In the
  CLI that gate is `cachedBytes == null` and it is load-bearing: one `BcCompiler` serves every
  app group in a bundle, so on a mixed bundle (app A a MISS, app B a HIT) persisting on B's HIT
  would write A's symbol picture under B's key. `PersistRadBaseline` also refuses outright when
  `LastEmittedModuleName` is not this app's, so that mistake is a logged no-op rather than a
  wrong baseline on disk.
- **Restarting a watch on an *edited* tree still misses.** The key hashes `.al` content, so a
  tree that has moved since the entry was written has no entry — and therefore no DLL to hydrate
  beside. Inherent to a content-keyed output cache.
- **`--server` deliberately does not write one**, because it could not be consumed. Server mode
  names its module `V2_<bundle dir>` (`Program.cs`) while the CLI derives it from `app.json`, and
  `ComputeAlCacheKey` hashes `module:<name>` — so the two modes compute **different keys for the
  same tree and never share a cache entry in either direction**. That predates the delta baseline
  and writing one does not fix it: a server-written baseline could only be found by another
  server run, and `--server` has no delta workspace to hydrate it into. Unifying the two names is
  the actual fix, and it is not local to caching — the name feeds the emitted assembly name,
  module identity, the protocol's responses and the phase log — so it belongs in its own change.

Neither artifact is part of `AlCacheSidecars.IsCompleteEntry`, unlike the enum-registry and
query-symbols sidecars. Those carry side effects a HIT cannot function without, so their
absence must force a MISS. These carry an optimisation: gating a HIT on them would turn every
pre-existing entry into a MISS, and would force a cache-schema bump that discards every one —
both to withhold something that is only ever a speedup.

## Cycle behavior

Every watch cycle computes a content hash for every `.al` file in the app's source tree.
This is whole-tree change detection, not an O(changed-files) operation. It avoids a
compile when files were only touched or rewritten with identical bytes.

After hashing, the changed files alone are parsed and a declaration-only compilation over
them answers what they declare NOW. Diffing that against the workspace's object map
classifies the cycle:

| Change | Compile path |
|---|---|
| No content changed | Reuse the loaded module; do not compile |
| An object of any kind was edited, added or removed | Re-emit exactly those objects with `Compilation.CreateForRad` and compile a C# overlay; a removal-only cycle produces no C# at all |
| A modified codeunit's callable surface moved, or an object was removed | Also re-emit the objects that directly reference it — one hop, not the transitive closure |
| A changed object declares a file resource the compiler must read (`AL0327`) | Normal full compile — see below |
| An `entitlement` changed | Delta of it **plus the app's permission sets** — see below |
| A changed file declares a key an untouched file still owns (a duplicate id or name) | Normal full compile, so the compiler reports the duplicate |
| A changed file declares no AL object at all (a new empty file, a comment-only file) | Record the new hash and stop — no compiler runs |
| A changed file declares a `dotnet` package | Normal full compile |
| Dependencies, app identity, version, or preprocessor symbols changed | Normal full compile |
| The delta does not bind (a syntax error, or a reference to something it removed) | No compile at all — report the AL diagnostics and leave the workspace on its last good state |

Modified and removed objects are stripped from the packaged baseline before
`CreateForRad` binds the new source, and Microsoft's own `WriteSymbolReference` merges the
result back into the previous module definition to produce the next baseline.

Binding errors are collected from `GetDeclarationDiagnostics()` **before** code generation
runs. BC's RAD emitter does not survive a dangling reference — it throws out of codegen
with `Unexpected value 'None' of type NavTypeKind` rather than reporting the `AL0185` its
own declaration pass already found — so asking first is what keeps "you deleted something
that is still used" a one-line diagnostic naming the missing object instead of a
whole-module rebuild whose emit-retry then drops the caller from the module.

If the runner cannot classify or emit an eligible delta safely — a RAD emit that throws, a
callback count that disagrees with the change model, a symbol merge that fails — it falls
back to a normal full compile. An AL or generated-C# error never advances the last good
baseline: the emit prepares a commit token that is applied only after the overlay assembly
loads.

## Bulk changes

A branch switch, rebase, bulk rename or formatter run rewrites, adds and deletes dozens to
thousands of `.al` files in one command. Whatever the cycle sees as changed is deltaed as one
change model: `AlRunner.Tests/Fixtures/RadBulkSwitch` pins a whole-version switch (8 modified,
2 added, 2 deleted) re-emitting exactly those twelve objects in both directions and leaving the
workspace settled — see `RadBulkSwitchDeltaTests`.

The *arrival* of such a change is handled by the watch loop rather than the delta path: one
command takes seconds, so the tree spends that whole window in a mixed state, and a debounce
that fires a fixed interval after the first file event starts a cycle mid-checkout — a
correct delta of an incorrect tree. `WatchSource.WaitForQuiescence` ([#1904], on `main`)
releases only after the tree has been quiet for `AL_RUNNER_WATCH_QUIET_MS` (default 250ms),
capped at `AL_RUNNER_WATCH_MAX_WAIT_MS` (default 10s) from the first event.

A watcher-buffer overflow (`FileSystemWatcher.Error`) is the residual case, and it is safe
here for a structural reason: the change model is computed by **re-hashing the whole tree**,
never from the event list. Dropped events can therefore only delay a cycle, not corrupt one.
The queued paths are used for one narrower decision — whether warm metadata may survive the
reload — and the app-identity half of that is re-checked independently by `ArmFor`.

[#1904]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/1904

## What the baseline contains

`AlRunner/Rad/RadWorkspace.cs` keeps, per app for the lifetime of the watch process:

- the SHA-256 hash of every `.al` file;
- the objects declared by each file;
- the compiler's full-emit symbol baseline (accepted overlays preserve that surface);
- the loaded baseline and overlay assembly generations.

plus the reverse one-hop reference graph and the extension→target edges, both read off
Microsoft's bound semantic models during a full compile.

Everything on that list except the loaded assemblies is `RadWorkspaceUpdate` — the token
`Commit` takes — and `RadWorkspace.Snapshot()` hands it back. That symmetry is what
`RadBaselineSidecar` persists, and it is deliberate: a map added to the workspace but not to
`RadWorkspaceUpdate` cannot be committed at all, so "what is persisted" cannot silently fall
behind "what a delta reads".

## Runtime metadata moves with the objects

A page, report, xmlport or enum is not only its generated CLR type: BC resolves it through
runtime metadata the AL emitter writes into process-wide registries as a side effect of
`Compilation.Emit`. Those writes happen before Roslyn compiles the generated C# and before
the assembly loads, so on the delta path they are buffered in
`AlRunner/Rad/RadMetadataCapture.cs` and applied only when the generation is committed. At
that point the cycle also drops the previous identity of every modified and removed
object, using the object map as it stood before the commit — which is what lets a renamed
enumextension or a deleted report leave nothing behind. A candidate the C# backend rejects
applies nothing at all.

A **full** compile writes through immediately instead, because the AL-output cache sidecar
is serialized straight off these registries while the compile is still in progress. It gets
the identity cleanup at commit rather than the buffering: an object the source tree no
longer declares has its metadata dropped, and so does the stale `(base enum, name)` pair of
an enumextension that was renamed or retargeted — the one registration that is not keyed by
its own object id, so a re-emit adds to it instead of replacing it. The residue that leaves
is narrow and self-healing: a full compile whose generated C# is then rejected has already
refreshed the entries of objects that still exist, and the next successful cycle overwrites
them again.

Warm reloads keep the registries only while every app in a single-bundle watch has a
committed baseline and every changed path is a `.al` file under one of them
(`RadWorkspaceStore.PrepareBundleReload`). Anything else — a manifest change, several
bundles — takes the clean-slate path and refreshes all of it.

Generated C# for the changed objects is compiled into a small, uniquely named generation
assembly and loaded beside the current module. `AlRunner/Rad/AlObjectResolution.cs` records
which generation owns each AL object type and tombstones the names a committed deletion
removed, so a still-loaded previous generation cannot answer for a deleted object. A later
full compile replaces the whole generation chain and establishes a fresh baseline.

## Measured on NP Retail

Measured on NP Retail (7,053 AL files / 6,949 objects) and its test app (286 files / 286
objects), watched together as ONE bundle on BC 28.1, 6-core / 12 GB, one test codeunit
selected. Cold first cycle: **~1,100 s**, of which 649 s is the Application's AL emit. The
cold cycle is not what `--watch` optimises and is unchanged by any of this.

A successful warm cycle reports the changed-object delta and overlay explicitly:

```text
[watch] NP Retail: delta +0 ~1 -0 over 1 changed file(s) → 1 object(s) re-emitted (1063ms)
[watch] NP Retail: overlay NP Retail#rad…g2 — 1 object(s), 28KB (536ms)
[watch] NP Retail Tests: unchanged — reusing the loaded module
```

One edit costs one object, for every object kind and every file operation — with two named
exceptions, both below the table: a change to a **callable or binding surface** also rebinds its
direct users (one hop, not the transitive closure), and an **entitlement** edit also binds the
app's permission sets, because BC will not let one resolve a permission set from the packaged
baseline.

| Edit | Delta | AL emit | Overlay | Cycle |
|---|---|---:|---:|---:|
| Codeunit body | `+0 ~1 -0` → 1 | 1.4 s | 0.21 s | 42 s |
| Codeunit callable surface | `+0 ~1 -0` → 1 | 1.5 s | 0.21 s | 39 s |
| Table field added | `+0 ~1 -0` → 1 | 1.2 s | 0.13 s | 41 s |
| Tableextension field added | `+0 ~1 -0` → 1 | 1.3 s | 0.19 s | 43 s |
| Page control added | `+0 ~1 -0` → 1 | 1.5 s | 0.12 s | 39 s |
| Pageextension | `+0 ~1 -0` → 1 | 1.3 s | 0.13 s | 42 s |
| Enum value added | `+0 ~1 -0` → 1 | 1.3 s | 0.15 s | 43 s |
| Enumextension value added | `+0 ~1 -0` → 1 | 2.2 s | 0.23 s | 44 s |
| Report / xmlport / query | `+0 ~1 -0` → 1 | 1.2 s | 0.13 s | 42–49 s |
| Test-app codeunit body | `+0 ~1 -0` → 1 | 0.9 s | 0.13 s | 59 s |
| New file added | `+1 ~0 -0` → 1 | 1.1 s | 0.22 s | 44 s |
| File renamed (same object) | `+0 ~1 -0` → 1 | 1.1 s | 0.20 s | 43 s |
| File deleted | `+0 ~0 -1` → 0 | 1.1 s | — | 41 s |
| Touched, identical bytes | no compile at all | — | — | 41 s |
| Two files in one app | `+0 ~2 -0` → 2 | 1.1 s | 0.25 s | 43 s |
| One file in each app | 1 + 1 | 1.2 + 0.9 s | 0.19 + 0.13 s | 42 s |
| Id-less object (`interface`, `controladdin`, `profile`, `pagecustomization`, `profileextension`, `entitlement`) | `+0 ~1 -0` → 0 | — | — | see below |

The id-less row used to read **full compile, 761–862 s**. `RadObjectKey` was `(Kind, Id)`,
and `interface`, `controladdin` and `profile` have no id, so they could not be told apart —
on NP Retail that was 84 of 7,339 files (60 interface, 16 controladdin, 8 profile), each a
guaranteed whole-module rebuild on any edit including a comment. They are now keyed by name
and delta like anything else. That row's timing has NOT been re-measured on NP Retail; the
behaviour is pinned by `RadIdlessObjectTests` against a fixture instead, and a delta that
compiles nothing has no plausible way to cost minutes.

`pagecustomization`, `profileextension` and `entitlement` joined them. The first two are
returned by `GetDeclaredApplicationObjectSymbols()` and then report **id 0** — they satisfy
every "is this an application object?" test and have no id to be told apart by, which is why
keying on the id alone left them unkeyable. An `entitlement` is not returned at all, so its
declaration is read off the syntax tree like an `interface`'s.

The claim that they had no representation to strip was only true of one of them:
`ModuleDefinition` does carry `PageCustomizations` and `ProfileExtensions` (and
`ProfileExtensionDefinition` carries `Name` + `TargetObject`), so both are stripped from the
packaged baseline like any other modified object. `Entitlements` is the one that genuinely
does not exist — there is no `EntitlementDefinition` type in the compiler — which cuts the
other way: an entitlement has no serialized copy that could shadow an edit, and nothing
downstream can resolve one. The map in `ModuleDefinitionOps` previously claimed an
`Entitlements` property; it cost nothing only because `GetProperty` returned null and the
loop skipped it in silence.

An id-less object is a binding contract as much as a codeunit is, so its users are rebound
when its surface moves: the reverse dependency graph records edges onto it even though it is
not an application object and `ContainingApplicationObject` never returns one. Without that
edge, widening an interface without touching its implementer reported success, emitted
nothing, and left the implementer bound to the previous contract. Two more places where the
name has to be the identity end to end: a removed one is stripped from the previous module
by name before Microsoft's symbol writer merges (the writer matches the change element, and
a serialized id-less element carries a synthesized id that source cannot reproduce), and key
names are decoded and case-folded, because AL escapes a quote by doubling it and its
identifiers are case-insensitive.

A **modified** id-less object needs no such pre-strip, and that was measured rather than
assumed: the writer merges one into exactly one copy carrying the post-edit shape. The
asymmetry with removals is coherent — for a modification the delta module supplies a
replacement, whereas for a removal only the change-element match can drop the old copy, and
that is the match the synthesized id defeats. `ModuleDefinitionOps.CountObjects` exists so
the suites can assert it: one *stale* copy also counts as one, so the tests check the count
and the shape. A second copy would not fail any compile — which of the two a later lookup
answers with is decided by array order — so a settled next cycle can be green over the
pre-edit definition.

#### An entitlement is compiled with the app's permission sets

`ObjectEntitlements` may only name permission sets declared in the **same module**, and BC
does not accept one resolved from the packaged baseline: the delta fails with **AL0683**
("belongs to a different module and cannot be used when defining entitlements") on a tree
that compiles clean cold. With the permission set's own file in the same delta it binds
without a diagnostic — so a cycle touching an entitlement pulls in every file declaring a
permission set. All of them, because which ones it names is only recoverable by parsing the
property, and an app has a handful of permission sets against approximately never editing an
entitlement.

The reverse edge needs the same treatment for the opposite reason. An entitlement produces no
compiler symbol, so no semantic model ever reports that it names a permission set, and
renaming one left every entitlement naming it pointing at something gone — delta green, cold
build **AL0185**. So a cycle that renames or removes a permission set rebinds every
entitlement. Only the *name* triggers it: a permission set has a real object id, so a rename
keeps its key and arrives as a modification rather than a removal plus an addition, and
changing `Assignable` or a permission line cannot break a reference by name. Rename and deletion
are pinned separately because they arrive on different code paths — `modified` and `removed`.
All of it is pinned by `RadIdlessObjectTests` against a cold compile of the same tree, which is
the only oracle available for an object the module definition does not represent.

#### A file that declares nothing costs nothing

Creating an empty `.al` file, editing a comment-only one or deleting it again used to rebuild
the whole module — for a file that contributes not one line of generated code. On NP Retail a
whole-module rebuild is the 761–862 s the id-less row above cites. It no longer compiles
anything at all: the cycle records the new hashes and returns, keeping every loaded type. Not
re-measured on NP Retail — the behaviour is pinned against the fixture instead, and a cycle
that invokes no compiler has no plausible way to cost minutes.

What made that unsafe was reading "declares nothing" as the *absence* of a symbol, which is
equally what an unidentifiable declaration looks like. It is now read positively, off BC's own
parser: `ObjectSyntax` is the base type of every AL top-level declaration — the id-bearing
kinds through `ApplicationObjectSyntax`, and `profile`, `interface`, `controladdin`,
`entitlement` and `dotnet` directly — so a tree with no `ObjectSyntax` node declares nothing,
full stop. A declaration of a shape the delta does **not** recognise is therefore visible as
such rather than indistinguishable from an empty file, and takes the full-compile path naming
its syntax kind; that is what keeps an AL kind some future compiler adds from being silently
skipped.

The other half is a per-file record of what a compile could *not* account for
(`RadFileDeclarations`), because "the workspace has no objects for this path" is also what a key
collision looks like. Two flags, both carried on the workspace across cycles:

- **`Unrecorded`** — the file declared more than the compile could record for it. No valid AL
  produces this today (a duplicate id or name is a compile error, so a compile clean enough to
  become a baseline cannot contain one); it is the guard that lets `MapObjectsToFiles` keep
  dropping what it cannot key without that silently becoming "this file declares nothing".
- **`DotNetPackage`** — see below.

Pinned by `RadObjectDeltaTests.AFileThatDeclaresNoObject_CostsNoCompilerWork_WhenAddedEditedOrDeleted`
(add, edit and delete, each asserting zero emitted sources and twenty untouched runtime types)
and the two guard tests beside it.

#### What still forces a full compile

Every AL **object** kind is keyable. Two cases remain:

- **A `dotnet` package declaration.** Not an AL object: it changes what every object in the
  module can bind to, and a RAD object compilation carries no package declaration trees —
  `MergeRadBaseline` deliberately restores the previously committed `DotNetPackages`. This used
  to fall out of the empty-file rule; now that such files delta, it is a rule of its own, and it
  fires in both directions. Declaring one is read off the changed file's syntax; **deleting**
  one can only come from the workspace's per-file record, since there is no file left to parse.
  Pinned both ways by `RadObjectDeltaTests.AFileDeclaringADotNetPackage_StillForcesAFullCompile`.
- **A duplicate declaration** — a changed file claiming a key an untouched file still owns.
  `ws.Declares` only answers "does the module declare this key", which is true either way, so
  the new object used to be classified as a *modification* of the other file's object and the
  cycle reported success on a tree that does not compile. Measured against a cold compile: a
  duplicated `interface` name is four AL0197s cold and **no diagnostic at all** through the
  delta; a duplicated codeunit id is four AL0264s cold and one unrelated AL0185. Only the
  compiler can say what a duplicate means, so it gets the whole module.

Adding a future kind is a line in `RadObjectKey.IsIdlessKind` (plus `IdlessKindOf` if the
symbol API omits it, plus a `ModuleDefinitionOps` array entry if one exists) and a fixture
that declares one — a kind counts as supported when a test proves the round trip, not when
it has a key.

#### A file resource is a question only the full compile can answer

BC resolves a `controladdin`'s `Scripts` / `StartupScript` / `StyleSheets` / `Images` through
an `IFileSystem` attached to the compilation, anchored at the app root (#1899/#1912). A RAD
compilation cannot have one: `Compilation.CreateForRad` takes no file-system parameter, and
attaching one afterwards with `WithFileSystem` returns a compilation that has **lost its
packaged module definition** — measured, everything outside the delta stops resolving, e.g.
`AL0247` for the target table of an untouched `tableextension`.

So the delta cannot tell a resource that is present from one that is missing, and an
`AL0327` from a RAD compilation is not evidence of anything. It is treated as "not
expressible as a delta" and the cycle compiles the module in full, which resolves the path
or reports a genuine typo. Both directions are pinned by
`RadObjectDeltaTests.AControlAddInResourcePath_IsAnsweredByTheFullCompile_NotSilencedAndNotFailed`
— a fix that merely suppressed AL0327 would hide every real one.

The same asymmetry has a second consequence, in the surface fingerprint. A full compile with
a file system records a `ReferenceSourceFileName` on every symbol it writes; a RAD-emitted
one comes back with it null. `ObjectSurfaceFingerprint` therefore strips it before comparing
— otherwise **every** re-emitted object reads as "its surface moved" and pulls in its direct
callers, whose fingerprints then differ for the same reason. Measured on the 20-object
fixture: a one-line body edit went from 1 re-emitted object to 3, over two rounds of
rebinding. Where a symbol was read from is not part of any binding contract; what it offers
is. Pinned in both directions by
`ABodyEdit_StaysOneObject_WhenTheCompileRecordsSourceFileNames` and
`ACallableSurfaceEdit_StillRebindsItsCaller_WhenTheCompileRecordsSourceFileNames`.

#### Every full compile says why, where the developer is looking

A whole-module rebuild in a warm watch loop is the difference between a one-second cycle and a
several-minute one, and an unexplained one is indistinguishable from the delta path being
broken. The commonest cause is not a bug at all: **switching a git branch usually switches
`app.json` with it**, and a different app version, id, publisher or dependency list invalidates
every cached object by definition.

So each decision reports a cause a developer recognises rather than a category:

```
[watch] NP Retail: full rebuild — app.json changed the app version: 1.0.0.0 → 1.0.1.0
[watch] NP Retail: full rebuild — the resolved dependency set changed (12 → 13) — app.json
      dependencies, or the .app files in .alpackages
[watch] NP Retail: full compile — POSPackages.al declares a dotnet package, which every object
      in the module binds against
[watch] NP Retail: full compile — Codeunit 'POS Sale' is also declared by POSSaleOld.Codeunit.al,
      which this cycle did not touch — only the compiler can say which of the two is the duplicate
[watch] NP Retail: full rebuild — app.json changed, which is not AL source this bundle's apps own
```

The reference-signature reason is derived by diffing the previous signature against the new one
facet by facet (`ReferenceSignature` writes `<facet>|<value>` lines for exactly this;
`DescribeSignatureChange` reads them), so it names what moved instead of listing everything that
could have.

**These lines are also collected, not only logged.** The interactive dashboard redirects both
console streams to `TextWriter.Null` for the duration of the bundle loop so its painted frame is
not scrolled away — which meant that in the one mode a developer actually watches, every reason
was discarded. `Rad/RadCycleNotes.cs` is a process-wide collector the watch loop drains after the
loop restores the streams, and the dashboard renders a yellow **full recompile** panel above the
test tree. Yellow, not red: the results below it are correct, the cycle was only slow. On a
plain delta cycle the panel is absent entirely, so "no full recompile happened" is readable
without parsing anything.

One reason is reported by a *later* cycle than the one that discovers it. A compile that cannot
record a baseline makes every subsequent cycle a full compile, and those later cycles know
nothing — so they rebuilt in silence while the reason had scrolled past one cycle before the
slowdown anyone noticed. `RadWorkspace.PendingFullCompileReason` parks it, and the compile that
acts on it consumes it. Exactly once, and a committed baseline retires it, so it cannot end up
attached to some unrelated later rebuild. The invalidation paths deliberately do NOT park —
`Invalidate` reports in the same cycle, and doing both would say it twice.

### Where a warm cycle's time actually goes

The delta is not the cost. `BCCOMPILER_TIMING=1` instruments the bundle-level phases as well
as the per-app ones — before that, a measured cycle only accounted for about half of itself,
and the unattributed remainder was guessed at. Steady state (cycles 2–3 after the baseline; a
one-procedure edit to one codeunit, which moves five objects because four files call it):

| Phase | Time |
|---|---:|
| Running the selected test codeunit (30 tests) | 11.0 s |
| `RecordPatches.AddSourceDir` — re-reads and re-parses every `.al` file in both apps | 7.9 s* |
| **AL delta emit (`CreateForRad`)** | **2.4 s** |
| `NP Retail Tests` establishing it has nothing to do | 2.9 s |
| Register + publish symbols | 2.5 s |
| Roslyn compile + assembly load of the overlay | ~1.4 s |
| `GetSharedReferences` (warm) | 1.0 s |
| Resolve + load dependencies | 1.0 s |
| Whole-tree hashing (7,053 files) | 0.14 s |
| Field-trigger wiring and record prewarm | 0.20 s |
| C# overlay compile | 0.15 s |

\* **The `AddSourceDir` row, and therefore the cycle total, is measured with a change this
branch does not carry.** That phase builds one AL syntax tree **per extractor** rather than per
file, so npcore's 7,339 files cost ~59,000 AL parses per cycle instead of 7,339. A direct probe
of the phase on the same corpus measured **29.7 s** at eight parses per file against
**7.0–7.9 s** at one; the table above was taken with the latter. Deduplicating the parse is
tracked separately as
[#1903](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/1903), so as this
branch stands the row is ~29.7 s and the cycle is roughly 22 s longer than the rows sum to.
That composite has not been re-measured in this configuration.

`AddSourceDir` is the largest overhead in the cycle either way, and it is O(whole tree) by
construction: the reload clears every parsed dictionary, so all of it is rebuilt to service an
edit to one file. Making it O(changed files) needs file→parsed-entry provenance, which does not
exist in `RecordPatches` today — the tableextension dictionaries are keyed by base-table name
and accumulate, so one file's contribution cannot currently be retracted.

Two corrections to what this section used to claim, both from the new instrumentation:
post-registration field-trigger wiring and record prewarm are **0.2 s**, not ~12 s; and
`GetSharedReferences` is cheap only when the bundle is configured as two source apps —
leave a precompiled `NP Retail.app` in `Test/.alpackages` and it re-reads 136 MB of
packages per call, 8–15 s per app per cycle.

### The developer loop it is meant to serve

The same corpus, driven through a scripted session that alternates application and test
edits with one test codeunit selected — the AL results are what prove each cycle ran the
new code, not just that a cycle happened:

| Step | Result | Cycle |
|---|---|---:|
| Add a local helper to an application codeunit | 30P/0F | 44 s |
| Change a label the test asserts on | **28P/2F** — the edited application code really ran | 41 s |
| Update the test's expectation to match | 30P/0F | 45 s |
| Add another helper | 30P/0F | 48 s |
| Add a public procedure (callable-surface change) | 30P/0F | 46 s |
| Add a test method calling it | **31P/0F** — the new test ran | 44 s |
| Edit two application codeunits at once | 31P/0F | 45 s |

## What the tests pin

Timings drift with the machine, so the executable contract is stated as identities and
counts instead. The first four suites share one real 20-object app,
`AlRunner.Tests/Fixtures/RadTwentyObject`, so "all of it" and "the one that changed" are
different numbers:

| Suite | Claim |
|---|---|
| `RadObjectDeltaTests` | One edit → exactly which objects re-emit and which CLR types change owner, for ten object kinds plus schema additions, a rename, a callable-surface change, and a rejected or abandoned candidate |
| `RadDeletionDeltaTests` | One deletion → zero objects re-emitted, exactly those object identities removed, exactly those CLR names tombstoned, every survivor still the identical baseline `Type` |
| `RadMetadataDeltaTests` | One edit → exactly one page/report/xmlport/enum metadata entry moves; a deletion drops its entry, on the delta path and on the full-compile fallback; a renamed enumextension leaves one registration; a rejected candidate leaves none behind |
| `RadWatchTwentyObjectTests` | The same claims against the real `--watch` process, via its own `[watch]` log lines, with the AL test outcome proving the new code actually ran |
| `RadIdlessObjectTests` | The six kinds with no object id: two `profile`s (and two `entitlement`s) are two objects rather than one colliding key; the kinds the symbol API never reports and the kinds it reports with id 0 are both tracked to their file; editing or deleting one is a delta that compiles no C#; narrowing an interface binds against the new contract rather than the baseline's copy; widening one WITHOUT touching its implementer still rebinds it, and so does renaming a `pagecustomization` without touching the `profileextension` that names it; a modification leaves the merged baseline holding exactly ONE copy, carrying the post-edit shape; a deletion leaves it entirely; identity survives an embedded quote and a case-only rename; an `entitlement` — which the module definition cannot represent at all — accepts and rejects exactly what a cold compile of the same tree does, in both directions of its permission-set relationship; and a changed file claiming a key an untouched file still owns does not pass as a modification |
| `WatchTests` | Cycle 1 of a watch is served from the AL-output cache, and the first edit really runs (never a second HIT): delta'd when the entry carries a baseline — after a one-shot run, and after an earlier watch — and building one when it does not |
| `RadBaselineSidecarTests` | A persisted baseline restores the compiler's symbol picture **byte-identically**, and a workspace hydrated from it deltas the first edit, rebinds the direct caller of a moved surface, classifies a deletion as a removal, and still rebuilds for a deleted `dotnet` package — plus the five ways hydration must fail closed (tree moved, file edited in place, symbols missing, unknown schema, app identity changed) |
| `RadBulkSwitchDeltaTests` | A whole-version switch (8 modified + 2 added + 2 deleted, in one cycle) re-emits exactly those twelve and no more, in both directions, leaving the workspace settled |
| `WatchDashboardTests` | …and, for the reason-reporting above: a recorded full-recompile reason reaches the dashboard verbatim with the app it belongs to, and the panel is absent on a delta cycle |

## Reloaded dependency tableextensions

Watch reloads preserve the symbol paths for precompiled dependency apps and re-merge their
`tableextension` fields when per-cycle record metadata is rebuilt. A second or later cycle
can therefore resolve fields supplied by a warm precompiled dependency; the former
`extension field ... not found in NCLMetaTable` reload regression is covered by
`RadDeltaWatchTests`.

## Control

There is no switch to turn this on: `--watch` is delta compilation. Eligibility and
fallback are automatic, and a change the delta path cannot classify is never forced through
an overlay — it becomes a full compile.

`AL_RUNNER_RAD=0` forces every watch cycle through a whole-module compile. It exists to
bisect a suspected delta bug ("does it still happen without the overlay?"), not as a
supported mode.
