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

| site | today | after |
|---|---|---|
| `EnsurePlatformAppsProvisioned` | scans bundle `.alpackages` only → always downloads | scans the provisioned set for the derived `major.minor` first; reuses on a hit |
| startup gate (`--auto-provision` branch) | scans `packageCacheDirs + bundle .alpackages` | folds a matching provisioned set into `packageCacheDirs` before deciding, so the gate is satisfied without downloading and resolution sees the apps |
| gate's post-download re-check | unchanged predicate | unchanged (already correct) |
| `EnsureTestToolkitProvisioned` | `Directory.Exists && any *.app` | the real `TestToolkitPresent` predicate, so a partial download stops reading as a hit |

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

**Integration** — a subprocess run that proves the whole gate reuses instead of resolving:

- child env `HOME` (and `USERPROFILE`) point at a temp dir, so `ArtifactsRoot` relocates
  without an env-override hook the runner does not have
- `--artifact-path <real engine dir>` supplies the engine, so the run gets past selection
- the fixture uses a **fabricated BC version that does not exist on the CDN** (`99.0`), so
  the pre-fix path fails at `ResolveVersion` with
  `could not resolve a full BC artifact version for '99.0'` instead of pulling 100 MB.
  That message *is* the assertion of the bug: reaching the network at all means the
  provisioned dir was ignored.
- post-fix, the reuse line appears, no resolve is attempted, and the run proceeds

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
