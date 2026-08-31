# AL Runner — Scope Manifest

Authoritative list of what the runner does, what it fakes faithfully, and what
it refuses to run. Error messages from `RunnerOutOfScopeException` reference
section anchors here, so the developer reading test output lands in the right
row of the right table.

This file is contract; see `.claude/rules/loud-failures.md` for the rule that
makes it binding.

The structure is four buckets, in order of decreasing fidelity:

1. **Real code, real path.** Runner provides the surroundings; the BC / ISV
   business logic executes unmodified.
2. **Faithful replacement.** The runner substitutes an in-process implementation
   that the BC code or AL test cannot distinguish from real BC for observable
   purposes.
3. **Out of scope — runner throws.** AL tests that reach these surfaces fail
   loudly with `RunnerOutOfScopeException`. Move the test to a real-service-tier
   test app.
4. **In scope, not yet implemented (TODO).** Placeholder throws today; should
   land in bucket 1 or 2 over time.

Audit status of each existing patch against this manifest is tracked in
`docs/archive/spike-scope-audit.md`.

---

## §1. Real code, real path

The runner loads these unmodified and lets them execute on the real BC code.
The runner's job is to populate enough state around them that they don't NRE.

| Surface | Form | Notes |
|---|---|---|
| **MS-shipped BC DLLs** | `Microsoft.Dynamics.Nav.SystemApplication.dll`, `Microsoft.Dynamics.Nav.BaseApplication.dll`, language packs, etc. | Loaded from `.app` files; R2R-precompiled bodies execute. |
| **ISV-shipped extension DLLs** | Any `.app` the runner is told about via `--package-cache` | Same load path as MS DLLs. |
| **AL business logic compiled in the test run** | The user's `src/` AL that the runner compiles | Cached as `<key>.dll`, can be re-used like MS DLLs across runs. |
| **Posting routines** | `Sales-Post`, `Purch-Post`, `Gen. Jnl.-Post`, `Item Jnl.-Post`, etc. | All real Base App posting logic; runs against in-memory tables (§2). |
| **Validation triggers** | `OnInsert`, `OnModify`, `OnValidate(field)`, `OnDelete`, `OnRename` | **Working as of 2026-05-11.** The Insert/Modify/Delete/Rename trigger bypasses were drained (commits `ae15b158`, `c2df0bcd`, `29b5acc9`); the real Ncl `NavRecord.*Async` bodies dispatch AL `trigger On*()` overrides natively. Recursion guard at 500 frames (`f8367536`) catches recursive triggers per BC's runtime-error contract. |
| **Event subscribers** | `[IntegrationEvent]` / `[BusinessEvent]` + subscribers | `RunEvent` is rewritten to `AlCompat.FireEvent`, real subscriber dispatch. |
| **.NET interop the apps use in-process** | `System.IO.MemoryStream`, `System.Text.Encoding.*`, `System.Text.RegularExpressions.Regex`, in-process `System.Security.Cryptography` primitives | These execute natively, no replacement needed. |
| **Number / string / date primitives** | `Format`, `Evaluate`, `CalcDate`, `Date2DMY`, etc. | All real BC implementations. |

---

## §2. Faithful replacement

The runner provides a substitute that the BC code cannot tell apart from the
real thing for any test that only observes documented BC behaviour.

| Surface | Real BC | Runner replacement | Faithfulness boundary |
|---|---|---|---|
| **Table storage** (record CRUD) | SQL Server | `TempTableDataProvider` in-memory store | Faithful for all functional reads/writes, keys, filters, ranges, modify-in-place. Different on: transaction commit/rollback (no-op), no row locking, no parallel-session isolation. |
| **Metadata system** (`NCLMetaTable`, `NCLMetaField`, `NCLMetaCodeunit`, …) | Loaded from compiled `.app` metadata streams | `NclMetadataCachePopulator` parses AL source, builds equivalent structures via reflection | Faithful for field types, lengths, FieldClass, FlowField CalcFormula, primary keys, tableextension field merging. Boundary: anything the populator hasn't been taught about throws or NREs into the populator's logged-error channel. |
| **Session / company / tenant / user** | Live BC session | Skeleton `NavSession`, `NavCompany`, `NavTenant` we populate with defaults | Faithful for any test that doesn't probe authentication state, license features, or telemetry identity. `UserId()` defaults to `"TESTUSER"`; `CompanyName()` defaults to `"My Company"`, with a matching row in the `Company` virtual table. Neither is currently configurable — open an issue if your workflow needs it. |
| **Permissions** | Permission sets evaluated against entitlements | All-granted `PermissionSet` returned by `NavSession.GetPermissionSet` | Faithful for any test that doesn't probe permission *denial* paths. Tests asserting "access denied" must be excluded or moved to real service tier. |
| **Time / random / GUID** | Real .NET implementations | Same — no replacement | Faithful. |
| **Field caption / table caption / lookup-page IDs** | From metadata + language pack | Table captions and lookup-page IDs come from parsed AL source or registered dependency symbols. Field captions come from parsed table metadata; undeclared captions use the field name, while a missing dependency field name may use a `"FieldNN"` placeholder. | Faithful for metadata the runner can parse; name/id fallbacks remain for metadata a dependency does not expose. |
| **Event publisher → subscriber dispatch** | Service-tier event dispatcher | **Working as of 2026-05-11 (`c4bce11a`, W-8b A-prime).** Discovers `[NavEventSubscriber]`-attributed methods at startup, constructs real `NavEventSubscription` instances, and injects them into each table's `NavTableTriggerEventHandler.eventScopes[evt].registeredSubscriptions`. BC's own `NavEventScope.CheckAndFireTriggerEventsAsync` then dispatches — no JmpHook on the dispatch path (it would have been R2R-inlined and silently bypassed; see `feedback_r2r_inlining_traps.md`). | Faithful for documented event semantics including `var` params and `IncludeSender`. Manual-binding subscribers (`BindSubscription`) still pending. |
| **`Page.Run` / `Page.RunModal` / report `[RequestPageHandler]`** | Real UI page or dialog | Looks up a registered page trap or `[PageHandler]` / `[ModalPageHandler]` / `[RequestPageHandler]` and calls it | Faithful for the handler and `TestPage.Trap()` dispatch contracts that test code relies on; no actual UI is rendered. |
| **`[HttpClientHandler]` dispatch** | Intercepts an AL `HttpClient` request during a test | BC's in-process handler dispatcher supplies the test request and accepts the mocked response | Faithful when the handler returns `false`, meaning the request was handled. Returning `true` requests real delivery, which remains out of scope. |
| **`RecordLink` (table 2000000068)** AL surface: `Rec.AddLink/HasLinks/DeleteLink/DeleteLinks/CopyLinks` | Stored in the platform Record Link table | The unmodified NCL implementation writes to the runner's in-memory Record Link table. | Faithful for AL-observable semantics, including reading the inserted row through `Record 2000000068`; table rows participate in normal test reset and install-baseline handling. |
| **`TaskScheduler` pending-ID lifecycle** | Validates the codeunit ids, stores a task, exposes its lifecycle, and may later execute it | Runner-compiled AL validates codeunit ids against the loaded assembly index, retains a fresh opaque id, and answers `TaskExists`, `CancelTask`, and `SetTaskReady` from a process-local pending set; tasks are never executed | Faithful for opaque-id creation and membership observations within a configured test-isolation boundary. Codeunits known only through app metadata, but not loaded as CLR types, fail validation. The set is non-transactional within a test; readiness, `NotBefore`, company switching, timeout, failure execution, Scheduled Task rows, and background execution are not modelled. The runner deliberately reports `CanCreateTask = false` while allowing unguarded runner-compiled `CreateTask` calls to succeed—a combination real BC cannot produce—so guarded AL skips creation while unguarded AL receives an id. Precompiled apps use BC's untouched async creation path and retain BC's refusal behavior. |
| **Query execution** | SQL projection, joins, grouping, and dataset export | In-memory projection over each table's `TempTableDataProvider`, with managed joins and provisional aggregation | Joins and dataset export are corpus-backed. Aggregate columns (`Sum`, `Count`, `Average`, `Min`, `Max`), aggregate `ColumnFilter`, and `ReverseSign` are implemented but remain provisional until matching corpus tests pass against a real BC service tier. |

---

## §3. Out of scope — runner throws

When AL test code reaches any of these, the runner throws
`RunnerOutOfScopeException(api, reason, anchor)` and the test fails with a
message naming the API and pointing here.

The developer's options: change the test to not depend on the unsupported
surface, or move it to a separate test app that runs against a real BC service
tier with SQL Server.

### §3.1. Email <a id="email"></a>

| API | Reason |
|---|---|
| `Email.Send`, `Email.OpenInEditor`, `Email.Enqueue` | Sending email requires SMTP / Graph API connectivity. Out of process. |
| `SMTP Mail`, `Mail Management.SendMail` | Same. |

### §3.2. External HTTP / Web APIs <a id="external-http"></a>

| API | Reason |
|---|---|
| `HttpClient.Send`, `.Get`, `.Post`, `.Put`, `.Delete`, `.Patch` without a matching `[HttpClientHandler]` | Real HTTP requires a network and an external server. A test handler that returns `false` is dispatched in-process; no handler, or a handler that returns `true` to request fall-through delivery, throws `external-http` instead of opening a connection. |
| OAuth / Azure AD token acquisition | Same — external network. |
| `ExternalBusinessEvent` delivery | BC queues delivery through service-tier infrastructure outside the runner process. Because delivery has no AL-observable result, the runner completes the delivery call without external subscribers. |
| Outbound REST/SOAP consumers | Same. |

### §3.3. Web service publishing <a id="web-services"></a>

| API | Reason |
|---|---|
| OData / SOAP endpoints exposed by pages or codeunits | Requires a web server hosting the endpoints. Out of process. |
| `Web Service Management`, `tenantwebservice` table-driven publishing | Same. |

### §3.4. File / blob storage <a id="file-storage"></a>

| API | Reason |
|---|---|
| `File.Download`, `File.Upload` (browser round-trip) | Browser interaction; no client. |
| `XmlPort.Run(id[, requestWindow[, import[, record]]])` — all four static overloads | Same browser round-trip as `File.Download`/`Upload`, one level down: BC's real `RunXmlPort()` body hard-codes `displayDialog: true` on both the upload and the download branch, so no combination of `requestWindow`/`import`/`record` avoids the client-callback file dialog — `record` only ever narrows `SetTableView`, it never reaches the I/O stream. Do not confuse this with two other xmlport surfaces that are *not* affected by this entry: (1) the **instance** `Export()`/`Import()`/`Run()`/`SetTableView()` methods on an AL xmlport variable, already in scope (§1 — BC's real, unmodified body runs once construction succeeds); (2) the **static** `XmlPort.Export(id, stream, record)` / `XmlPort.Import(id, stream, record)` forms, which take a stream directly and never show a dialog — a separate in-scope-but-not-yet-implemented case, see §4. |
| Azure Blob Storage, Azure Files connectors | External storage. |
| `File Management.BLOBImportFromServerFile` etc. against real filesystems outside the test directory | External filesystem dependency. |

### §3.5. Printing <a id="printing"></a>

| API | Reason |
|---|---|
| `Report.Run(Print, ...)` / `SaveAsPdf` to a real printer or PDF | Requires renderer + driver. Report **callbacks** (`[ReportHandler]`, `[RequestPageHandler]`) fire — see §2. |

### §3.5.1. Report output (layout + request page) <a id="report-rendering"></a>

| API | Reason |
|---|---|
| `Report.SaveAsPdf` / `SaveAsHtml` / `SaveAsExcel` / `SaveAsWord` / `SaveAsDocx` | No layout renderer. Cecil-rewritten to throw `InvalidOperationException("out-of-scope: NavReport.SaveAs* ...")`. Tests `asserterror` + `Assert.ExpectedError('out-of-scope: NavReport.SaveAs')`. |
| `Report.RunRequestPage(...)` | In scope when a test supplies `[RequestPageHandler]`: BC's generated request-page form and handler dispatch run headlessly, and BC's own parameter serializer returns the resulting XML. With no matching handler, the client callback remains out of scope. |
| Static `Report.Run(id, ...)` / `Report.RunModal(id, ...)` | In scope. The runner constructs the report from BC metadata and executes the same lifecycle as an AL report variable. `requestWindow = true` drives the generated request page through `[RequestPageHandler]`; `systemPrinter` cannot provide a physical printer. |

`TestRequestPage.SaveAsXml(parametersFile, datasetFile)` is also in scope. The
request-page control and data-item filter mutations are applied to the live report, then
Microsoft's `ReportResultSetProcessorFactory`, `ReportSaveAsXmlRenderer`,
`NavDataSetBuilder`, and `DataItemIterator` generate both XML files. This supports reports
over the runner's in-memory records, including temporary records, without a database or a
layout renderer. PDF, Word, Excel, HTML, and printer output remain out of scope.

**Layout *selection* is in scope; layout *content* is not.** A report's
`rendering { layout(Name) { Type; MimeType; LayoutFile; … } }` declarations are
captured at compile time and published into the "Report Layout List" system
virtual table (2000000234), which is where BC's own by-name resolution looks. So
`ReportLayoutSelection.SetTempLayoutSelectedName('<LayoutName>')` resolves the
named layout through BC's unmodified code path, and the resolved layout's
`Type` drives the processor fork exactly as on a real tier (an undeclared name
still raises BC's own `NavNCLReportNoLayoutException`). What those rows do **not**
carry is the layout's *bytes*: the media-id columns hold the same empty GUID an
application-provided layout row carries on a real tier, where the bytes are
fetched separately from the published app package
(`ReportLayout.FetchLayoutFromApplication`). The runner has no app package for a
source-compiled report, so a renderer that demands the layout content — including
a custom document merger reading `LayoutData` — gets nothing. Rendering itself is
out of scope; only selection/resolution is supported.

Instance `Run()` / `RunModal()` execute `OnInitReport` → `OnPreReport` → BC's data-item loop
→ `OnPostReport`. The static overloads use the same lifecycle and, when `requestWindow = true`,
insert the generated request page and `[RequestPageHandler]` between `OnInitReport` and
`OnPreReport`. The data-item loop owns `DataItemTableView`, `DataItemLink`, nested items,
per-row `OnAfterGetRecord`, and `CurrReport.Skip` / `Break`. A non-processing report still
attempts layout output after the lifecycle unless `SaveAsXml` selected the dataset renderer.

### §3.5.2. Media image decoding <a id="media-image-decoding"></a>

BC's `NavMediaFactory` normally validates and decodes images through `System.Drawing`.
`NavMediaImage` either retains the source stream (`saveStream = true`) or later encodes the
decoded image. Those Microsoft NST types are retained for ordinary media storage, MIME
handling, and the non-image binary fallback, but their image decoder cannot execute on Linux:
current .NET treats `System.Drawing.Common` as Windows-only and rejects the call before BC can
inspect the image.

The runner therefore has one bounded portable image path: it validates PNG structure, critical
chunk order, and chunk CRCs, then lets BC's own `NavMediaBinaryFile` store the original stream as
`image/png`. Preserving the source bytes follows the same invariant as the NST's
`saveStream = true` branch without fabricating a `System.Drawing.Image`. It does not claim to
implement GDI+ transforms, metadata extraction, dimensions, or thumbnails. Non-image media still
follows BC's own `application/octet-stream` fallback. Other image formats fail with the named
`media-image-decode` out-of-scope error; they are never accepted as images without decoding.
The PNG path validates the container and chunk integrity but does not inflate IDAT data, so it is
not a claim of byte-for-byte GDI+ decoder equivalence for every corrupt pixel stream.

### §3.6. Background jobs / scheduling <a id="jobs"></a>

The narrow §2 replacement retains pending task IDs so creation and membership lifecycle calls
remain coherent. IDs created by a test survive until cancellation or the configured test-isolation
reset. IDs created by install triggers are captured and restored with the install baseline at that
boundary. `SetTaskReady` only confirms membership: it never dispatches a task or models `NotBefore`.

| API | Reason |
|---|---|
| Job Queue Entry execution against a scheduler | No scheduler — job-queue rows are not picked up and run. |
| Task or failure-codeunit execution, timing, and Scheduled Task table integration | The pending-ID replacement has no dispatcher, clock, background session, or platform task table. |
| `TaskScheduler.CreateTask` from precompiled MS / ISV apps | Precompiled callers use BC's untouched async creation path, which retains BC's own refusal behavior. The pending-ID creation seam applies to AL compiled by this runner. |
| `IsolatedStorage` scoped to *real* session/user/company beyond the runner's flat in-memory bag | Possible TODO if needed; currently a single in-memory bag. |

### §3.7. Cryptography requiring external KMS / certificates <a id="crypto-external"></a>

| API | Reason |
|---|---|
| Key Vault integration | External KMS. |
| Certificate validation against a real cert store / CA | External infrastructure. |
| In-process primitives (hashing, AES, etc.) | **In scope** — those are §1, run natively against .NET. |

### §3.8. Real licensing / entitlements <a id="licensing"></a>

| API | Reason |
|---|---|
| `Session.IsLicensed`, license-file validation | No license system. Replacement returns "all granted" (§2), but tests probing denial paths fall back to out-of-scope. |

### §3.9. Parallel session contract <a id="parallel-sessions"></a>

| API | Reason |
|---|---|
| `StartSession`, `IsSessionActive`, session timeout / cancellation across processes | Runner runs everything in one process, inline. Logic tests work; contract tests don't (see `docs/limitations.md#no-parallel-session-execution`). |

### §3.10. Transaction semantics <a id="transactions"></a>

| API | Reason |
|---|---|
| `Commit`, `Rollback` as real boundaries | No transactions. `Commit` is a no-op. Tests asserting on commit boundaries must move to real service tier. |

### §3.11. Page rendering / client interaction <a id="ui"></a>

| API | Reason |
|---|---|
| Unhandled `Page.Run` (non-modal), `controladdin`, `usercontrol`, profiles | Requires BC client. Handler- or trap-backed `Page.Run` and UI dialog **callbacks** (`[MessageHandler]`, `[ConfirmHandler]`, …) are in scope under §2; the UI itself isn't. |

### §3.12. Debugger <a id="debugger"></a>

| API | Reason |
|---|---|
| `Debugger.Attach`, `Break`, `StepInto`, etc. | No debug loop. See `docs/limitations.md#no-debugger-infrastructure`. |

### §3.13. .NET interop (DotNet AL type) <a id="dotnet-interop"></a>

| API | Reason |
|---|---|
| `assembly_declaration`, `dotnet_declaration`, `DotNet` variables, `GetDotNetType` | Requires BC service tier's type-resolution. In-process .NET interop the apps themselves use is **in scope** (§1) — only the AL `DotNet` surface is out. |

---

## §4. In scope, not yet implemented (TODO)

These are surfaces we intend to support but haven't built yet. They throw
`RunnerOutOfScopeException` with reason `not-yet-implemented` so a developer
hitting them files a runner-gap issue rather than silently passing.

| Surface | Plan | Tracking |
|---|---|---|
| `NavReport.RunReportAsync` faithful replacement | EventPipe + handler dispatch (Spike 4 mechanism proven, deployment pending) | HANDOFF §6 Tier 1C |
| `NavReport.SaveAsAsync` faithful replacement | Same | HANDOFF §6 Tier 1C |
| `NavForm.GetAutoFormatStringAsync` | Investigate Option-C first | HANDOFF §6 Tier 1C |
| `RecordImplementation.CalcFieldsAsync` residual FlowField shapes | Extend populator (basic CalcFormula + unquoted-name path landed `d337c849`, `ff0d83e7`) | residual edge cases |
| `ALDatabase.AL*` cluster | Now throws `out-of-scope/ALDatabase.*` per scope.md §3 (per-method clean classification needs investigation — two Sonnet attempts segfaulted, see `feedback_aldatabase_hard.md`) | classification investigation |
| `NavApplicationObjectBaseHandle\`1.get_Target` tableId=0 path | Synthetic empty record for default-variant case. Today throws `out-of-scope/NavRecord.CloneForVariant (default-variant tableId=0)` via `8efcc462`. | HANDOFF §6 Tier 1B |
| `NavRecord..ctor` for remaining system tables (excluding Company and RecordLink — see §2) | BcAppFallback for SystemPackage AL source lands the metadata; real BC ctor bodies may need further skeleton DataAccessSource wiring | HANDOFF §6 Tier 1B |
| AL Runner Config codeunit `131100` | v2 equivalent of v1's `MockSession` routing | HANDOFF §6 Tier 2 |
| `FilterGroup(n)` scoped filter groups | Track group state on Record | known gap |
| Manual-binding event subscribers (`BindSubscription`) | Auto-binding subscribers work as of `c4bce11a`; manual-binding wiring deferred | follow-on to W-8b |
| `NavMethodScope` recursion-depth threshold (currently hard-coded 500) | Make configurable / verify matches real BC's limit precisely | follow-on to `f8367536` |
| Static `XmlPort.Export(id, stream, record)` / `XmlPort.Import(id, stream, record)` — in-memory xmlport serialization | No dialog involved (unlike static `Run`, §3.4) — a faithful in-process implementation is plausible; not yet built. `NavXmlPort_StaticExport`/`StaticImport` in `XmlPortPatches.cs` throw `not-yet-implemented` today. | HANDOFF.md / SCOPE-AUDIT.md |

---

## How to read this from a failing test

When you see a test fail with `RunnerOutOfScopeException`:

```
NavNCLDialogException: RunnerOutOfScopeException: Email.Send is out of scope.
Reason: external-smtp. See docs/scope.md#email.
```

1. Open `docs/scope.md` at the anchor.
2. The row tells you which bucket the API is in (3.x permanent, or 4 TODO).
3. If §3 — move the test to a real-service-tier test app, or refactor to inject
   an AL interface and pass a fake from the test project.
4. If §4 — file a runner-gap issue and add a `known-gaps-<area>.json` entry
   in `tests/expectations/` linking it (see `docs/expectations.md`).

## Sister docs

- `.claude/rules/loud-failures.md` — the rule.
- `docs/limitations.md` — user-facing version with patterns + workarounds.
- `docs/archive/spike-scope-audit.md` — audit table of each existing patch vs this manifest.
- `docs/archive/spike-handoff.md` — what's prioritized for the §4 list.
