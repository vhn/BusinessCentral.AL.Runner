# Delta compilation (`--watch`)

Delta compilation is a `--watch` optimization. With a cold output cache, the first watch
cycle performs a normal full compile and records a baseline for each source app. A
cache-hit first cycle loads the cached DLL; the first later edit performs one full-bundle
compile to establish every app's baseline. Subsequent cycles use those baselines when an
edit is safe to load as a small overlay. Other runner modes use the normal compile path.

## Cycle behavior

Every watch cycle computes a content hash for every `.al` file in the app's source tree.
This is whole-tree change detection, not an O(changed-files) operation. It avoids a
compile when files were only touched or rewritten with identical bytes.

After hashing, each app takes one of these paths:

| Change | Compile path |
|---|---|
| No content changed | Reuse the loaded module; do not compile |
| Only existing codeunits changed, with the same callable surface | Re-emit those codeunits with `Compilation.CreateForRad` and compile a C# overlay |
| An object was added or deleted | Normal full compile |
| A table, tableextension, page, report, query, enum, or any other non-codeunit changed | Normal full compile |
| A codeunit's callable or binding-visible surface changed | Normal full compile |
| Dependencies, app identity, version, or preprocessor symbols changed | Normal full compile |

A body-only codeunit edit can change statements, expressions, local implementation, or
comments without changing the callable surface. Procedure signatures, access, subtype,
subscriber metadata, namespace, and other symbol-visible details are structural changes
and therefore refresh the whole module.

If the runner cannot classify or emit an eligible delta safely, it falls back to a normal
full compile. An AL or generated-C# error never advances the last good baseline.

## What the baseline contains

`AlRunner/Rad/RadWorkspace.cs` keeps, per app for the lifetime of the watch process:

- the SHA-256 hash of every `.al` file;
- the objects declared by each file;
- the compiler's full-emit symbol baseline (accepted overlays preserve that surface);
- the loaded baseline and overlay assembly generations.

For an eligible edit, changed codeunits are removed from the old symbol baseline before
`Compilation.CreateForRad` binds their new source. Their generated C# is compiled into a
small, uniquely named generation assembly and loaded beside the current
module. A later full compile replaces that generation chain and establishes a fresh
baseline.

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

These timings demonstrate the existing-codeunit body-edit path only. Additions,
deletions, non-codeunit edits, callable-surface changes, and reference changes deliberately
take the normal full-compile path. Every cycle still pays for whole-tree hashing and runs
the selected tests.

## Reloaded dependency tableextensions

Watch reloads preserve the symbol paths for precompiled dependency apps and re-merge their
`tableextension` fields when per-cycle record metadata is rebuilt. A second or later cycle
can therefore resolve fields supplied by a warm precompiled dependency; the former
`extension field ... not found in NCLMetaTable` reload regression is covered by
`RadDeltaWatchTests`.

## Control

`AL_RUNNER_RAD=0` disables the watch optimization. Eligibility and fallback are otherwise
automatic; structural changes are never forced through an overlay.
