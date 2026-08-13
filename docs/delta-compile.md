# Delta compilation (`--watch`)

Delta compilation is what `--watch` does. With a cold output cache, the first watch cycle
performs a normal full compile and records a baseline for each source app. A cache-hit
first cycle loads the cached DLL; the first later edit performs one full-bundle compile to
establish every app's baseline. Subsequent cycles use those baselines when an edit is safe
to load as a small overlay. Other runner modes use the normal compile path.

The AL-output cache serves the FIRST cycle and nothing after it. A cached whole-module DLL
carries no compiler symbol baseline, so it cannot be delta'd against — but starting a watch
on an unchanged tree should still cost a load rather than minutes of compiling, so the cache
answers cycle 1 and the first edit pays for the baseline. Once a generation is loaded the
workspace owns the module: a later cache key that still matches (an `app.json`-only change
does not move it, since the key hashes `.al` sources) must never resurrect the pre-edit DLL
over the running one.

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
| A changed file declares an id-less object (`controladdin`, `profile`, `pagecustomization`, …) | Normal full compile — `RadObjectKey` cannot identify it |
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

## What the baseline contains

`AlRunner/Rad/RadWorkspace.cs` keeps, per app for the lifetime of the watch process:

- the SHA-256 hash of every `.al` file;
- the objects declared by each file;
- the compiler's full-emit symbol baseline (accepted overlays preserve that surface);
- the loaded baseline and overlay assembly generations.

plus the reverse one-hop reference graph and the extension→target edges, both read off
Microsoft's bound semantic models during a full compile.

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
selected. Cold first cycle: **1,206 s**, of which 538 s is the Application's AL emit.

A successful warm cycle reports the changed-object delta and overlay explicitly:

```text
[rad] NP Retail: delta +0 ~1 -0 over 1 changed file(s) → 1 object(s) re-emitted (1063ms)
[rad] NP Retail: overlay NP Retail#rad…g2 — 1 object(s), 28KB (536ms)
[rad] NP Retail Tests: unchanged — reusing the loaded module
```

One edit costs one object, for every object kind and every file operation:

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
| **Id-less object (`interface`, `controladdin`)** | **full compile** | — | — | **761–862 s** |

An id-less object is the one shape that still costs a whole-module rebuild: `RadObjectKey`
is `(Kind, Id)`, and `interface`, `profile`, `controladdin` and `pagecustomization` have no
id — BC reports 0 for all of them, so they cannot be told apart. Keying them by name is the
obvious next step; until then, editing one on an app this size is a cold compile.

### Where a warm cycle's time actually goes

The delta is no longer the cost. A 44 s warm cycle over both apps breaks down as
(`BCCOMPILER_TIMING=1`):

| Phase | Time |
|---|---:|
| `RecordPatches.AddSourceDir` — re-reads and re-parses every `.al` file in both apps | ~26 s |
| Post-registration field-trigger wiring and record prewarm | ~12 s |
| Per-app setup, symbol publish, dependency resolve | ~3.5 s |
| **AL delta emit + C# overlay + load** | **~1.6 s** |
| Whole-tree hashing (7,053 files) | 0.29 s |
| `GetSharedReferences` (warm) | 0.27 s |
| Running the selected tests | ~1.5 s |

So delta compilation is ~4% of the cycle. The next two things worth attacking are the
per-cycle AL source re-parse and the record prewarm — neither of which is compilation, and
both of which are proportional to the whole tree rather than to the edit.

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
| `RadObjectDeltaTests` | One edit → exactly which objects re-emit and which CLR types change owner, for ten object kinds plus schema additions, a rename, a callable-surface change, an id-less fallback, and a rejected or abandoned candidate |
| `RadDeletionDeltaTests` | One deletion → zero objects re-emitted, exactly those object identities removed, exactly those CLR names tombstoned, every survivor still the identical baseline `Type` |
| `RadMetadataDeltaTests` | One edit → exactly one page/report/xmlport/enum metadata entry moves; a deletion drops its entry, on the delta path and on the full-compile fallback; a renamed enumextension leaves one registration; a rejected candidate leaves none behind |
| `RadWatchTwentyObjectTests` | The same claims against the real `--watch` process, via its own `[rad]` log lines, with the AL test outcome proving the new code actually ran |
| `RadIdlessObjectTests` | An app declaring two `profile`s — which both key as `Profile:0` — still gets a baseline, still deltas its ordinary objects, and takes the full-compile path when a profile itself is touched |
| `WatchTests` | Cycle 1 of a watch is served from the AL-output cache; the first edit builds the baseline instead of being served a second time |

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
