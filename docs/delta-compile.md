# Delta compilation (`--watch`)

Delta compilation is what `--watch` does. With a cold output cache, the first watch cycle
performs a normal full compile and records a baseline for each source app. Subsequent cycles
use those baselines when an edit is safe to load as a small overlay. Other runner modes use
the normal compile path.

`--watch` behaviour that concerns *runtime* state rather than compilation — what a reload
drops and what survives it — is documented in
[`docs/server-mode.md`](server-mode.md), under "The reload contract", because the server and
the watch loop share that machinery. In particular, "Forgetting a cached BC object does not
end its life" is a `--watch` finding written under a `--server` heading.

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
        │        + one hop of reverse references when a codeunit's or id-less object's
        │          surface moved
        │        + every codeunit whose TableNo names a table this cycle strips
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
        │        ├── any AL error ──► widen once by the stripped-surface rules, RETRY
        │        │     (extensions of a stripped target · users of a stripped interface ·
        │        │      TableNo on a stripped table · dataitem on a stripped table)
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
| `Rad/ModuleDefinitionOps.cs` | Symbol-reference surgery on BC's `ModuleDefinition` (strip objects, count them, fingerprint a surface, find `TableNo` bystanders) |
| `Rad/AlObjectResolution.cs` | Which loaded generation owns each AL CLR type, and which names are tombstoned |
| `Rad/RadMetadataCapture.cs` | Buffers the runtime metadata an AL emit registers, until the generation is committed |
| `Rad/RadBaselineSidecar.cs` | Persists / restores a baseline beside the cached AL output |
| `Rad/RadCycleNotes.cs` | Collects "why this cycle compiled in full", for the dashboard |
| `WatchSource.cs` | Arms the watchers once per process; queues changed paths; quiescence debounce |
| `Program.cs` (watch loop) | Drains the paths, decides warm-reload eligibility, wires cache HIT → hydrate |
| `WatchDashboard.cs` | Renders the yellow full-recompile panel above the test tree |

### `Rad/RadWorkspace.cs` — the baseline, and what may be committed to it

One `RadWorkspace` per app, keyed in `RadWorkspaceStore` by module name + `AppId`, living for
the whole watch process. See [the baseline](#the-baseline) for what it holds and how it is
persisted.

`ArmFor(signature)` is the gate: a workspace that was armed under a different reference
signature has its baseline dropped, with `DescribeSignatureChange` naming the facet that
moved. `Invalidate(reason)` reports in the same cycle; `PendingFullCompileReason` parks a
reason for a *later* cycle to report, for the case where the compile that discovered the
problem is not the compile that pays for it.

`FileOf(key)` — which file declares an object — is served from an index rebuilt at the end of
`Commit` and cleared in `Invalidate`. It is derived, not committed, so neither
`RadWorkspaceUpdate` nor the sidecar carries anything extra and it cannot drift. That matters
because every caller asks in bulk: once per declared object in the added-vs-modified
classifier, once per widened caller, and twice over, since a widened cycle recurses. Measured
direct-user fan-in on NP Retail, in files: **p50 2, p90 10, p99 59, max 435**
(`POSSale.Codeunit.al`); 28 objects exceed 100 files and none exceeds 500. A linear scan of
the object map per key does not survive that. Guarded by `RadWorkspaceFileOfTests`.

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
- `DeltaCompile` — the probe compilation, the change classification, the rebind hops,
  `CreateForRad`, and the merge back into the module definition. Returns `null` to mean
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
against fields declared in its own file. The exemption is a test on the **kind name**
(`IsExtension` is `Kind.EndsWith("Extension")`), which is why `pagecustomization` is stripped
and `profileextension` is not.

`CountObjects` exists for the tests: a *stale* copy of an object counts as one just as a
fresh copy does, so a suite that only asserted "it is still there" would pass over a merged
definition holding both.

`ObjectSurfaceFingerprint` and `CodeunitsWithTableNo` are the two rebind inputs; both are
described under [scope and edge cases](#scope-and-edge-cases).

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

This is *object-type* resolution. The member-level half of the same problem — a cross-app
caller still dispatching a member id the callee no longer has — is a different mechanism
entirely, and lives in [the reference graph](#a-sibling-app-whose-callee-moved-rebinds-too).

### `Rad/RadMetadataCapture.cs` — metadata that moves with the objects

A page, report, xmlport or enum is not only its CLR type: BC resolves it through runtime
metadata the AL emitter writes into process-wide registries as a *side effect* of
`Compilation.Emit` — which happens before Roslyn runs and before anything loads. On the delta
path those writes are buffered here and applied only when the generation is committed, so a
candidate the C# backend rejects leaks nothing into the live runtime.

At commit the cycle also drops the previous identity of every modified and removed object,
using the object map as it stood before the commit — which is what lets a renamed
`enumextension` or a deleted report leave nothing behind.

A **full** compile writes through immediately instead, because the AL-output cache sidecar is
serialized straight off these registries while the compile is still in progress. It gets the
identity cleanup at commit rather than the buffering: an object the source tree no longer
declares has its metadata dropped, and so does the stale `(base enum, name)` pair of an
`enumextension` that was renamed or retargeted — the one registration that is not keyed by its
own object id, so a re-emit adds to it instead of replacing it. The residue that leaves is
narrow and self-healing: a full compile whose generated C# is then rejected has already
refreshed the entries of objects that still exist, and the next successful cycle overwrites
them again.

Warm reloads keep the registries only while every app in a single-bundle watch has a committed
baseline and every changed path is a `.al` file under one of them
(`RadWorkspaceStore.PrepareBundleReload`). Anything else — a manifest change, several bundles —
takes the clean-slate path and refreshes all of it.

### `Rad/RadBaselineSidecar.cs` — a cache HIT that arrives delta-ready

See [the baseline](#the-baseline). It is the reason a HIT costs a load rather than a
whole-module compile on the first edit.

### `Rad/RadCycleNotes.cs` — why a cycle was slow, where it can be seen

Every fallback writes a `[watch]` line to stderr, and the interactive dashboard redirects both
streams to `TextWriter.Null` while the bundle loop runs so its painted frame is not scrolled
away — so in the one mode a developer actually watches, a stderr-only reason is discarded.
Notes are collected process-wide, drained by the watch loop after the streams are restored,
and rendered as a yellow panel above the test tree. `FullCompileBecause` writes both, always.

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
because a cycle is now short enough that a burst which would otherwise be absorbed by a long
compile can start one against a half-applied checkout.

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
| A modified **codeunit's** or **id-less object's** serialized surface moved, or an object was removed | Also re-emit the objects that directly reference it — one hop, not the transitive closure |
| A table this cycle strips is named by an untouched codeunit's `TableNo` | Also re-emit that codeunit, under the table's new *and* previous name — see below |
| A changed object declares a file resource the compiler must read (`AL0327`) | Normal full compile — see below |
| An `entitlement` changed | Delta of it **plus the app's permission sets** — see below |
| A changed file declares a key an untouched file still owns (a duplicate id or name) | Normal full compile, so the compiler reports the duplicate |
| A changed file declares no AL object at all (a new empty file, a comment-only file) | Record the new hash and stop — no compiler runs |
| A changed file declares a `dotnet` package | Normal full compile |
| Dependencies, app identity, version, or preprocessor symbols changed | Normal full compile |
| The delta does not bind (a syntax error, or a reference to something it removed) | No compile at all — report the AL diagnostics and leave the workspace on its last good state |

A modified **table, page, enum, report or query** does not widen the cycle on a surface move:
the `changedSurfaces` filter admits only codeunits and the id-less kinds. It can still widen
for a different reason — stripping such an object takes surface off untouched bystanders, and
that is repaired by
[a widened retry](#a-delta-damages-surface-a-bystander-holds-only-because-the-stripped-object-exists)
driven by the diagnostic it causes.

Modified and removed objects are stripped from the packaged baseline before
`CreateForRad` binds the new source, and Microsoft's own `WriteSymbolReference` merges the
result back into the previous module definition to produce the next baseline.

Binding errors are collected from `GetDeclarationDiagnostics()` **before** code generation
runs. BC's RAD emitter does not survive a dangling reference — it throws out of codegen
with `Unexpected value 'None' of type NavTypeKind` rather than reporting the `AL0185` its
own declaration pass already found — so asking first is what keeps "you deleted something
that is still used" a one-line diagnostic naming the missing object instead of a
whole-module rebuild whose emit-retry then drops the caller from the module.

That ordering has a consequence worth carrying: everything the declaration pass does *not*
catch — every method-body diagnostic — reaches the runner only through `rad.Emit(...)`. Which
of the two an AL id comes from is **not** guessable: six of the eight by-name breaks below are
method-body diagnostics, and the two raised against a layout `modify(...)` and a dataset
`add(...)` come from the declaration pass. Anything that acts on "the delta reported an error"
has to hook both.

If the runner cannot classify or emit an eligible delta safely — a RAD emit that throws, a
callback count that disagrees with the change model, a symbol merge that fails — it falls
back to a normal full compile. An AL or generated-C# error never advances the last good
baseline: the emit prepares a commit token that is applied only after the overlay assembly
loads.

## The baseline

`RadWorkspace` keeps, per app for the lifetime of the watch process:

- the SHA-256 hash of every `.al` file;
- the objects declared by each file, and the per-file record of what a compile could not
  account for (`RadFileDeclarations`);
- the reverse one-hop reference graph and the extension→target edges, both read off
  Microsoft's bound semantic models during a full compile;
- the compiler's full-emit symbol baseline (accepted overlays preserve that surface);
- the reference signature it was armed under;
- the loaded baseline and overlay assembly generations.

Everything on that list except the loaded assemblies is `RadWorkspaceUpdate` — the token
`Commit` takes, and what `RadWorkspace.Snapshot()` hands back. That symmetry is the
load-bearing part: a map added to the workspace but not to the token cannot be committed at
all, so "what a delta reads", "what a cycle commits" and "what the sidecar persists" cannot
drift apart.

### The AL-output cache serves the first cycle, and serves it delta-ready

The cache answers cycle 1 and nothing after it: starting a watch on an unchanged tree should
cost a load rather than minutes of compiling. Once a generation is loaded the workspace owns
the module — a later cache key that still matches (an `app.json`-only change does not move
it, since the key hashes `.al` sources) must never resurrect the pre-edit DLL over the
running one.

A cached DLL on its own is not enough to delta against, so the baseline is cached too, in two
artifacts beside the DLL:

```text
<key>.dll               the module
<key>.rad-baseline.json the object map, per-file hashes, one-hop reference graph,
                        extension targets and the reference signature
<key>.rad-symbols.json  the compiler's ModuleDefinition, in BC's own serialized form
```

Without them a HIT left the workspace with no baseline, and the developer's **first edit**
paid one whole-module compile to establish one — 761–862 s on NP Retail, at exactly the moment
they are blocked waiting for a result.

`AlRunner/Rad/RadBaselineSidecar.cs` writes them and restores them. That the symbol baseline
survives a round trip is not an assumption: the delta path already reads every merged
baseline back through `SymbolReferenceJsonReader` (see `MergeRadBaseline`), and
`RadBaselineSidecarTests` asserts a restored full-compile baseline re-serializes
**byte-identically** to the one the compile produced.

Three of the persisted maps are absent from the module definition, and two of the three fail
*silently* if dropped: without the reference graph a moved callable surface rebinds nobody
and its callers keep executing the previous contract, and without the extension targets a
renamed `enumextension` leaves its old registration behind.

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

Switching between one-shot and `--watch` over one tree has to feel like one tool, so the
baseline is produced by **whichever of the two compiled** — not only by `--watch`. Both go
through `BcCompiler.TryBuildBaselineSnapshot`, so a baseline written by one is
indistinguishable from the other's:

| Mode | Writes it |
|---|---|
| `--watch`, cycle that compiles in full | from the committed workspace, after the generation loads |
| one-shot | `Program.PersistRadBaseline`, after `Assembly.Load` |
| `--server` | **no** — see the third limit below |

So the ordinary path — run the suite one-shot, then start `--watch` — arrives delta-ready, and
the first edit costs one object. Pinned by
`WatchTests.Watch_AfterAOneShotRun_ServesTheCache_AndDeltasTheFirstEdit`. The reverse direction
(watch, then one-shot) was already a plain cache HIT and is unaffected.

### What the snapshot costs

On a small app, almost nothing. Measured on `tests/runner-extras` (25 compiled apps, BC 28.1,
`BCCOMPILER_TIMING=1`): **105 ms total**, against 17.4 s of compiling and a 47.4 s run — 0.6%
of compile time, 0.2% of wall clock. Nearly all of it is one phase:

| Snapshot phase | 25 apps |
|---|---:|
| reference graph (a semantic model per tree, `GetSymbolInfo` per node) | 102 ms |
| object map | 3 ms |
| file declarations / extension targets / module definition | <1 ms each |

**That percentage does not generalise, and the reason is visible in the table above.** Four of
the five phases read symbols the compilation has already computed, but the reference graph asks
Microsoft for a semantic model *per syntax tree* and calls `GetSymbolInfo` on *every node of
every file* — so it scales with the **source tree**, not with the object count, and 25 apps of a
few files each is the cheapest possible shape for it.

Measured on NP Retail (7,339 `.al` files across the bundle, 6,949 objects in the Application,
BC 28.1, `--watch --no-cache`), the cold cycle grows from **1,125.9 s** without the snapshot to
**1,375.7 s** with it: **~250 s, or +22%** — not 0.6%. So quote the 25-app figure only for apps
of that size; for anything larger, budget the cold overhead against the size of the source tree
rather than as a fixed percentage. (The without-snapshot leg needs
`AL_RUNNER_EMIT_TIMEOUT_SEC=7200` to finish at all — the default 120 s emit timeout aborts an
app this size. A cycle that has a RAD workspace waits indefinitely instead, which is why the
with-snapshot leg needs no override.)

The cold cycle is not what `--watch` optimises, but it is not *unchanged* either: that +22% is
the price of delta-readiness, paid once per full compile, and it is what a warm cycle on the
same app buys back.

### Neither artifact gates a cache HIT

Neither is part of `AlCacheSidecars.IsCompleteEntry`, unlike the enum-registry and
query-symbols sidecars. Those carry side effects a HIT cannot function without, so their
absence must force a MISS. These carry an optimisation: gating a HIT on them would turn every
pre-existing entry into a MISS, and would force a cache-schema bump that discards every one —
both to withhold something that is only ever a speedup.

### Three limits, all stated because each looks like a bug from outside

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

## Measured on NP Retail

Measured on NP Retail (7,053 AL files / 6,949 objects) and its test app (286 files / 286
objects) — 7,339 files across the bundle — watched together as ONE bundle on BC 28.1,
6-core / 12 GB, one test codeunit selected.

A successful warm cycle reports the changed-object delta and overlay explicitly:

```text
[watch] NP Retail: delta +0 ~1 -0 over 1 changed file(s) → 1 object(s) re-emitted (1063ms)
[watch] NP Retail: overlay NP Retail#rad…g2 — 1 object(s), 28KB (536ms)
[watch] NP Retail Tests: unchanged — reusing the loaded module
```

One edit costs one object, for every object kind and every file operation — with three named
exceptions, all detailed under [scope and edge cases](#scope-and-edge-cases): a change to a
**codeunit's or id-less object's** serialized surface also rebinds its direct users (one hop,
not the transitive closure); a delta that strips a **table** also rebinds every codeunit whose
`TableNo` names it; and an **entitlement** edit also binds the app's permission sets, because
BC will not let one resolve a permission set from the packaged baseline.

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
| Id-less object (`interface`, `controladdin`, `profile`, `pagecustomization`, `profileextension`, `entitlement`) | `+0 ~1 -0` → 0 | — | — | not re-measured |

The id-less row's timing has **not** been re-measured on NP Retail; the behaviour is pinned by
`RadIdlessObjectTests` against a fixture instead, and a delta that compiles nothing has no
plausible way to cost minutes. Before those kinds were keyable at all, an edit to one — a
comment included — was a guaranteed whole-module rebuild, and NP Retail has 84 such files of
7,339 (60 `interface`, 16 `controladdin`, 8 `profile`).

### Cold versus warm, whole cycle

One codeunit body edit, same tree, `--watch`:

| Phase | Cold cycle | Warm cycle 1 | Warm cycle 2 |
|---|---:|---:|---:|
| AL emit | 950.3 s | 4.4 s | 1.5 s |
| C# compile | 663.6 s | 6.2 s | 0.3 s |
| AL test run | 140.3 s | 120.2 s | 107.5 s |
| **total** | **1,754.2 s** | **130.8 s** | **109.2 s** |

`main` has no baseline to delta against, so every warm cycle there re-emits all 6,949 objects:
739–766 s of AL emit, ~1,070 s total. The interesting part of the warm column is that
compilation has stopped being the cost at all — a warm cycle is now dominated by **running the
AL tests**, which is work the developer actually asked for.

> **Cold-cycle figures on this page come from three different runs and do not agree.** This
> table's 1,754.2 s, the snapshot A/B's 1,125.9 s → 1,375.7 s, and an earlier "~1,100 s, of
> which 649 s is the Application's AL emit" were taken at different times under different
> flags. Each is reported as measured; none has been reconciled against the others. Treat the
> order of magnitude as the claim, not the digits.

### Where a warm cycle's time actually goes

The delta is not the cost. `BCCOMPILER_TIMING=1` instruments the bundle-level phases as well
as the per-app ones — without that, a measured cycle accounted for about half of itself and
the remainder was guesswork. Steady state (cycles 2–3 after the baseline; a one-procedure
edit to one codeunit, which moves five objects because four files call it):

| Phase | Time |
|---|---:|
| Running the selected test codeunit (30 tests) | 11.0 s |
| `RecordPatches.AddSourceDir` — re-reads and re-parses every `.al` file in both apps | 7.9 s |
| **AL delta emit (`CreateForRad`)** | **2.4 s** |
| `NP Retail Tests` establishing it has nothing to do | 2.9 s |
| Register + publish symbols | 2.5 s |
| Roslyn compile + assembly load of the overlay | ~1.4 s |
| `GetSharedReferences` (warm) | 1.0 s |
| Resolve + load dependencies | 1.0 s |
| Whole-tree hashing (7,053 files) | 0.14 s |
| Field-trigger wiring and record prewarm | 0.20 s |
| C# overlay compile | 0.15 s |

The `AddSourceDir` row is the post-[#1903] figure and this branch carries that fix
(`4732a66d`, PR #1911): the phase builds one AL syntax tree per **file** rather than one per
**extractor**, so npcore's 7,339 files cost 7,339 AL parses per cycle rather than ~59,000. A
direct probe of the phase on the same corpus measured **7.0–7.9 s** at one parse per file
against **29.7 s** at eight.

`AddSourceDir` is still the largest overhead in the cycle, and it is O(whole tree) by
construction: the reload clears every parsed dictionary, so all of it is rebuilt to service an
edit to one file. Making it O(changed files) needs file→parsed-entry provenance, which does not
exist in `RecordPatches` today — the tableextension dictionaries are keyed by base-table name
and accumulate, so one file's contribution cannot currently be retracted.

Two figures the instrumentation corrected: post-registration field-trigger wiring and record
prewarm are **0.2 s**, not the ~12 s previously attributed to them; and `GetSharedReferences`
is cheap only when the bundle is configured as two source apps — leave a precompiled
`NP Retail.app` in `Test/.alpackages` and it re-reads 136 MB of packages per call, 8–15 s per
app per cycle.

[#1903]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/1903

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

## Scope and edge cases

Design, not measurement. Each subsection states a rule the delta path applies, the evidence
behind it, and — where the evidence is missing — says so.

### Id-less objects: the name is the identity, end to end

`interface`, `controladdin`, `profile`, `pagecustomization`, `profileextension` and
`entitlement` have no AL object id. The first two and `entitlement` are not returned by
`GetDeclaredApplicationObjectSymbols()` at all, so their declarations are read off the syntax
tree; `pagecustomization` and `profileextension` *are* returned and then report **id 0** —
they satisfy every "is this an application object?" test and have no id to be told apart by,
which is why keying on the id alone left them unkeyable.

Five of the six have an array in `ModuleDefinition` (`Interfaces`, `ControlAddIns`,
`Profiles`, `PageCustomizations`, `ProfileExtensions`), so their pre-edit copy *can* be
stripped. **Four actually are.** `ModuleDefinitionOps.WithoutObjects` is called with
`.Where(key => !key.IsExtension)`, and `IsExtension` is `Kind.EndsWith("Extension")` — so
`ProfileExtension` is carved out along with the real extension kinds and is never stripped.
That is not a bug: a `profileextension` resolves through a target profile that is itself
resolved from the baseline, and
`RadIdlessObjectTests.ModifyingAnIdLessObject_LeavesOneBaselineCopy_CarryingTheNewShape`
pins the outcome for it — the test fails both if stripping breaks the bind and if not
stripping shadows the edit.

`Entitlements` is the one that genuinely does not exist — there is no `EntitlementDefinition`
type in the compiler — which cuts the other way: an entitlement has no serialized copy that
could shadow an edit, and nothing downstream can resolve one.

An id-less object is a binding contract as much as a codeunit is, so its users are rebound
when its surface moves: the reverse dependency graph records edges onto it even though it is
not an application object and `ContainingApplicationObject` never returns one. Without that
edge, widening an interface without touching its implementer reports success, emits nothing,
and leaves the implementer bound to the previous contract.

Two more places where the name has to be the identity end to end: a removed one is stripped
from the previous module **by name** before Microsoft's symbol writer merges (the writer
matches the change element, and a serialized id-less element carries a synthesized id that
source cannot reproduce), and key names are decoded and case-folded, because AL escapes a
quote by doubling it and its identifiers are case-insensitive.

A **modified** id-less object needs no such pre-strip, and that was measured rather than
assumed: the writer merges one into exactly one copy carrying the post-edit shape. The
asymmetry with removals is coherent — for a modification the delta module supplies a
replacement, whereas for a removal only the change-element match can drop the old copy, and
that is the match the synthesized id defeats. `ModuleDefinitionOps.CountObjects` exists so
the suites can assert it: one *stale* copy also counts as one, so the tests check the count
and the shape. A second copy would not fail any compile — which of the two a later lookup
answers with is decided by array order — so a settled next cycle can be green over the
pre-edit definition.

### An entitlement is compiled with the app's permission sets

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
renaming one leaves every entitlement naming it pointing at something gone — delta green, cold
build **AL0185**. So a cycle that renames or removes a permission set rebinds every
entitlement. Only the *name* triggers it: a permission set has a real object id, so a rename
keeps its key and arrives as a modification rather than a removal plus an addition, and
changing `Assignable` or a permission line cannot break a reference by name. Rename and
deletion are pinned separately because they arrive on different code paths — `modified` and
`removed`. All of it is pinned by `RadIdlessObjectTests` against a cold compile of the same
tree, which is the only oracle available for an object the module definition does not
represent.

### A file that declares nothing costs nothing

Creating an empty `.al` file, editing a comment-only one or deleting it again compiles
nothing at all: the cycle records the new hashes and returns, keeping every loaded type. Not
measured on NP Retail — the behaviour is pinned against the fixture instead, and a cycle that
invokes no compiler has no plausible way to cost minutes.

What makes that safe is reading "declares nothing" **positively**, off BC's own parser, rather
than as the *absence* of a symbol — which is equally what an unidentifiable declaration looks
like. `ObjectSyntax` is the base type of every AL top-level declaration — the id-bearing kinds
through `ApplicationObjectSyntax`, and `profile`, `interface`, `controladdin`, `entitlement`
and `dotnet` directly — so a tree with no `ObjectSyntax` node declares nothing, full stop. A
declaration of a shape the delta does **not** recognise is therefore visible as such rather
than indistinguishable from an empty file, and takes the full-compile path naming its syntax
kind; that is what keeps an AL kind some future compiler adds from being silently skipped.

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

### What still forces a full compile

Every AL **object** kind is keyable. Two cases remain:

- **A `dotnet` package declaration.** Not an AL object: it changes what every object in the
  module can bind to, and a RAD object compilation carries no package declaration trees —
  `MergeRadBaseline` deliberately restores the previously committed `DotNetPackages`. It fires
  in both directions. Declaring one is read off the changed file's syntax; **deleting** one can
  only come from the workspace's per-file record, since there is no file left to parse. Pinned
  both ways by `RadObjectDeltaTests.AFileDeclaringADotNetPackage_StillForcesAFullCompile`.
- **A duplicate declaration** — a changed file claiming a key an untouched file still owns.
  `ws.FileOf` answers *which file* owns a key, which is the question that distinguishes a
  modification from a duplicate; `ws.Declares` only answered "does the module declare this
  key", which is true either way, so the new object was classified as a *modification* of the
  other file's object and the cycle reported success on a tree that does not compile. Measured
  against a cold compile: a duplicated `interface` name is four AL0197s cold and **no
  diagnostic at all** through the delta; a duplicated codeunit id is four AL0264s cold and one
  unrelated AL0185. Only the compiler can say what a duplicate means, so it gets the whole
  module.

Adding a future kind is a line in `RadObjectKey.IsIdlessKind` (plus `IdlessKindOf` if the
symbol API omits it, plus a `ModuleDefinitionOps` array entry if one exists) and a fixture
that declares one — a kind counts as supported when a test proves the round trip, not when
it has a key.

### A file resource is a question the delta answers itself

BC resolves a `controladdin`'s `Scripts` / `StartupScript` / `StyleSheets` / `Images` — and a
report's layout — through an `IFileSystem` attached to the compilation, anchored at the app
root (#1899/#1912). The delta is constructed with one, from the same
`BcCompiler.AppFileSystem(appRootDir)` the full compile uses and under the same
`appRootDir != null && Directory.Exists` guard, so neither path can answer a resource question
the other cannot. A present file resolves; an absent one raises `AL0327` naming it, from the
delta. Both directions are pinned by
`RadObjectDeltaTests.AControlAddInResourcePath_IsAnsweredByTheDelta_NotSilencedAndNotFailed`
— a fix that merely suppressed AL0327 would hide every real one.

Until this was measured, the delta had no file system, so an `AL0327` from it was not evidence
of anything and the cycle handed the whole module to a full compile. On the 20-object fixture
that cost **20 re-emitted objects for a one-line property edit to an add-in that emits no C#
at all**.

> **The constructor parameter and `WithFileSystem` are not interchangeable — this is the
> measurement that decided it.** `CreateForRad` takes `IFileSystem fileSystem` as optional
> parameter 12 on BC 28.1 (`Microsoft.Dynamics.Nav.CodeAnalysis` 17.0.36.40629), with
> `dotNetResolverFactory` at 13; `Compilation.WithFileSystem(IFileSystem)` exists separately.
>
> Attaching one **afterwards** returns a compilation that has lost its packaged module
> definition. Run against a body edit to `RadPerfHeaderExtA.TableExt.al`, whose target table is
> not in the delta:
>
> ```
> RadPerfHeaderExtA.TableExt.al@3:54: error AL0247:
>     The target Table 'RAD Perf Header' for the extension object is not found
> ```
>
> …with zero objects emitted. Passing the same file system through the **constructor** does
> not: the identical edit deltas to its one tableextension, clean, and binds the target out of
> the packaged definition. Pinned by
> `RadObjectDeltaTests.ADeltaGivenAFileSystem_StillResolvesAnUntouchedExtensionTarget`, which
> fails with exactly that AL0247 if the two are ever swapped. A body edit to a plain codeunit
> does **not** catch this — it needs nothing from the packaged definition and stays green
> either way; the probe has to be an extension object whose target is untouched.

### The fingerprint compares two different producers, so it canonicalises

`ObjectSurfaceFingerprint` decides whether a modified codeunit's or id-less object's surface
moved, and therefore whether its direct users are rebound. The two sides of that comparison are
built by **different code paths**. The committed baseline comes from
`SerializableSymbolModelConverter.ConvertModuleToSerializableSymbolModel(Compilation)` and
stays an object graph. The merged one is written by `CompilationUtilities.WriteSymbolReference`
and read back with `SymbolReferenceJsonReader` — a JSON round trip the committed one never
sees. Anything either side represents differently, for reasons of its own, reads as a surface
move — and a spurious move cascades, because the callers it drags in have fingerprints that
differ for the same reason.

Two such differences are known, and both are handled by **shape** rather than by property
name, because a list of individual offending properties has already proved incomplete twice.

**1. Provenance.** A compile given an app root (#1912 — what the CLI passes on every cycle)
records a `ReferenceSourceFileName` on every symbol it writes. This used to be asymmetric: only
the full compile had a file system, so a RAD-emitted symbol came back with it null and
comparing raw serialized symbols reported "the surface moved" for **every** re-emitted object.
Measured on the 20-object fixture: a one-line body edit went from 1 re-emitted object to 3,
over two rounds of rebinding. Where a symbol was read from is not part of any binding contract;
what it offers is. Pinned in both directions by
`ABodyEdit_StaysOneObject_WhenTheCompileRecordsSourceFileNames` and
`ACallableSurfaceEdit_StillRebindsItsCaller_WhenTheCompileRecordsSourceFileNames`.

> **The asymmetry is gone, and this entry is now removable — measured, not assumed.** With the
> delta constructed with the same file system, both producers record the identical value:
> `"ReferenceSourceFileName":"src/RadPerfService.Codeunit.al"` on both sides, **relative to the
> app root**, so it is stable across checkouts and machines. Dropping `ReferenceSourceFileName`
> from `_provenanceProperties` leaves all 31 `RadObjectDeltaTests` green.
>
> It is kept because the symmetry is now the **caller's** to maintain: `appRootDir` is an
> optional parameter, and a caller that gives one side a file system and not the other
> reproduces the cascade exactly. Measured with the strip removed and the file system withheld
> from the RAD compilation alone — both tests above fail, the body edit pulling in
> `RAD Perf Unrelated A`. The CLI cannot hit that (`appGroup.SuiteDir` is always a real
> directory), but `BcCompiler.EmitIncremental` is public and defaults it to `null`.
>
> Removing it is therefore a live option for whoever rewrites this fingerprint per-member
> (W2) — with the evidence above, not as a guess.

**2. Null versus empty.** The second instance cost a real app its watch loop. On NP Retail a
body-only edit to `NPR Adyen Management` diverged on this, in a 36 KB serialized surface:

```
before …"NameForSerialization":"NonDebuggable","Arguments":null}]…
after  …"NameForSerialization":"NonDebuggable","Arguments":[]}]…
```

An argument-less method attribute: `null` from the converter, `[]` after the round trip. Ten
characters. The codeunit read as moved, pulled in its **30 direct caller files**, and one of
those then failed to bind — so every warm cycle reported `EMIT-ZERO` plus an AL0126 and the
loop never progressed. The 20-object fixture missed it because no fixture object carried a
method attribute at all.

So the fingerprint is canonicalised by shape: provenance is stripped, and any property that is
absent — whether it says so with `null` or with an empty array — is dropped from both sides.
That cannot hide a real change, because a surface that genuinely gained or lost a member has
the property non-empty on exactly one side, and the two still differ. Both directions are
pinned: `ABodyEdit_StaysOneObject_WhenTheEditedCodeunitCarriesAnArgumentLessAttribute` (a body
edit stays one object) and `ChangingAnAttributesArguments_StillCountsAsASurfaceMove`
(retargeting an `[EventSubscriber]` still rebinds its callers, so ignoring attribute
*arguments* is not the fix).

### A delta damages surface a bystander holds only because the stripped object exists

Every modified or removed non-extension object is stripped from the packaged module definition
before `CreateForRad` binds the new source (`:620-624`), or its pre-edit shape shadows the
edit. That is correct and unavoidable. Everything the delta did not touch is still resolved
**from that stripped definition** — the syntax trees a RAD compilation is handed do not
participate in it.

**The thesis this section used to state is wrong, and too broad.** It said damage follows
whenever an untouched object's *serialized surface names* a stripped object. Measured: a plain
by-name pointer re-resolves fine, because the stripped object is still handed to the compiler
as a syntax tree and the syntax is the authority for that name. `RadByNameSubtypeTests` edits
a table whose name an untouched codeunit's `Record` parameter mentions, and **passes**.

What actually breaks is narrower: **surface the bystander holds ONLY BECAUSE the stripped
object exists.** A contributed field or enum value, conformance to an implemented interface,
the `Run(Record)` overload `TableNo` confers — none of that is in the bystander's own source,
so re-resolving a name does not put it back.

Damage needs a **triple**, which is why lone edits never showed it:

> **X** stripped ∧ **V** untouched, resolved from the packaged baseline, holding surface it
> derives from X ∧ **W** in the same delta, binding to exactly that part of V's surface.

Drop V and the delta is X+W, which bind against each other from source and never consult a
serialized surface. Drop W and nothing ever asks V's damaged representation a question. Every
fixture below is three objects, or it is not evidence.

**Nine shapes reproduce.** Each has a committed test asserting the delta reports exactly what
a cold compile of the identical tree reports. For the eight bind-time shapes cold is `[]` and
the delta invents a diagnostic:

| Shape | Test | What the delta invented |
|---|---|---|
| `tableextension` field | `RadByNameTableExtTargetTests` | `AL0132 'Record "ExtTarget Base"' does not contain a definition for 'Ext Value'` |
| `enumextension` value | `RadByNameEnumExtTargetTests` | `AL0132 '"EnumExt Base"' does not contain a definition for 'Extended'` |
| `pageextension` control | `RadByNamePropertyShapesTests.PageExtensionTargetObject_…` | `AL0270 The control 'BystanderMarker' is not found in the target` |
| `reportextension` column | `RadByNamePropertyShapesTests.ReportRelatedTable_…` | `AL0118 The name 'Description' does not exist in the current context` |
| query column type | `RadByNamePropertyShapesTests.QueryRelatedTable_…` | `AL0386 A required package dependency could not be found` |
| codeunit `implements I` | `RadByNameInterfaceCodeunitTests` | `AL0122 Cannot implicitly convert type 'Codeunit "ByName Impl"' to 'Interface "ByName Contract"'` |
| enum `implements I` | `RadByNameInterfaceEnumTests` | `AL0122`, the same, for an enum |
| `TableNo` under a table **rename** | `RadByNameTableNoRenameTests` | `AL0126 No overload for method 'Run' takes 1 arguments. Candidates: built-in method 'Run()'` |
| cross-app member-id move | `RadDeltaWatchTests.Watch_MovingAMemberIdInOneApp_RebindsItsCrossAppCaller` | `NavNCLCompilationException: Function ID … was called. The object with ID … does not have a member with that ID.` |

The last row is the odd one out and is listed here only because it is the same class of
symptom. It is not a packaged-baseline strip at all: it is the
[cross-app edge](#a-sibling-app-whose-callee-moved-rebinds-too), it fails at **run time**
rather than at bind time, and its oracle is a cold *run* of the same tree (which produces the
developer's own assertion failure) rather than an empty diagnostic list. It is fixed by a
different mechanism from the other rows — the sibling-app rebind, not the widened retry.

The first four rows are one family, not four: an untouched extension loses what it contributes
to a stripped target — a `tableextension` its field, an `enumextension` its value, a
`pageextension` its control, a `reportextension` its column. Extensions are exempt from the
strip on their own account, but nothing exempts them from their *target* being stripped.

**Where the diagnostic surfaces is not guessable from the AL id.** Six of the eight arrive out
of `rad.Emit(...)`; the `pageextension` control (AL0270) and the `reportextension` column
(AL0118) are raised by `GetDeclarationDiagnostics()` instead, because a layout `modify(...)`
and a dataset `add(...)` are declarations rather than method bodies. An earlier revision of
this section asserted all of them were method-body diagnostics; that was wrong, and a repair
wired only to the emit's return left exactly those two still broken. The repair now hangs off
**every** point the cycle can return an AL error.

#### The repair: one widened retry, driven by the diagnostic

`BcCompiler.DeltaCompile` computes a bystander set from the objects it stripped and re-runs
itself once with those files added to `changedFiles`. Four rules, and the shared test is
**surface a bystander holds ONLY BECAUSE a stripped object exists**:

> extensions whose target is stripped ∪ users of a stripped interface ∪ codeunits whose
> `TableNo` names a stripped table ∪ reports and queries with a dataitem on a stripped table

None of those sets grows with the call graph — an object has the extensions it has, an
interface the implementers it has — which is the whole difference between this and
`DirectUsersOf(every stripped object)`, the cascade that pulls 313 objects for one
hub-codeunit edit on npcore. It is also much narrower than "V's surface names X": a plain
by-name pointer re-resolves fine, which is why the six clean property shapes and
`TypeDefinition.Subtype` need no rule at all.

**Why the retry rather than computing it up front.** Every input is known before the strip, and
widening there unconditionally does repair all eight shapes in one pass. It also widens every
cycle that strips an object with an extension, a dataitem or an implementer — whether or not
anything in the delta ever asks the damaged representation a question. Measured on the
20-object fixture: a one-line body edit to `RAD Perf Header` went from **1 re-emitted object to
5** (two tableextensions, a report and a query), and
`RadObjectDeltaTests.EditingOneObject_ReloadsOnlyItsSemanticDelta` and
`RadWatchTwentyObjectTests` both fail on it. The damage is **latent**: it becomes real only
when something in the same delta binds to the part of the bystander's surface that is gone, and
then it is loud. So the repair is attached to the diagnostic, and a cycle that binds clean pays
nothing.

That also makes the precision guarantee structural rather than lucky: the six clean shapes in
`RadByNamePropertyShapesTests` produce no diagnostic, so no widening can reach them, and their
exact modified/emitted lists still guard what they were written to guard.

**A rebound bystander is a delta participant, and two suites disagree about that.** The repair
adds the bystander's file to `changedFiles` and recurses, so the bystander is classified as
modified and re-emitted like anything else. `RadByNameTableNoRenameTests` requires exactly
that — it asserts three emitted sources, naming the bystander, precisely so that a repair which
only avoided the diagnostic cannot pass. The three repaired shapes in
`RadByNamePropertyShapesTests` assert the opposite, because their expected lists were written
before any repair existed and say "two objects, no bystander". Those three still fail, on that
assertion alone: the diagnostic is gone and `delta == cold == []`. There is no mechanism that
satisfies both — a bystander is either rebound from source (and then it is in the change set)
or it is not (and then it is still broken). Rebinding it WITHOUT re-emitting it was considered
and rejected: the bystander's generated C# can depend on the stripped object's surface, so
skipping its emit trades a loud break for a stale assembly.

`TableNo` is the one rule that ALSO fires after a clean emit, and it earns that on measurement
rather than symmetry — see [below](#the-one-rule-that-also-fires-without-a-diagnostic).

Recursion terminates because a round can only ADD files: a bystander whose file is already in
`changedFiles` is skipped, so the widened retry's own bystander set shrinks to empty. A
bystander the workspace cannot trace to a file on disk takes the whole module, named through
`FullCompileBecause` like every other bail-out.

#### `TypeDefinition.Subtype` does not reproduce

Carried into this work as "the important one" — a method parameter's `Record "T"` serializes
`T` as a bare string, counted at **13,610** occurrences on npcore, ~54× more common than
`TableNo`. `RadByNameSubtypeTests` builds the triple over it and **passes**: editing a table
whose name an untouched codeunit's `Record` parameter mentions is fine, because the stripped
table is still supplied as syntax and the parameter's type re-resolves against it.

That result is what disproved the wider thesis, and it deletes the claim that `Subtype` is the
widest exposure. It is not exposure at all.

#### Six by-name property shapes are clean, and are now pinned

`RadByNamePropertyShapesTests` builds the same triple over each and asserts delta == cold:
`SourceTable`, `CalcFormula`, `RunObject`, `LookupPageId`/`DrillDownPageId`, enum-value
`Implementation`, and `RoleCenter`. All six survive the strip.

Two of the six are blunter than the rest and the suite says so: `LookupPageId`/`DrillDownPageId`
and `RoleCenter` are *object-level* properties with no member for a W to aim at, so the
strongest available W merely forces the bystander to resolve (`Record V`,
`profileextension extends V`). They would not notice a degradation that left V's members intact.

The trip-wires are worth their runtime because a broken shape here really would look different
from a working one: each reference was first pointed at a name that does not exist and the
fixture recompiled cold, confirming this pipeline diagnoses the break at all. All nine shapes
that got a `[Fact]` are diagnosable that way.

#### Three shapes are untestable with a cold-compile oracle — a known coverage gap

`TableRelation`, `Permissions` and `IncludedPermissionSets` are **not** tested, and are not
"clean". Measured: a dangling `TableRelation` — to a missing table *or* to a missing field of a
table that exists — compiles **silently**, and so do `Permissions` and `IncludedPermissionSets`
naming things that do not exist. A cold compile therefore cannot tell a surviving reference
from a destroyed one, so `delta == cold == no diagnostics` would be green by construction.

That is a test asserting nothing dressed as coverage, which is worse than an acknowledged gap.
Their objects stay in `Fixtures/RadByNamePropertyShapes` because they document the shape and
cost nothing, but no `[Fact]` claims to prove them. A real oracle for these would have to
inspect the merged module definition directly rather than ask the compiler.

#### Query `RelatedTable` was not a separate defect after all

Recorded here for a while as unexplained: the delta reported `AL0386 A required package
dependency could not be found` where a cold compile of the same tree was clean, and `AL0386` is
a package-resolution failure rather than anything that looks like a by-name break.

It is the same break as the report, with a diagnostic that hides it. A query dataitem records
its table exactly as a report dataitem does — a `RelatedTable` NAME — and a query column
serializes only its `SourceColumn` field name, never a type. So the dataitem's by-name
reference is the *only* record of what any column is, and once the table is stripped the
compiler cannot answer `Host.QueryNo`'s type from anywhere. Adding the report/query dataitem
rule to the widening set turned both `ReportRelatedTable_…` and `QueryRelatedTable_…` from a
diagnostic into `delta == cold == []` in the same change, without any query-specific code.

The lesson worth keeping is about the diagnostic, not the query: two of these breaks
(`AL0386` here, `AL0270` for the pageextension) name something that has nothing to do with the
edit, so grouping this family by AL id would have split it three ways.

#### "Stub instead of strip" is measured dead — do not re-propose it

Leaving a member-less stub in the packaged definition instead of removing the object is
constructible (every definition type has a parameterless constructor and settable properties)
and it does repair the shapes above. It also **shadows the supplied syntax**, on all five
object kinds tested:

| Scenario | STRIP | STUB |
|---|---|---|
| table stubbed, another delta object reads its fields | clean | `AL0132 … no definition for 'Code'` |
| codeunit stubbed, another delta object calls its method | clean | `AL0132 … no definition for 'Ping'` |
| interface stubbed, another delta object calls it | `AL0122` | `AL0132 … no definition for 'Ping'` |

It converts "one dangling bystander" into "every consumer in this delta loses the edited object
entirely" — the exact failure the strip exists to prevent, in a harder-to-detect form. The
extension carve-out is not a counter-example: an extension resolves its own members through a
target that is being rebuilt from source in the same delta, a structurally different path.

#### The one rule that also fires without a diagnostic

`TableNo` is the case that was observed first, on NP Retail. A cycle that rebound
`AdyenSetup.Page.al` also had `NPR Adyen Reconciliation Hdr` in its change set. Codeunit
6248336 declares `TableNo` on that table, was **not** in the delta, and was confirmed still
present in the packaged definition with the property intact — only the table it names was gone.
Its `Run(Record)` overload therefore did not exist, and an untouched page calling
`AdyenRecreateRecDoc.Run(ReconHeader)` — code that compiles clean cold — failed with
`AL0126: No overload for method 'Run' takes 1 arguments`.

So a delta that strips a table also rebinds, from source, every codeunit whose `TableNo` names
it (`ModuleDefinitionOps.CodeunitsWithTableNo`). Both modified and removed tables, for opposite
reasons: a modified one must resolve to its NEW shape, while a removed one must produce the AL
diagnostic a cold compile produces instead of a silently overload-less codeunit.
`RadRunnableCodeunitBindingTests` pins both.

Unlike the other three rules this one runs on a cycle that produced **no diagnostic at all**,
and that asymmetry is measured rather than stylistic: on the 20-object fixture the identical
split still binds clean — `RadRunnableCodeunitBindingTests` says so in its own summary — so the
AL0126 only appears at NP Retail's scale. A repair that waited for the diagnostic would
therefore be scale-dependent, which is the one property a delta path cannot have.

**The rename reaches it the other way.** `RadByNameTableNoRenameTests` renames the table, keeping
its id, so it arrives as a *modification* while the packaged codeunit still spells the old name
— and there the AL0126 *does* fire, at emit time, before the post-emit widening runs. That is
why the rule is evaluated in both places: it is one rule, reached from a clean emit or from the
diagnostic-driven retry. Collecting the table under BOTH its current and its committed name is
what makes the rename match at all, and BC serializes `TableNo = 72100` — numeric in AL source —
as the NAME `"Rename Target"`, confirmed by reading a persisted `rad-symbols.json`.

#### The widening rules name themselves in the log

A widened cycle says **which** rule widened it, not just that something did:

```
[watch] NP Retail: rebinding 4 direct caller file(s)
[watch] NP Retail: rebinding 12 file(s) — direct callers, plus 3 whose codeunit's TableNo
      names a table this cycle strips
[watch] NP Retail: rebinding 3 file(s) whose codeunit's TableNo names a table this cycle strips
[watch] NP Retail: rebinding 5 bystander file(s) — 3 that would extend an object this cycle
      strips, 2 that would hold a dataitem on a table this cycle strips
```

They have completely different remedies — a moved callable surface is the developer's own edit
propagating, while a stripped-surface rebind is the delta compensating for a packaged symbol it
had to remove — and a single undifferentiated count is what made a one-object body edit look
indistinguishable from a real cascade for the length of an npcore investigation.

### Member ids are signature hashes

Generated calls bake Microsoft's member id, so what moves one decides who must be rebound.
Established by static analysis of npcore's 24 MB `rad-symbols.json` (2,420 codeunits / 12,354
methods), a controlled experiment, and decompiling the algorithm.

`MethodSymbol.CalculateMethodId` hashes the **upper-cased** method name (FNV), the return
type's **bare `NavTypeKind`**, and per parameter `(index, IsVar, NavTypeKind)`, plus a
per-parameter subtype term **only** when the method requires overload disambiguation. The same
`get_Id` feeds the serialized `Id`, the `case <id>:` in `OnInvoke`, and the caller's
`Target.Invoke(<id>, …)` — verified: 3,997 of 4,000 symbol-file ids appear verbatim as
`ldc.i4` constants in npcore's emitted 61 MB DLL.

| Does **not** move the id | **Moves** the id |
|---|---|
| insert a method before another · append · reorder | parameter type |
| body-only edit | return type |
| parameter **rename** | `var` / byref on a parameter |
| an overload **added** | return removed |
| made `internal` | method renamed |
| attribute added | |
| global var added | |
| object `Access` changed | |
| containing codeunit renamed | |

A RAD delta re-emit reproduces ids bit-identically to a full compile — verified both in the
emitted C# and by a cache-HIT caller dispatching correctly against a delta-re-emitted callee.
Nothing in the AL surface is ordinally numbered: methods hash, table fields carry
author-assigned numbers, enum values and add-in events bind by name.

**Four contract changes are id-invisible.** The id cannot be used as a proxy for "the contract
moved":

- **access** (`internal` / `local`),
- **attributes** (`[NonDebuggable]`, `[TryFunction]`, `[IntegrationEvent]`),
- **parameter names**,
- **the return type's subtype** — only the bare `NavTypeKind` is hashed, so
  `Codeunit A` → `Codeunit B` and `List<Integer>` → `List<Text>` are invisible.

**The overload hazard is silent.** Adding `Which(Integer)` alongside an existing
`Which(Decimal)` does **not** move the existing member's id — and *does* move which id the
caller bakes, because the new overload changes disambiguation:

| | `Which(Decimal)` id | caller bakes | runtime |
|---|---|---|---|
| before | `-460041644` | `-460041644` | `BOUND-TO=DECIMAL` |
| after adding `Which(Integer)` | `-460041644` *unmoved* | **`-460041642`** | `BOUND-TO=INTEGER` |

The old `case` label survives in the callee, so a stale caller keeps working and answers with
the wrong overload. Nothing throws.

**Inferred, not measured:** BC's `CanBeOverloaded()` appears to exclude events, subscribers and
handlers from the subtype term, which would make `OnFoo(Record Customer)` and
`OnFoo(Record Item)` collide on one id. Nobody has run it.

### A sibling app whose callee moved rebinds too

`ReferenceTarget` keeps an edge whose target is a **sibling source app of the same bundle**,
and drops every edge into a precompiled `.app` dependency. It used to drop both, which is the
bug this section is mostly about.

The hole that left: editing app A re-emits A's objects; app B never enters `changedFiles`,
takes the `NoChange` short-circuit, and keeps executing IL that bakes A's **previous** member
ids. Nothing in B's own file hashes can say so, and the graph — which was the only thing that
could — had nothing to say. Measured on npcore before the fix: the Test app's persisted
baseline recorded **1 cross-app edge in 505**, and that one was an extension target, not a
call.

Two failure modes, and the difference matters for
[`.claude/rules/loud-failures.md`](../.claude/rules/loud-failures.md):

- **Loud**, when the retired member id is absent from the re-emitted callee — a
  `NavNCLCompilationException` naming a function id no AL author has ever seen, where a cold
  compile of the same tree reports the developer's own assertion failure. Loud is not correct,
  but it is at least visible.
- **Silent**, when the old id survives on the new object. The overload case above is exactly
  that shape: the caller keeps dispatching an id that still resolves, to the wrong member.

Both halves have tests that were RED before the fix, and the pair exists because measuring only
the first one would have understated the bug.

`Watch_MovingAMemberIdInOneApp_RebindsItsCrossAppCaller` is the loud half. It retypes
`Delta Lib`'s parameter from `Integer` to `Decimal` — which moves the id, per the contract
above — while leaving `Delta Bridge`'s own source valid, because an Integer argument widens to
a Decimal parameter. It asserts that `Delta Bridge` is re-emitted in the same cycle, that the
AL result matches a cold run of the identical tree, and — as a control — that a body-only edit
leaves `Delta Bridge` **unchanged**, so a fix cannot be "rebind everything".

`Watch_AddingAnOverloadInOneApp_RebindsItsCrossAppCaller` is the silent half, and the reason
loudness must not be taken for a mitigation. Adding `Pick(Integer)` beside `Pick(Decimal)`
leaves the Decimal overload's id *and* its `case` label intact; what moves is which id the
caller bakes. Measured:

```
[watch] Delta Bridge: unchanged — reusing the loaded module
FAIL  Codeunit60941.PickBindsTheOnlyOverload
      NavNCLDialogException: Delta Lib Pick returned 1, expected 2
```

No exception, no diagnostic, no log line — just the previous overload's answer. The only thing
between that and a green run over wrong code is an assertion that happens to check the value.
The test pins the silence explicitly, so reading it cannot leave the opposite impression.

So the loudness of the retyped-parameter case is an accident of which edit was measured first,
not a property of the bug.

This is distinct from the cross-app *object-type* staleness `AlObjectResolution` handles.
Both are fixed now, by different mechanisms.

#### How the rebind is decided

Four constraints shaped it, and each one is a way to get it silently wrong.

**Identity is not the compiler's `AppId`.** `RadWorkspaceStore` keys an app group with no
`app.json` as `name:<module>`, while the compilation built for that group is given
`DeterministicGuid(moduleName)`. Mapping the compiler's Guid straight to a workspace therefore
never matches such a group — and it fails as *zero retained edges*, which is indistinguishable
from "this app calls nothing". `Rad/RadAppCohort.cs` owns the translation, in one place, so the
two halves cannot drift.

**The cohort comes from the app graph, not from the live workspaces.** A one-shot run has no
`RadWorkspace` at all — the store is only enabled under `--watch` — and it still writes the
baseline sidecar a later watch hydrates. Deciding what to retain from live workspaces would
persist an envelope with no cross-app edges, so the first watch over a cached tree would be
exactly as stale as before, with nothing to show for it.

**Precompiled dependencies stay out.** Only apps compiled from source in this bundle can change
between two watch cycles. A `.app` in `.alpackages` cannot, and if one is replaced,
`ReferenceSignature`'s `ref|…|version|appId` line moves and the workspace invalidates wholesale.
Retaining those edges would cost 70k–210k additional edges on npcore — a 2–4× sidecar — for
edges that can never be actionable.

**`RadObjectKey` is not widened.** It is the key type of the object map, the extension-target
map, `RadChangeSet` and every RAD test, and it is scoped to one app by construction: two apps
can each declare `interface "Contract"` and both key as `("Interface", 0, "CONTRACT")`.
Cross-app edges live in a map of their own, keyed `(app identity, RadObjectKey)`, so every
same-app path keeps its type and its cost.

The signal itself is a **committed broadcast, not a drainable event**: each producer carries a
publish generation, each consumer a per-producer watermark. A queue drained by the first asker
would leave the *second* dependent of one producer silently bound to the old ids — so the
watermark is read, not taken. It is published from `RadEmitResult.Commit`, which is the first
moment the generation is known to have loaded; a candidate the C# backend rejects announces
nothing, and the consumer's watermark stays put so the next cycle re-widens rather than
dropping the rebind. A consumer that compiles *before* its producer — `BuildAppGroups` falls
back to declaration order on a dependency cycle — picks the signal up on the next cycle instead
of losing it.

The widening is computed **before** the no-change short-circuit, because the consumer's own
source is exactly what did not move. A consumer this cycle cannot trace to a file inside its own
app's source tree takes the full compile with a named reason, rather than shipping a module that
still dispatches the sibling's previous ids.

A full rebuild broadcasts "assume everything moved". That is not laziness: a full compile is
preceded by `Invalidate`, which drops the object map, so by the time the new module exists there
is no record of what the previous one declared and a per-key answer cannot be reconstructed. The
reasons a cycle rebuilds in full — a dependency, identity or preprocessor-symbol change, a
`dotnet` package, an edit the delta could not classify — are exactly the ones able to move any
member id in the module.

Neither the generations nor the watermarks are persisted. A watermark restored from disk would
be compared against a fresh producer's counter starting at zero, so every publish would read as
already-consumed and the rebind would be suppressed — the same silent staleness, reintroduced by
the persistence of the fix.

#### What it costs, measured on the three-app fixture

A body-only edit still logs `Delta Bridge: unchanged`. The cycle that removes a member logs:

```
[watch] Delta Lib: delta +0 ~1 -0 over 1 changed file(s) → 1 object(s) re-emitted
[watch] Delta Bridge: rebinding 1 cross-app caller file(s) — 1 that call Delta Lib
[watch] Delta Lib Tests: unchanged — reusing the loaded module
```

Two properties in three lines: the widening names its count and its producer rather than being
an undifferentiated re-emit, and it does **not** cascade — Bridge is re-emitted but its own
surface did not move, so it publishes nothing and the app that calls *Bridge* is left alone.

The sidecar schema went 1 → 2 to carry these edges, and a schema-1 envelope is refused rather
than read. That refusal is not pedantry: `System.Text.Json` ignores members it does not find, so
a schema-2 reader handed a schema-1 envelope deserializes it happily and gets **zero** cross-app
edges — a hydrated workspace that silently rebinds no sibling caller, which is the exact bug
those edges exist to fix.

**What the refusal costs, measured rather than assumed.** On an ordinary upgrade it costs
nothing, because it never fires: the AL-output cache key's second line is
`runner:<sha256 of al-runner.dll>`, so changing `Schema` changes the binary, which changes the
key — a schema-2 reader computes a different key and never opens the schema-1 envelope at all.
The `.dll` misses too, and the full compile that follows writes a fresh schema-2 pair.

When it *does* fire — an envelope hand-edited, or a build that changed the schema without
changing the binary — it is a whole-**bundle** compile, not one app's: `PrepareBundleReload`
invalidates every workspace while any app lacks a baseline, so one refused envelope turned a
2-object delta into all 9 on the three-app fixture. **And it does not heal.** `TrySave` is
guarded on sidecar paths that are only assigned while no generation is loaded, and a cache HIT
loads one immediately — so on the cycle that pays the compile those paths are null and the
schema-1 envelope is still on disk afterwards. Every subsequent watch session pays it again.

#### One-shot then watch still pays a full compile, for a dependency target

Measured while proving the hydration path: the sidecar written by a one-shot run does hydrate,
and its cross-app edges do rebind a sibling caller on the first edit — but only for apps nothing
else depends on. For a **dependency target**, the producer's own hydration does not survive:

```
[watch] Delta Lib: full rebuild — the resolved dependency set changed (1 → 0)
```

A one-shot publishes each dependency-target app's symbols in a pre-pass (`EmitSiblingSymbols`,
which the RAD path skips), so by the time `Delta Lib` compiles, the sibling-symbols directory
already holds *Delta Bridge*'s symbols and they enter Lib's resolved reference set. Lib's
persisted signature therefore carries a `ref|…|Delta Bridge|…` line the watch path can never
reproduce, and `ArmFor` invalidates.

So for any app another app depends on, one-shot-then-watch still costs a whole-module compile on
the first edit — the exact cost the sidecar exists to remove. Unfixed, and pinned by an assertion
in `RadDeltaWatchTests.OneShotSidecar_ThenWatch_HydratesCrossAppEdges_AndRebindsTheSiblingCaller`
so it cannot quietly change.

#### A one-way hole, same-app and cross-app alike

`MapObjectReferences` walks the objects `UniquelyKeyedObjects` returns, and that is only
`IApplicationObjectTypeSymbol`. An `interface`, a `controladdin` and an `entitlement` are not,
so they are recorded correctly as reference **targets** and never as reference **sources**.
Whatever such an object names is invisible to the graph in both directions of app boundary.

Untested and unmeasured. The impact is bounded by what those kinds can reference at all — an
interface's method signatures can name objects, an entitlement's `ObjectEntitlements` names
permission sets (which the permission-set-rename rule covers separately, by name, precisely
because no semantic model reports that edge) — but "bounded" is not "none", and nobody has run
it.

### Reloaded dependency tableextensions

Watch reloads preserve the symbol paths for precompiled dependency apps and re-merge their
`tableextension` fields when per-cycle record metadata is rebuilt. A second or later cycle
can therefore resolve fields supplied by a warm precompiled dependency; the
`extension field ... not found in NCLMetaTable` reload regression is covered by
`RadDeltaWatchTests`.

### Every full compile says why, where the developer is looking

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

**These lines are collected, not only logged.** The interactive dashboard redirects both console
streams to `TextWriter.Null` for the duration of the bundle loop so its painted frame is not
scrolled away — which means a stderr-only reason is invisible in the one mode a developer
actually watches. `FullCompileBecause` therefore does both, always: `Rad/RadCycleNotes.cs` is a
process-wide collector the watch loop drains after the loop restores the streams, and the
dashboard renders a yellow **full recompile** panel above the test tree. Yellow, not red: the
results below it are correct, the cycle was only slow. On a plain delta cycle the panel is
absent entirely, so "no full recompile happened" is readable without parsing anything.

One reason is reported by a *later* cycle than the one that discovers it. A compile that cannot
record a baseline makes every subsequent cycle a full compile, and those later cycles know
nothing about why — so they rebuild in silence while the reason has scrolled past.
`RadWorkspace.PendingFullCompileReason` parks it, and the compile that acts on it consumes it.
Exactly once, and a committed baseline retires it, so it cannot end up attached to some
unrelated later rebuild. The invalidation paths deliberately do NOT park — `Invalidate` reports
in the same cycle, and doing both would say it twice.

## What the tests pin

Timings drift with the machine, so the executable contract is stated as identities and
counts instead. The first four suites share one real 20-object app,
`AlRunner.Tests/Fixtures/RadTwentyObject`, so "all of it" and "the one that changed" are
different numbers.

| Suite | Claim |
|---|---|
| `RadObjectDeltaTests` | One edit → exactly which objects re-emit and which CLR types change owner, for ten object kinds plus schema additions, a rename, a callable-surface change, and a rejected or abandoned candidate; and the two ways the committed and merged baselines describe one unchanged surface differently — a recorded source file name, and an argument-less method attribute that is `null` on one side and `[]` on the other — each with the opposite direction pinned beside it |
| `RadDeletionDeltaTests` | One deletion → zero objects re-emitted, exactly those object identities removed, exactly those CLR names tombstoned, every survivor still the identical baseline `Type` |
| `RadMetadataDeltaTests` | One edit → exactly one page/report/xmlport/enum metadata entry moves; a deletion drops its entry, on the delta path and on the full-compile fallback; a renamed enumextension leaves one registration; a rejected candidate leaves none behind |
| `RadWatchTwentyObjectTests` | The same claims against the real `--watch` process, via its own `[watch]` log lines, with the AL test outcome proving the new code actually ran |
| `RadIdlessObjectTests` | The six kinds with no object id: two `profile`s (and two `entitlement`s) are two objects rather than one colliding key; the kinds the symbol API never reports and the kinds it reports with id 0 are both tracked to their file; editing or deleting one is a delta that compiles no C#; narrowing an interface binds against the new contract rather than the baseline's copy; widening one WITHOUT touching its implementer still rebinds it, and so does renaming a `pagecustomization` without touching the `profileextension` that names it; a modification leaves the merged baseline holding exactly ONE copy, carrying the post-edit shape — including for the `profileextension` that `IsExtension` exempts from the strip; a deletion leaves it entirely; identity survives an embedded quote and a case-only rename; an `entitlement` — which the module definition cannot represent at all — accepts and rejects exactly what a cold compile of the same tree does, in both directions of its permission-set relationship; and a changed file claiming a key an untouched file still owns does not pass as a modification |
| `RadTableExtensionSelfReferenceTests` | A `tableextension` that reads its OWN fields through `Rec` still deltas — npcore's shape, which the 20-object fixture missed because both of its extension triggers touch a base-table field: adding a field, adding and reading one in the same edit, two extensions on one table seeing each other's new fields, removing a field (which must stop binding), and introducing a self-reference where there was none |
| `RadRunnableCodeunitBindingTests` | A delta that strips a table also rebinds the codeunits whose `TableNo` names it, so an untouched `CodeunitVar.Run(Rec)` still binds — with no diagnostic to prompt it, which is why that one rule does not wait for one — and dropping `TableNo` for real still reports the AL0126 a cold compile reports |
| `RadByNameTableExtTargetTests`, `RadByNameEnumExtTargetTests`, `RadByNameInterfaceCodeunitTests`, `RadByNameInterfaceEnumTests`, `RadByNameTableNoRenameTests` | One three-object triple each, asserting the delta reports exactly what a cold compile of the identical tree reports. Cold is `[]`, and each delta invented the diagnostic in the table above until the widened retry landed. The `TableNo` one additionally asserts the bystander is re-emitted, so a repair that merely avoided the diagnostic cannot pass |
| `RadByNameSubtypeTests` | The same triple over a method parameter's `Record "T"` — a plain by-name pointer re-resolves against the supplied syntax, which is what disproved the wider damage thesis, and it must never widen |
| `RadByNamePropertyShapesTests` | Six by-name property shapes survive the strip and are pinned as trip-wires (`SourceTable`, `CalcFormula`, `RunObject`, `LookupPageId`/`DrillDownPageId`, enum-value `Implementation`, `RoleCenter`); three do not (`pageextension` control, `reportextension` column, query `RelatedTable`) and are repaired by the widened retry. Every scenario asserts the exact modified/emitted lists. For the six that is exactly right and structurally safe — a clean shape raises no diagnostic, so no widening can reach it. For the three repaired ones those lists now INCLUDE the bystander, because a bystander rebound from source IS a delta participant; the original "no bystander" expectation encoded the premise that those shapes were clean, which measurement disproved |
| `RadWorkspaceFileOfTests` | `FileOf` names the declaring file for every object the fixture declares, measured against an oracle read off the tree; returns null for a key the app never declared; and follows an object that moves to a different file across a commit |
| `RadBulkSwitchDeltaTests` | A whole-version switch (8 modified + 2 added + 2 deleted, in one cycle) re-emits exactly those twelve and no more, in both directions, leaving the workspace settled |
| `RadDeltaWatchTests` | Multi-app watch behaviour end to end: a warm reload still resolves a precompiled dependency's `tableextension` fields; a cross-app member-id move rebinds its caller in both its loud form (a retyped parameter, so the old id is gone) and its silent one (an added overload, so the old id survives and the caller would otherwise get the previous overload's answer); and the precision controls — a body-only edit leaves the caller `unchanged`, while a real surface move names its count and its producer and is asserted **not** to come from a full-rebuild broadcast |
| `WatchTests` | Cycle 1 of a watch is served from the AL-output cache, and the first edit really runs (never a second HIT): delta'd when the entry carries a baseline — after a one-shot run, and after an earlier watch — and building one when it does not |
| `RadBaselineSidecarTests` | A persisted baseline restores the compiler's symbol picture **byte-identically**, and a workspace hydrated from it deltas the first edit, rebinds the direct caller of a moved surface, classifies a deletion as a removal, and still rebuilds for a deleted `dotnet` package — plus the five ways hydration must fail closed (tree moved, file edited in place, symbols missing, unknown schema, app identity changed) |
| `WatchSourceTests` | The watch loop's own contract, deterministically: an edit made from inside `onArmed` is always seen (#1822's race); watchers arm exactly once; a burst below the quiet window releases only after it settles; a single save releases within one quiet window; and a watcher-buffer overflow is handled loudly rather than swallowed |
| `WatchBurstSwitchTests` | The same quiescence claim against the real `--watch` process: a seven-file version switch produces exactly ONE cycle, against the settled tree, with the correct result — not a phantom failure mid-checkout followed by a second cycle |
| `WatchStateResidencyTests` | One test re-run across three `--watch` cycles sees no state from any earlier one: no manual event binding (from a test-codeunit global *or* a `SingleInstance` one), no `SingleInstance` field value, no committed row. The AL fixture also proves it can observe each kind of state while it IS live, so a gutted runtime cannot pass by making it all unobservable |
| `WatchOutputSlicingTests` | The test harness's own reading of a live watch process: which cycle a `[watch]`/`[emit-timing]` line belongs to, over synthetic line sequences that reproduce a starved stderr pump — so a flaky read cannot be mistaken for a flaky cycle |
| `WatchDashboardTests` | A recorded full-recompile reason reaches the dashboard verbatim with the app it belongs to, and the panel is absent on a delta cycle |

## Control

There is no switch to turn this on: `--watch` is delta compilation. Eligibility and
fallback are automatic, and a change the delta path cannot classify is never forced through
an overlay — it becomes a full compile.

`AL_RUNNER_RAD=0` forces every watch cycle through a whole-module compile. It exists to
bisect a suspected delta bug ("does it still happen without the overlay?"), not as a
supported mode.
