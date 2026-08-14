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

What is **not** yet handled is the arrival of such a change. One command takes seconds, so the
tree spends that whole window in a mixed state, and the watch loop debounces on a fixed
interval after the first file event rather than waiting for the burst to finish — so a cycle
can start mid-checkout, against a tree that is part old version and part new. That is a
correctness problem, not just a slow one, and it is tracked separately as
[#1904](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/1904). Nothing in the
delta path depends on it being fixed; the change model is computed by re-hashing the tree, so
a mid-burst cycle produces a correct delta of an incorrect tree.

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
| `WatchTests` | Cycle 1 of a watch is served from the AL-output cache; the first edit builds the baseline instead of being served a second time |
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
