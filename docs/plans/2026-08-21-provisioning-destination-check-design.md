# Provisioning: check the destination before downloading

Date: 2026-08-21

## The defect

`--auto-provision` re-downloads the Microsoft platform R2R apps on **every** invocation,
forever, even when a complete R2R set is already sitting in the runner-owned destination
it would download into.

Two independent causes:

1. `EnsurePlatformAppsProvisioned` (`AlRunner/Program.cs`) decides "there is a gap" by
   scanning **only the target bundle's `.alpackages`**. That directory vendors symbol-only
   packages permanently — by design, because the runner must never write into the user's
   project (#1653). So the check is unsatisfiable by construction: it never looks at
   `<artifactsRoot>/<version>/platform-apps`, the place the download actually lands.
   Its sibling `EnsureTestToolkitProvisioned` *does* guard its own destination, so the
   asymmetry is a plain omission rather than a policy.

2. The startup provisioning gate scans `packageCacheDirs + bundleAlpackagesDirs`, and
   `DefaultPackageCacheDirs` composes the provisioned dirs from `SelectedVersion` only.
   A set provisioned at any other version is invisible, so a successfully provisioned
   machine reports the same gap on the next run.

Observed on npcore: two 106 MB downloads in one `--auto-provision` command (the
provisioning driver, then the post-re-exec startup gate), landing in a directory neither
of them subsequently reads.

## The fix

Every site that is about to download first asks whether its **own destination** already
satisfies the requirement, using the same predicate the gate uses.

`ProvisioningCheck.CheckPlatformApps` already returns `Ok` when an R2R copy sits beside a
symbol-only one (`found.Any(f => f.IsR2R)`, covered by
`CheckPlatformApps_BothR2RAndSymbolOnly_IsOk`). So folding the destination into the
scanned set does three jobs with one predicate: it reports the gap truthfully, it makes
the download unnecessary, and it makes the apps visible to dependency resolution.

### Destination discovery happens *before* the CDN resolve

The naive placement — resolve `major.minor` to a full version, compose the destination,
then check it — still costs a CDN index fetch on every warm run, and fails outright
offline (`ResolveVersion` returns null before the cache check is ever reached).

So discovery is a pure filesystem scan keyed on the `major.minor` that the download
*would* target: the highest version-named dir under `<artifactsRoot>` whose name matches
that prefix segment-wise **and** whose `platform-apps` subdir satisfies
`CheckPlatformApps`. No network, works offline, and needs no version policy of its own —
it only asks "is there already a provisioned set for this same `major.minor`?".

This is what keeps the change independent of the version-derivation problems (see
"Explicitly out of scope").

### Call sites

Four, not three — the startup gate's **toolkit** branch has the same hole as its platform
branch: it checks dirs keyed on `SelectedVersion` but downloads to a dir keyed on the
version derived from the platform apps, so those disagree in exactly the common case.

| site | today | after |
|---|---|---|
| `EnsurePlatformAppsProvisioned` | scans bundle `.alpackages` only → always downloads | scans the provisioned set for the derived `major.minor` first; reuses on a hit |
| startup gate, platform branch | scans `packageCacheDirs + bundle .alpackages` | folds a matching provisioned set into `packageCacheDirs` before deciding, so the gate is satisfied without downloading and resolution sees the apps |
| startup gate, toolkit branch | same hole, second artifact set | same fix, via `FindProvisionedTestAppsDir` |
| gate's post-download re-check | unchanged predicate | unchanged (already correct) |
| `EnsureTestToolkitProvisioned` | `Directory.Exists && any *.app` | the real `TestToolkitPresent` predicate, so a partial download stops reading as a hit |

Both gate branches are placed **ahead of the loud `exit 2` bail**, not merely ahead of the
download. A machine holding a complete provisioned set must never be told it has a
provisioning gap — without `--auto-provision` today it exits 2 while owning everything it
needs.

### Reuse is loud

A reuse decision prints one line naming the directory it reused, so "it didn't download"
is observable rather than inferred — and so the integration test has something to assert
on.

## Testing

Two levels, both hermetic and network-free.

**Unit** — the decision is extracted into a pure `ProvisioningCheck` helper taking
`artifactsRootDir` as a parameter (mirroring `PlatformAppsDirFor`), so tests point it at a
temp dir:

- complete R2R set at `<root>/<v>/platform-apps` matching the prefix → **reuse**
- destination absent → download
- destination present but **symbol-only** → download (proves it is not a naive
  `Directory.Exists` check)
- two provisioned versions match the prefix → the highest wins

**Integration** — subprocess runs that prove the real code paths reuse instead of resolving.

All of them relocate the artifacts root with a new env var, **`AL_RUNNER_ARTIFACTS_ROOT`**.
This is a real capability gap the tests exposed rather than test scaffolding: `--artifact-path`
pins *one version's engine dir* and cannot express "the root those version dirs live under",
so the root was previously reachable only by moving `HOME` — which drags every other
home-rooted path along (cache roots, default package caches) and forces the caller to rebuild
the `.local/share/al-runner/artifacts` layout by hand. That hand-spelled path is exactly what
`TestArtifactsGateTests.OnlyTheSharedHelperNamesTheArtifactCachePathsInCode` exists to forbid,
and it duly failed the first attempt. Documented in `--help` under ENVIRONMENT; its resolution
is a pure `internal` helper so both directions are testable without mutating process env,
which would race every other test reading the root.

Every fixture uses a **fabricated BC version that does not exist on the CDN** (`99.0`), so the
pre-fix path fails at `ResolveVersion` with `could not resolve a full BC artifact version`
instead of pulling 100 MB. That message *is* the assertion of the bug: reaching the network at
all means the provisioned dir was ignored.

- **driver path** — `al-runner provision <bundle>`, fully synthetic. A synthetic engine (just
  the six files `Check` looks for) suffices because `provision` returns before anything loads
  the engine, so this needs no BC install and runs anywhere.
- **contrast** — same, with the provisioned set absent: must still reach the CDN. An
  "always reuse" implementation fails here.
- **run path** — the startup gate, with the real engine via `--artifact-path`, asserting the
  reuse line, no resolve, and that the bundle's test actually executed.

`--package-cache <empty dir>` on the run-path test is load-bearing: it *replaces* the default
caches, which stops whichever Microsoft symbol packages happen to be cached on the host from
changing which app the gate reports first — and therefore which `major.minor` the reuse lookup
is keyed on. Without it the result depends on the machine.

Both directions per `tdd.md`: the positive asserts reuse on a satisfied destination; the
negative asserts a symbol-only destination still downloads, so a "always reuse" stub
would fail.

All claims here are runner-specific (cache/provisioning wiring), so nothing belongs in the
upstream `tests/al-language` corpus — see `.claude/rules/bc-behavior-tests-go-upstream.md`.

## Explicitly out of scope

The BC-version derivation for downloads is separately broken — three different derivations
(`EngineMajor` on a `major.0.0.0` stamp, `DeriveProvisionMajorMinor` on
`Issues[0].AppVersion`, `DerivePresentPlatformMajorMinor` on directory-enumeration order),
none anchored on `EngineBuiltVersion()`, so a provisioning pass can target a version the
run will not select. That is a distinct defect and is deliberately untouched here: keying
discovery on the destination the download *would* use makes this change correct regardless
of how the derivation is eventually fixed.
