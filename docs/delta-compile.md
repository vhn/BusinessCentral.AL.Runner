# Delta compilation (`--watch --rad`)

Delta compilation is opt-in: plain `--watch` keeps the full-reload behaviour, and `--rad`
turns on object-granular compilation. With a cold output cache, the first watch cycle
performs a normal full compile and records a baseline for each source app. A cache-hit
first cycle loads the cached DLL; the first later edit performs one full-bundle compile to
establish every app's baseline. Subsequent cycles use those baselines when an edit is safe
to load as a small overlay. Other runner modes use the normal compile path.

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

Modified and removed objects are stripped from the packaged baseline before
`CreateForRad` binds the new source, and Microsoft's own `WriteSymbolReference` merges the
result back into the previous module definition to produce the next baseline.

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

Generated C# for the changed objects is compiled into a small, uniquely named generation
assembly and loaded beside the current module. `AlRunner/Rad/AlObjectResolution.cs` records
which generation owns each AL object type and tombstones the names a committed deletion
removed, so a still-loaded previous generation cannot answer for a deleted object. A later
full compile replaces the whole generation chain and establishes a fresh baseline.

## Measured body-only case

The optimization was measured on NP Retail (7,053 AL files / 6,949 objects) and its test
app (286 files / 286 objects), watched together on BC 28.1 on a 6-core / 12 GB machine.
The warm cycles below changed the body of one existing codeunit without changing its
callable surface:

| Cycle | Edit | AL emit | C# compile | Test run | Total |
|---|---|---:|---:|---:|---:|
| 1 | Cold baseline, both apps from source | 661.0 s | 591.3 s | 33.3 s | **1285.6 s** |
| 2 | One Application codeunit body | 2.9 s | 0.6 s | 23.0 s | **26.5 s** |
| 3 | One Test-app codeunit body | 2.7 s | 0.4 s | 28.4 s | **31.4 s** |
| 4 | One Application codeunit body | 2.6 s | 0.2 s | 28.6 s | **31.4 s** |

A successful warm cycle reports the changed-object delta and overlay explicitly:

```text
[rad] NP Retail: delta +0 ~1 -0 over 1 changed file(s) → 1 object(s) re-emitted (173ms)
[rad] NP Retail: overlay NP Retail#rad…g2 — 1 object(s), 17KB (192ms)
[rad] NP Retail Tests: unchanged — reusing the loaded module
```

A final Test-app-only run on the completed implementation selected 2,314 npcore tests:
one codeunit re-emitted in 599 ms and compiled as a 162 KB overlay in 361 ms. The warm
cycle spent 1.8 s in AL emit, 0.4 s in C# compile, and 108.3 s running tests (110.4 s
total), with zero bundle compile or execution failures and the same test-result counts
as its baseline cycle.

These timings were measured on the earlier codeunit-body-only implementation, before
additions, deletions and non-codeunit edits joined the delta path. Every cycle still pays
for whole-tree hashing and runs the selected tests.

## What the tests pin

Timings drift with the machine, so the executable contract is stated as identities and
counts instead. All four suites share one real 20-object app,
`AlRunner.Tests/Fixtures/RadTwentyObject`, so "all of it" and "the one that changed" are
different numbers:

| Suite | Claim |
|---|---|
| `RadObjectDeltaTests` | One edit → exactly which objects re-emit and which CLR types change owner, for ten object kinds plus schema additions, a rename, a callable-surface change, an id-less fallback, and a rejected or abandoned candidate |
| `RadDeletionDeltaTests` | One deletion → zero objects re-emitted, exactly those object identities removed, exactly those CLR names tombstoned, every survivor still the identical baseline `Type` |
| `RadMetadataDeltaTests` | One edit → exactly one page/report/xmlport/enum metadata entry moves; a deletion drops its entry; a rejected candidate leaves none behind |
| `RadWatchTwentyObjectTests` | The same claims against the real `--watch --rad` process, via its own `[rad]` log lines, with the AL test outcome proving the new code actually ran |

Known gaps these suites currently fail on, in the order they matter:

1. **Metadata is not dropped on deletion.** A deleted page, report, xmlport or
   enumextension leaves its runtime metadata registered, so BC can still resolve an object
   that no longer exists in the source tree.
2. **Metadata is not transactional.** The AL emitter writes those registries before Roslyn
   runs, so a candidate whose generated C# is rejected still mutates the live runtime.
   `AlRunner/Rad/RadMetadataCapture.cs` exists for this and is not wired up yet.
3. **A dangling reference is rejected the expensive way.** Deleting an object something
   still calls makes BC's RAD emit throw out of code generation instead of reporting the
   binding error, so the runner falls back to a full compile whose emit-retry then excludes
   the caller from the module. Nothing is committed and the retry is stable, but the
   developer is told "1 broken object unrelated to the rest of the module" instead of
   "RAD Perf Service is missing".

## Reloaded dependency tableextensions

Watch reloads preserve the symbol paths for precompiled dependency apps and re-merge their
`tableextension` fields when per-cycle record metadata is rebuilt. A second or later cycle
can therefore resolve fields supplied by a warm precompiled dependency; the former
`extension field ... not found in NCLMetaTable` reload regression is covered by
`RadDeltaWatchTests`.

## Control

`--rad` (with `--watch`) is the only switch: without it, `--watch` compiles and reloads the
whole bundle exactly as before. Eligibility and fallback are otherwise automatic; a change
the delta path cannot classify is never forced through an overlay.
