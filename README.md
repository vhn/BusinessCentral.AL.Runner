# BusinessCentral.AL.Runner

[![Test Matrix](https://github.com/StefanMaron/BusinessCentral.AL.Runner/actions/workflows/test-matrix.yml/badge.svg)](https://github.com/StefanMaron/BusinessCentral.AL.Runner/actions/workflows/test-matrix.yml)
[![NuGet](https://img.shields.io/nuget/v/MSDyn365BC.AL.Runner)](https://www.nuget.org/packages/MSDyn365BC.AL.Runner)

Run Business Central AL unit tests in **milliseconds** — no BC service tier, no Docker, no SQL Server, no license required.

## Changes on this temporary performance fork

This is a temporary performance fork of
[`StefanMaron/BusinessCentral.AL.Runner`](https://github.com/StefanMaron/BusinessCentral.AL.Runner).
It carries **85 commits** that are not upstream yet (298 files, +28,333 / −711), in two areas:
the **first** compile of a large app, and what a **save** costs once the runner is watching.

Measured on **NP Retail** — npcore, 7,053 + 286 `.al` files, 6,949 AL objects, BC 28.1 — as one
bundle with one test codeunit selected, on an otherwise idle 6-core / 12 GB Mac. The runner is
exactly as shipped: no environment overrides, no flags beyond `--watch --test`. Each row is one
scripted edit in a single `--watch` session, timed from save to results on screen; the cold row
includes compiling the bundle's dependency apps from AL.

| What you do | this fork | what the cycle actually did |
|---|---:|---|
| **Cold compile** — fresh cache, nothing warm | **282 s** | full compile of both apps + the delta baseline snapshot |
| **`--watch` — change a codeunit** | **5 s** | `delta +0 ~1 -0` · 1 object re-emitted in 0.8 s |
| **`--watch` — add a codeunit** | **5 s** | `delta +1 ~0 -0` · the new object only |
| **`--watch` — delete a codeunit** | **6 s** | `delta +0 ~0 -1` · nothing re-emitted, the name is tombstoned |
| *Edit shapes that used to cost a whole-module rebuild* | | |
| First edit after the runner starts | **8 s** | `delta +0 ~1 -0` — the cold compile left a baseline behind, so the very first edit already deltas |
| Copy-paste a `.al` file → duplicate object id | **1 s** | `AL0264` reported, workspace untouched, **no rebuild** |
| Copy-paste a procedure → duplicate method | **2 s** | `AL0440` reported, **no rebuild** |
| Rename an object (same id, new name) | **6 s** | `delta +0 ~1 -0` — a renamed object is still a delta, not an add+remove |
| Add a field to an existing table | **5 s** | `delta +0 ~1 -0` |
| Change an existing table field | **5 s** | `delta +0 ~1 -0` |
| Add an action to an existing page | **5 s** | `delta +0 ~1 -0` |
| Save a file back to its original bytes | **4 s** | **no compile at all** — the tree re-hashes identical |

Cycles 2 and 3 are the ones that prove a delta *ran* rather than merely compiled: flipping a
`Label` the suite asserts verbatim turns it **28 pass / 2 fail**, and reverting it returns
**30 / 0**. The delta-compiled application code really executed.

### Initial cold compilation performance

Everything in this group is about the one-shot / first-cycle compile of a real app. Nothing here
changes *what* is compiled — only how the work is scheduled, how often the same bytes are read,
and how much of the heap is live at the peak.

- **Run under Server GC** (`8c355880`) — the single biggest lever, and it was never a tuning
  question. A cold AL compile is GC-throughput-bound: the Application module's emit produces
  165 MB of C# that becomes ~330 MB of UTF-16 and stays reachable until Roslyn finishes.
  `AlRunner.csproj` set no GC property and the shipped `runtimeconfig.json` carried no
  `System.GC.Server`, so **every user ran Workstation GC** — while every benchmark harness in the
  repo exported `DOTNET_gcServer=1` by hand, which is exactly why it went unnoticed. Identical
  binary, GC flavour the only difference: BC AL emit 837.7 s → 175–331 s, Roslyn bind+IL 293.9 s
  → 88–104 s, wall 1,283 s → median 470 s. Faster *and* 0.5 GB smaller resident, because the
  Workstation run spends the difference thrashing. DATAS was tested as a middle ground and
  rejected (537 s cold, and the unit suite's summed test-seconds went 1,328 → 2,071 s).
- **Run BC's emit across threads** (`41d7fc9f`, opt-in first in `4a824734`). `ConcurrentEmit`
  defaults to false on both `CompilationOptions` and `EmitOptions`, so all 6,956 of npcore's
  objects reached `AddApplicationObject` on one thread while the bind phase beside it used the
  whole machine. An earlier measurement *refuted* this and shipped it off; that reading was taken
  on a memory-starved host before Server GC landed. Re-measured with the arms interleaved under
  Server GC, the worst on-leg beats the best off-leg by **1.45x** (median 1.8x) with no heap
  penalty. Now on by default; `AL_RUNNER_BC_CONCURRENT_EMIT=0` is the escape hatch, and the
  determinism this costs is stated in the code — object arrival order, hence the emitted
  assembly's member layout, is no longer fixed.
- **Parallelise the AL source-tree parse and the declared-object census** (`128f3a85`,
  `13607795`). Registering the source tree was the largest wholly serial pass left — 11.5 s for
  7,364 files, CPU-bound in the AL parser. Only the parse moves off the calling thread; the eight
  metadata extractors still run serially in file order, so every de-dup and last-writer-wins rule
  sees the identical sequence. Batched at 256 files, so the extra live set is bounded by the batch
  rather than by the tree.
- **Parallelise the Roslyn half** (`7d36c59c`): per-source parse inside a `Parallel.For` with
  `DocumentationMode.None`, one `MetadataReference` per (path, mtime, length) shared process-wide
  (~80 unchanging assemblies were being re-indexed per app group *and* on every watch cycle), and
  `ApplyPolyfillRedirects` in one left-to-right walk instead of 35 `String.Replace` sweeps per
  source. Roslyn moves to 5.6.0 with `LanguageVersion` pinned explicitly — C# 14 made `field` a
  contextual keyword inside accessor bodies, exactly the kind of identifier an AL-to-C# emitter
  produces.
- **One `.app` read answers both metadata questions, and the package scan fans out**
  (`07ba0fc3`). `ReadManifest` and `HasSymbolReference` were answered from the same central
  directory but asked separately: 226 reads per scan of the 113-package / 138 MB platform-apps
  directory. `HasSymbolReference` also stopped pulling the whole package into a `byte[]` to look
  for one entry — per read of `Microsoft_Base Application`, 439 MB allocated before, 45 MB after.
  That is what makes the scan safe to parallelise.
- **Release BC's compilation before the Roslyn compile** (`ea2f31d4`). The bound AL compilation
  and Roslyn's compilation of the C# it produced are consecutive, not concurrent — but the delta
  baseline held the first reachable through `BcCompiler.LastCompilation` for the whole of the
  second, doubling the peak on the phase that already sets it. The baseline is now built as plain
  data immediately after the emit; only the *write* stays after the load, which is all the "a
  rejected candidate must not become a cache entry" invariant ever needed.
- **Walk the object-reference graph in parallel** (`e4d5ab62`). The most expensive step of
  building a delta baseline — 43–53 s of a cold npcore cycle, 11–13 % of the whole run, against
  4–700 ms for the other four phases combined. Now 11–14 s. Bounded to the core count, because
  each in-flight group holds a semantic model and that file's bound bodies.
- **Drop the emit-phase deadline and `AL_RUNNER_EMIT_TIMEOUT_SEC`** (`3a3c78d4`) — the change that
  decides whether an app this size runs at all. How long an emit takes is a function of app size
  and host speed, and the runner can predict neither: npcore's Application group emits in 89 s on
  an idle machine and 333 s on a loaded one, so any fixed budget either aborts a legitimate
  compile or is too loose to catch a real hang. Upstream's budget is a flat **120 s with no
  `--watch` waiver** (`Program.cs:1462`); measured on this corpus, an unmodified upstream `main`
  ends every cycle — cold and warm alike — in `EMIT-TIMEOUT after 120s`, and because the cold
  compile never finishes it never records a baseline for the next cycle to delta against either.
  Worse, the timeout *abandoned* the wait without cancelling: a "timed-out" emit kept burning
  cores, holding its bound compilation alive while the next app group parsed, so the cycle after
  it ran on a machine still working on the previous one.
- **`--no-cache` now disables every on-disk cache, not just `al-out`** (`113e6331`). It used to
  leave compiled dependency DLLs, the Cecil-rewritten `Ncl`, parsed `.app` symbol tables,
  extracted R2R chunks and the manifest index in place — worth tens of seconds in exactly the
  situation the flag is reached for. It redirects to a throwaway per-run directory rather than
  deleting anything, so it cannot sabotage a concurrent run.
- **Two untested concurrency escape hatches are gone** (`043b8e4f`), `FileOf` is indexed instead
  of scanned per key (`1450482a`), and the phase log reports a **real peak RSS on macOS** instead
  of a silent zero (`e8d81026`) — which is what made "which phase costs seconds" and "which phase
  holds gigabytes" two separately answerable questions.

### Watch mode delta compilation for iterative file changes

This is not a set of optimisations on upstream's watch loop — it is a different architecture.

Upstream keeps a per-`BcCompiler`-instance, in-memory dictionary as its baseline
(`BcCompiler.Incremental.cs`, `TryEmitIncremental`). On its happy path it swaps the changed
object's generated C# into the baseline's C# for **every** object and hands that whole union to
Roslyn, producing a new whole-module assembly per save. Its happy path is narrow: it falls back
to a full whole-module compile for an added, removed or renamed file, an id-less object kind, a
file declaring anything other than exactly one object, a changed id or name, and — because the
baseline lives only in memory — **the first cycle of every process**.

This fork keeps a persistent per-app **workspace** (`AlRunner/Rad/`, 4,936 lines), compiles only
the changed objects through Microsoft's own `Compilation.CreateForRad`, and loads the result as a
small **overlay assembly beside** the generations already loaded. On a delta cycle the module is
never rebuilt and never reloaded.

```text
 editor     WatchSource     RadWorkspace      CreateForRad       Roslyn        CLR
    │            │                │                 │               │           │
    │ save BloomFilter.Codeunit.al│                 │               │           │
    ├────────────►                │                 │               │           │
    │            ├─ queue the path, then wait for QUIESCENCE —      │           │
    │            │  not a fixed sleep, so a 12-file branch          │           │
    │            │  switch is ONE cycle, not two    │               │           │
    │            │ drain changed paths              │               │           │
    │            ├────────────────►                 │               │           │
    │            │                ├─ HashSourceTree — SHA-256 per file (0.14 s / 7k)
    │            │                │  DiffFiles → 1 file moved   (none → NO COMPILE)
    │            │                │  declaration probe over the CHANGED FILE ONLY
    │            │                │    → RadObjectKey(Codeunit, 6151273)        │
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
    │            │                │                 │               │ overlay assembly, 27 KB → gN
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
previous generations, not instead of them: `Rad/AlObjectResolution` resolves each AL object to the
generation that owns it in O(1) and tombstones the CLR names a delta removed, so there is no
whole-module reload and no bound on how many saves can accumulate. And `Commit` is the only thing
that advances the workspace, running **after** the assembly loads — so an AL diagnostic, a
rejected C# candidate or a failed load leaves the last good baseline exactly where it was, and
the next save re-diffs against it.

Noteworthy changes, in the order they matter:

- **`--watch` is object-granular by default** (`34c4367a`, `3271d56c`). `--rad` is gone; delta
  compilation is what `--watch` does (`AL_RUNNER_RAD=0` remains as a bisect switch). Runtime
  metadata registered by an AL emit — page, report, xmlport and enum-registry writes that happen
  *before* Roslyn and before `Assembly.Load` — is buffered in `RadMetadataCapture` and applied
  only once the generation loads, so a candidate the C# backend rejects cannot leave the live
  runtime describing objects whose code never loaded. Benchmarking it on npcore found two bugs no
  fixture reproduces: seven `profile` objects all keyed `Profile:0` and threw out of the baseline
  snapshot (caught and logged, so npcore silently never had a baseline — every cycle a full
  compile), and stripping a `tableextension` from the packaged baseline made `Rec."<its own
  field>"` fail `AL0132` inside its own trigger.
- **The baseline is persisted beside the cached AL output** (`e866daf1`). This is the fix for a
  trap an in-memory baseline cannot escape: a cache HIT skips Emit+Compile entirely, so there is
  no compile left to build a baseline *from*, and the **first edit of every session pays a full
  compile** just to establish one. Two sidecars (`<key>.rad-symbols.json`,
  `<key>.rad-baseline.json`) carry the `ModuleDefinition`, object map, per-file hashes, one-hop
  reference graph, extension targets and reference signature; hydration re-hashes the tree and
  refuses unless every file matches, parking a reason so the fallback explains itself. Cycle 1 is
  both fast *and* delta-ready.
- **A warm cycle stopped re-parsing the whole source tree** (`af4157c5`). `RecordPatches` is the
  runner's stand-in for BC's metadata service, so AL source is the only description of
  table/page/report/query/xmlport shape it has — and every save cleared its dictionaries and
  re-derived all of them, re-reading and re-parsing 7,339 files to service an edit to one. That
  was the largest single line item in a warm cycle, 5–10x the AL delta emit it exists to serve.
  Each of the eight extractors is split into a pure `Extract*(text)` half and a stateful
  `Apply*(records)` half, memoized on (path, content hash, preprocessor symbols), and the
  unchanged files' records are **replayed** in enumeration order. Replay rather than retraction is
  what makes it tractable: the `tableextension` dictionaries accumulate by base-table name in AL
  declaration order, so one file's contribution genuinely cannot be subtracted. Measured
  **7.12/7.20/6.06 s → 0.15/0.14/0.12 s** across three warm cycles, at 7,339 parses each → 0.
- **Id-less object kinds delta instead of rebuilding the module** (`0acaf95b`, `81ffbbc5`).
  `interface`, `controladdin`, `profile`, `pagecustomization`, `profileextension` and
  `entitlement` have no object id. On NP Retail that was 84 of 7,339 files where any edit — a
  comment included — was a guaranteed whole-module rebuild. `RadObjectKey` now carries a `Name`
  used as the discriminator when the *kind* is id-less. They are binding contracts too, so the
  reverse dependency graph records edges onto them: widening an interface without touching its
  implementer used to report success, emit nothing, and leave the implementer bound to the old
  contract.
- **The rebind is decided member by member** (`b13dc89d`). `changedSurfaces` compared a modified
  codeunit's whole canonicalised symbol, so any edit that touched a codeunit at all re-emitted its
  complete caller set — on NP Retail, one added procedure on `NPR POS Session` cost **313 objects
  and 22–54 s**. It now compares the shell wholesale and `Methods` as a multiset keyed on
  (Name, Id), rebinding only when a member is gone, a member's fingerprint moved, or a member was
  added under a name the object already had. It is deliberately never keyed on "did the id move?"
  — four contract changes leave a member id bit-identical.
- **Bystanders that lose derived surface are rebound** (`5a47c3a2`). A delta strips every modified
  object from the packaged `ModuleDefinition` so the new source binds; what does not survive that
  is surface an *untouched* object holds only because the stripped object exists — a contributed
  field, an implemented interface's conformance, the `Run(Record)` overload `TableNo` confers. A
  pre-emit widening was implemented first and rejected **on measurement**: it took a one-line body
  edit from 1 re-emitted object to 5. The repair now hangs off every point the delta can return an
  AL error, keyed on four structural rules.
- **Cross-app rebinding** (`7253ff3e`, `aff1d10c`). Generated AL calls bake Microsoft's member id,
  which is a hash of the callee's signature — so when app A re-emits a moved surface, every app
  that *calls* it is left executing IL that dispatches A's previous id. Loud when the retired id
  is gone; **completely silent** when it survives, because adding an overload moves which id the
  caller bakes without moving the callee's own. `RadAppCohort` maps compiler `AppId` to workspace
  identity for one bundle, and edges into sibling *source* apps are retained by
  (app identity, `RadObjectKey`).
- **Three states stopped costing a whole-module compile** (`34066117`). The overlay chain used to
  invalidate the workspace at 12 generations, so **every 11th code-producing save rebuilt
  everything** — minutes on a 7,000-object app, at a moment no developer could predict, for memory
  hygiene rather than correctness. A byte-identical `app.json` counted as "not AL source", so a
  branch switch, a checkout, an editor autosave — and on macOS/APFS even reading the tree with
  `File.Copy` — charged the whole bundle a rebuild with nothing edited. And a **duplicate
  declaration** handed the module over on the argument that only the compiler can say which of the
  two is the duplicate; the compiler's answer is always the same (two objects in one app cannot
  share an id or a name), so that bought a diagnostic and nothing else — for the most ordinary way
  a developer starts a new object, copying an existing `.al` file.
- **A file that declares nothing costs nothing** (`81ffbbc5`). Creating an empty `.al` file,
  editing a comment-only one, or deleting it again now compiles nothing at all. What made that
  unsafe was reading "declares nothing" as the *absence* of a symbol, which is equally what an
  unidentifiable declaration looks like; it is read positively off BC's parser instead —
  `ObjectSyntax` is the base type of every AL top-level declaration.
- **One parse per file, and one cycle per bulk change** (`1c665ac1`). `AddSourceDir` handed each
  file's text to eight extractors and each built its own full AL syntax tree: npcore's 7,339 files
  cost ~59,000 parses per warm cycle instead of 7,339 — **29.7 s → 7.0–7.9 s**. The debounce was a
  fixed `Thread.Sleep(250)` after the first watcher event, so a branch switch started a cycle
  against a tree that was part-old and part-new: a 12-file version switch delivered over 1.4 s
  produced two cycles, the first compiling 4 of the 12 files and reporting a failing test from
  source that passes in *both* versions. It is quiescence-based now (`AL_RUNNER_WATCH_QUIET_MS`,
  10 s cap), the notify buffer is 64 KB, and an overflow forces a cycle instead of leaving the
  runner asleep on a changed tree.
- **Two whole-tree questions per cycle are gone** (`89e751e2`). `GetOrderedDepIds` and
  `BundleDeclaresQuery` each feed one consumer, and both consumers sit behind the AL-output cache
  gate — false from warm cycle 2 on, because by then the app owns a loaded generation. Both ran
  ahead of that gate anyway, every cycle: one rebuilt a second `DependencyResolver` index by
  re-reading every `.app` manifest out of its zip, the other read the whole tree to prove an app
  declares no query (12.7 MB on npcore, and the common case).
- **The delta gets the app's file system** (`400af51c`) so `AL0327` "missing file" for a
  `controladdin`'s scripts is answered rather than escalated; **namespace-free packaged binding is
  repaired** (`6c010b0d`); the delta **stops inventing `AL0133`** when the packaged surface will
  not resolve (`1f6f59eb`); and a bundle whose apps would compile under one `AppId` is **refused**
  (`906a6c68`).
- **A warm cycle reports the same run as the cold one** (`2ae4de00`, `38430b38`, `bef948a5`).
  Three defects outside the delta path made the same unedited bundle report different test sets
  cold and warm (npcore: 2317/432/1885 cold, 2314/415/1899 warm): event dispatch armed after the
  install seed, a table publisher's `IncludeSender` argument passed as null, page and xmlport
  metadata cleared by neither branch of the reload, and manual event bindings owned by a
  `SingleInstance` codeunit surviving its reset.
- **The reference-graph walk no longer swallows its own faults** (`4a67a7b2`). A per-node
  `catch { }` was justified as "a malformed node has no useful dependency edge"; measured against
  BC 28.1 that case does not occur — six malformed shapes put 168 nodes through `GetSymbolInfo`
  and got 168 answers. What it did absorb was any fault its own `Parallel.For` introduced, and a
  lost edge is indistinguishable from an object that calls nothing: the symptom is not an error
  but a caller that should have rebound and did not, several cycles later, reported green.
- **Every fallback names its cause where the developer is looking** (`81ffbbc5`, `aff1d10c`). The
  dashboard redirects both console streams while the bundle loop runs, so full-compile and rebind
  reasons were being discarded in the one mode they exist for. They are collected as well as
  logged, and rendered as a panel above the test tree.
- **The suites were made to prove things** (`646d6002` and the `test(rad)` series). Thirty RAD
  tests reported `Passed` while asserting nothing; the cross-app rebind claims, the by-name
  property shapes, the two symbol producers' member-for-member equivalence and the silent same-app
  overload hazard are each pinned by a test verified to fail without its fix.

[`docs/delta-compile.md`](docs/delta-compile.md) is the long form: the funnel a cycle runs, where
a warm cycle's time actually goes, and the cases that are still a full compile (a `dotnet` package
declaration, a manifest edit, a bundle that cannot be kept warm).

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

- `Microsoft.Dynamics.Nav.Ncl.dll` — rewritten once via Cecil at startup and cached at `~/.cache/al-runner/ncl-cecil/<key>.dll`. This is the runtime-engine layer. See `AlRunner/Infrastructure/NclCecilRewrite.cs`.
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
al-runner --package-cache ~/.local/share/al-runner/packages tests/al-language/tests/al-language

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
reproduce (see [`docs/delta-compile.md`](docs/delta-compile.md)). Later cycles hash the
complete `.al` source tree and
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
rebuild: [docs/delta-compile.md](docs/delta-compile.md).

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
| `--isolation codeunit\|test\|disabled` | Test isolation mode. Default `codeunit`. |
| `--watch` | Stay resident with warm dependencies; on `.al` or `app.json` changes, recompile only the AL objects that changed and run **in-process**. Debounces on quiescence (default 250ms of no further event, capped at 10s) so a bulk multi-file rewrite — a branch switch, a rebase, a formatter run — settles before a cycle starts, instead of firing mid-checkout. Tune with `AL_RUNNER_WATCH_QUIET_MS` / `AL_RUNNER_WATCH_MAX_WAIT_MS`. |
| `--server` | Long-running JSON-RPC daemon over stdin/stdout (warm deps → ~19s→~4s/run). See [docs/server-mode.md](docs/server-mode.md). |
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
