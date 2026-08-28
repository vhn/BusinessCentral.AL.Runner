# Changelog

All notable changes to this project are documented here. Format based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning follows
[SemVer](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **server:** per-statement hit counts + position table (coverage:true)
- **dap:** real step granularity for next/stepIn/stepOut
- **dap:** restore --dap breakpoint debugging on v2 (slice 1 of #1642)
- **server:** implement --capture-values on execute via NavMethodScope.Exit()
- **pack:** select and shadow-swap a per-BC-minor engine variant at startup
- **tdd:** support --tdd together with --watch
- **cli:** --tdd — infer and generate missing members instead of only refusing
- **cli:** --tdd — report excluded objects' tests as failed, not vanished
- **cli:** print the informational version, and stop overselling --print-cache-key
- **reporter:** name this bundle's provisioning gaps again in the run summary
- **compile:** opt-in concurrent BC emit behind AL_RUNNER_BC_CONCURRENT_EMIT
- **cache:** --no-cache disables every on-disk cache, not just al-out
- **rad:** give the RAD compilation the app's file system
- **server:** compile and run inline AL code from the execute command
- **coverage:** --coverage via BC's own StmtHit instrumentation
- **cache:** persist the RAD baseline beside the cached AL output
- --count-baseline so a shrunken run can no longer report green
- **watch:** delta the remaining full-compile triggers, and say why when one fires
- **rad:** delta id-less AL objects instead of rebuilding the module
- **watch:** make --watch object-granular by default, and make it hold up on npcore
- **server:** add the cancel command — cooperative mid-run cancellation
- **rad:** object-granular --watch delta compilation plus its proportionality suite
- **server:** populate protocol-v2 errorKind + stackFrames on streamed test events
- **metadata:** populate the Table Metadata virtual table; fix the docs-only CI bypass
- **parser:** move the remaining six AL parsers onto BC's syntax tree
- **parser:** parse AL tables with BC's own syntax tree, not regexes
- **server:** stream runTests as protocol-v2 NDJSON (second slice of #1641)
- **win32stubs:** ship a prebuilt libwin32_stubs.so so Linux needs no C compiler
- **cli:** restore --test-timeout and clarify the --run -> --test/--filter redesign
- **windows:** real Windows support via VirtualProtect
- auto-inject stub usercontrol + ControlAddin for stripped dep pages
- add --fail-on-stub flag to catch blank-shell stub and no-op test passes (issue #1519)
- support custom preprocessor symbols in compile-dep/extract-deps
- extract-deps --packages <dir> auto-discovers .app dep sources
- extract-deps — reachability-based dependency slicing from .app artifacts
- sweep 160 not-tested overloads to covered (issue #1400)
- support app.json feature flags (NoImplicitWith, NoPromotedActionProperties, TranslationFile)
- Enhance AL diagnostic formatting with source filename support
- AL test count badge + remove redundant perf check step
- add Version.Create(Text) 1-arg ALCreate overload — closes #1296
- add missing ALFieldError, AutoFormat, EnsureGlobalVars on Record types
- run telemetry triage on hourly schedule
- AL-level call stacks with procedure names and line numbers
- --generate-stubs uses BC symbol table for platform codeunits
- in-memory Query mock (Open/Read/Close, filters)
- add AL stub for Library - Utility (codeunit 131003) — closes partial #1139
- add Report.Run 4-arg overload (StaticRun)
- add GetPosition(Boolean) overload to MockRecordHandle
- add CopyArray 3-arg overload
- add MockVariant.Clear() method
- BreakpointManager — DAP debugger runtime core
- symbol-table auto-stubs, per-test timeout, MaxStrLen fix, enumextension dispatch — 1.0.19
- rich auto-stubs — generate methods from .app SymbolReference.json
- SingleInstance codeunit support + auto-stub transparency
- --compile-dep and --dep-dlls for dependency execution
- add AL stub for codeunit 132250 (Library - Test Initialize)
- add AL stub for codeunit 130440 (Library - Random)
- add AL stub for codeunit 130500 (Any) — pseudo-random test data generator
- auto-stub test-toolkit codeunits (130000-139999) so Codeunit.Run no-ops
- graceful codeunit-not-found fallback + --init-events lifecycle flag
- fix 3 runtime-api gaps — HttpHeaders.ContainsSecret string overload, FilterPageBuilder.GetView(caption,bool), AlScope.Parent dynamic?
- fix NavScope using-blocks, AlScope Bind/Unbind, NavApp string overloads — close #1085 #1090 #1107
- ALTestFieldSafe(object,bool) and ALFind(NavText/string,bool) overloads — fix #1108 #1109
- fix NavList<NavText>→MockArray and MockInStream→string type gaps (#1080, #1081)
- add AlScope.Parent static stub to fix CS0117 — issue #1092
- TestField(Field, Value, ErrorInfo) — add ErrorInfo overloads for ALTestFieldSafe
- add missing ALGetResource(4-arg) and StaticSaveAs(5/6-arg) overloads — closes #1087 #1088
- enrich CompilationGap telemetry with triggering AL source line
- stub CurrReport.Preview / PreviewCanPrint as no-op — issue #1055
- stub Database.KeyGroupEnabled/KeyGroupDisable/KeyGroupEnable — issue #1054
- add GetPart(partHash) to page classes for subpage access
- add FlowTemplateGallery customaction test and coverage — issue #1044
- add Report.RunModal 2/3/4-arg static overloads
- enrich telemetry with member names, AL line hints, and test identity — issue #1039
- auto-detect .alpackages folder when --packages not specified — issue #1033
- add 4-arg ALUploadIntoStream overload — issue #1021
- add missing JsonObject/JsonArray overloads — issue #1025
- XmlDeclaration navigation/tree-mutation methods — issue #779
- ReportInstance.CreateTotals — add 0-arg and N-arg no-op stubs
- MockInterfaceHandle IsInterfaceOfType and AsInterfaceOfType
- MockArray<T> 2D/3D indexers and GetSubArray — issue #974
- MockRecordRef.ALFilterGroup — delegate to MockRecordHandle
- MockRecordHandle.ALSetSelectionFilter — implement Record.SetSelectionFilter
- MockVersion equality + comparison operators
- Variant.IsXxx XML type proof tests + fix IsFilterPageBuilder
- Add Clear() to MockReportHandle and MockXmlPortHandle
- ALFindSet/ALInsert 3-arg overloads — issue #979
- TextBuilder.Length setter — truncate via assignment
- MockDialog.Clear() — fix CS1061 for AL Clear(dlg)
- ReportInstance remaining — Execute, Print, PageNo, ValidateAndPrepareLayout
- Debugger.IsActive/Activate/Deactivate stubs
- ErrorInfo.Create, Message, ErrorType — issue #215
- TestPage.RunPageBackgroundTask and TestHttpRequestMessage.QueryParameters
- Media.FindOrphans and MediaSet.FindOrphans — return empty list
- ReportInstance handle methods — Quit, PrintOnlyIfDetail, SaveAs*, layout, run
- Media type — HasValue, ImportFile, ExportFile proving tests
- Table — Delete, Find, Get, Insert, Modify, Reset, SetFilter, SetRange coverage
- MediaSet mock — Count, Insert, Remove, Item, MediaId, ImportFile, ExportFile
- TestPage — Close, Caption, ValidationErrorCount, First, Last, Previous, Expand, IsExpanded, View, No, Yes
- HttpRequestMessage — SetCookie, GetCookie, RemoveCookie, GetCookieNames, SetSecretRequestUri, GetSecretRequestUri
- TestPage.GoToKey and GoToRecord — real record lookup
- TestHttpRequestMessage — Path, RequestType, HasSecretUri
- strip fileupload() action declarations — issue #448
- MediaSet.FindOrphans — stub returning empty list
- TestPage.New() and TestPage.Edit() — set editable state
- strip addfirst(GroupName; Fields) from tableextension fieldgroups — issue #816
- TestPage.Trap — register trap so next RunModal on same page returns OK (issue #869)
- Variant.IsXml*/IsInStream/IsOutStream/IsDictionary type-check methods
- implement System.GetLastErrorObject (issue #853)
- [ErrorBehavior(ErrorBehavior::Collect)] on test procedures — issue #858
- TestPage.OpenEdit/OpenView/OpenNew — set editable state (issue #866)
- prove Table.CalcFields FlowField evaluation with 9 end-to-end tests (issue #864)
- Database.CompanyName — promote stub → covered
- Database.SessionId and HasTableConnection — proving tests (issue #843)
- SessionSettings.ProfileAppId/ProfileSystemScope tests + fix bucket-1 ID collision (issue #846)
- CompanyProperty — configurable DisplayName/UrlName/ID with proving tests (issue #842)
- page background task API stubs — issue #837
- TextConst.IndexOfAny — both overloads proven via Label tests
- Page.PromptMode — MockCurrPage and MockFormHandle stubs
- dispatch TestPage custom action OnAction triggers (issue #832)
- JsonObject — GetTime/Duration/Option/Byte/BigInteger/Values/Path + ReadFromYaml/WriteToYaml stubs
- XmlProcessingInstruction — WriteTo, SelectNodes, SelectSingleNode
- report dataitem triggers — OnPreDataItem/OnAfterGetRecord/OnPostDataItem execute on Run() (issue #833)
- XmlProcessingInstruction remaining methods coverage (issue #780)
- Table record-link methods — AddLink, CopyLinks, DeleteLink, DeleteLinks, HasLinks
- XmlDocument node manipulation — Remove, ReplaceWith, AddAfterSelf, AddBeforeSelf
- Table extra methods — ChangeCompany, GetAscending, Truncate, etc. (issue #812)
- misc stubs — XmlNode.IsXmlDocumentType, NavApp.GetArchiveRecordRef/GetResource, RecordId.GetRecord
- TestRequestPage — GoToKey, GoToRecord, IsExpanded coverage
- FieldRef.Name/Caption — tableextension fields and FieldIndex ordinal fix
- Media — ImportFile, ImportStream, ExportFile, ExportStream, MediaId, FindOrphans
- System misc stubs — Format, GetUrl, GetDocumentUrl, CaptionClassTranslate, CodeCoverage*, ImportObjects, ExportObjects, ImportStreamWithUrlAccess
- WebServiceActionContext mock — 7 methods (GetObjectId/Type/ResultCode, SetObjectId/Type/ResultCode, AddEntityKey)
- XmlElement remaining methods coverage — issue #802
- Table metadata stubs — FieldName, CurrentCompany, RecordLevelLocking + confirm existing
- XmlDocument — Create, Add, GetRoot, ReadFrom, WriteTo, GetChildNodes, SelectNodes, GetDeclaration, RemoveNodes
- System session/env stubs — GuiAllowed, IsServiceTier, ApplicationPath, TemporaryPath, WorkDate, GlobalLanguage, WindowsLanguage, RoundDateTime, IsNull, Hyperlink
- CompressArray/CopyArray implementation + System built-ins coverage (issue #793)
- System encryption stubs and date/variant utilities
- RecordId.TableNo, List.Sort, XmlNameTable.Add/Get
- System error-handling utilities — GetLastErrorText/Code/CallStack, ClearLastError, GetCollectedErrors, ClearCollectedErrors, HasCollectedErrors, IsCollectingErrors
- RecordRef.FieldExist + RecordLevelLocking — metadata-aware + stub
- JsonToken.Path, NumberSequence.Range, Time.Millisecond, XmlNodeList.Get, XmlReadOptions/XmlWriteOptions.PreserveWhitespace
- XmlAttribute nav/namespace methods coverage (issue #772)
- NavApp archive stubs — GetArchiveVersion/LoadPackageData/RestoreArchiveData/DeleteArchiveData
- HttpContent.Clear/IsSecretContent + HttpResponseMessage cookie/environment stubs
- Session extended — 9 more methods stubbed + tested
- XmlDocumentType — WriteTo, AsXmlNode, GetDocument, GetParent, Remove, ReplaceWith, SelectNodes, SelectSingleNode, AddAfterSelf, AddBeforeSelf
- JsonArray.Clone() and AsToken() — proving tests
- JsonObject — GetChar, GetDate, GetDateTime, WriteWithSecretsTo
- Version — Create, Major, Minor, Build, Revision, ToText
- ModuleDependencyInfo — Id, Name, Publisher
- TestAction — Enabled, Visible, Invoke; ProductName — Full, Marketing, Short
- TestFilter stubs — Ascending, CurrentKey, SetCurrentKey, GetFilter, SetFilter
- SessionInformation — SqlRowsRead/SqlStatementsExecuted/Callstack stubs
- HttpHeaders — Clear, Keys, TryAddWithoutValidation, ContainsSecret, GetSecretValues
- TestHttpResponseMessage — Content, Headers, HttpStatusCode, IsSuccessfulRequest, ReasonPhrase, IsBlockedByEnvironment
- HttpClient configuration methods — SetBaseAddress, GetBaseAddress, Clear, UseResponseCookies, UseServerCertificateValidation, UseWindowsAuthentication, AddCertificate
- RequestPage CurrPage stubs — Caption, LookupMode, ObjectId, SetSelectionFilter (issue #719)
- RecordRef stubs — IsDirty, LoadFields, CopyLinks, ReadConsistency, SecurityFiltering, Truncate
- XmlportInstance — Export/Import/Run/Break/Skip/Quit and property stubs
- static Page.* method stubs — Run, Update, ObjectId, LookupMode and 11 more
- XmlNamespaceManager — 8 methods covered
- TextBuilder — Insert, Remove, Replace, Length, Clear, Capacity, MaxCapacity, EnsureCapacity
- SessionSettings — in-memory mock + proving tests
- stub 18 Report.* static methods
- XmlComment — 9 remaining methods covered
- XmlText methods — Create, Value, AsXmlNode, WriteTo, navigation
- XmlDocumentType — GetPublicId/SystemId/InternalSubset, Set*, WriteTo, AsXmlNode, navigation (17 methods)
- XmlCData — 12 methods covered, fix WriteTo interception for XML types
- Session — CurrentClientType / CurrentExecutionMode / DefaultClientType stubs
- TestRequestPage methods — 21 stubs
- TextConst string method tests
- TestField methods — AsBoolean, AsInteger, AsDate, AsTime, AssertEquals and 10 more
- Variant.Is* type-checking for JSON, Notification, TextBuilder, List
- TestPart — 20 part-navigation methods
- Table.AddLoadFields / Table.AreFieldsLoaded — standalone no-ops
- FilterPageBuilder mock — AddTable/Count/Name/GetView/SetView/RunModal/PageCaption
- File I/O stub — MockFile (26 methods)
- JsonObject methods — Add/Get/Contains/Keys/Remove/Replace/Clone/AsToken/GetText/GetInteger/GetDecimal/GetObject
- NavApp.GetResourceAsText/GetResourceAsJson/ListResources — no-op stubs
- ClearAll() resets codeunit globals + Clear proving tests
- FieldRef.OptionMembers and FieldRef.IsOptimizedForTextSearch
- JsonToken type-checking — IsArray/IsObject/IsValue/AsArray/AsObject/AsValue/Clone/Path
- ModuleInfo properties — AppVersion/DataVersion/Id/PackageId/Name/Publisher/Dependencies
- FileUpload.FileName and CreateInStream stubs (MockFileUpload)
- System.Abs / Power / Round — proving tests + 1-arg Round fix
- FieldRef.GetEnumValueCaptionFromOrdinalValue / FromOrdinalValue
- NavApp.IsInstalling / IsUnlicensed / IsEntitled stubs
- JsonValue typed conversions — AsBoolean/AsInteger/AsText/AsDecimal/IsNull/SetValue
- InStream.EOS — end-of-stream detection
- ErrorInfo.Title — get/set title on error info
- ErrorInfo.Verbosity — test coverage for verbosity get/set
- IsolatedStorage.SetEncrypted — transparent-crypto stub
- System.ArrayLen — per-dimension MockArray fix
- Integer.ToText — test coverage for integer-to-text conversion
- strip Database.SetUserPassword in rewriter — no-op standalone
- ErrorInfo.PageNo — get/set page number on error info
- ErrorInfo.DataClassification — get/set data classification on error info
- ErrorInfo.SystemId — get/set system ID on error info
- ErrorInfo.AddNavigationAction — strip call in rewriter (no-op standalone)
- ErrorInfo.AddAction — strip-entire-call no-op
- visible_attribute_condition — conditional Visible on page controls
- add tooltip_control_property test suite
- List.Sort() — proving tests for integer and text lists
- Cookie property mocks (Name, Value, Domain, Path, Secure, HttpOnly, Expires)
- JsonObject.GetBoolean — typed boolean accessor for JSON objects
- Database.UnregisterTableConnection — strip-entire-call no-op
- Xmlport.Run — static no-op stub via MockXmlPortHandle.StaticRun
- TestRequestPage.Editable — field edit-mode check
- Dialog.StrMenu — static MockDialog stubs returning defaultNo
- Dialog.HideSubsequentDialogs / Dialog.LogInternalError stubs
- XmlPort.Import(portNumber, fileName) no-op stub
- Database.SetDefaultTableConnection — strip-entire-call no-op
- XmlDocumentType.Create and GetName coverage
- Database.RegisterTableConnection — strip-entire-call no-op
- implement Record.SetAutoCalcFields
- Database.DataFileInformation no-op stub
- Database.ExportData no-op stub
- Database.ImportData no-op stub
- Database.GetDefaultTableConnection — empty-string stub
- Database.CopyCompany no-op stub
- Database.ChangeUserPassword — strip-entire-call no-op stub
- System.Sleep no-op stub — add proving tests
- Database.CheckLicenseFile no-op stub
- System.Evaluate — built-in Evaluate(var Variable; Text) function
- Database.AlterKey no-op stub
- CodeunitInstance.Run — preserve OnRun state on the handle's instance
- DataTransfer.UpdateAuditFields — round-trip auto-property on MockDataTransfer
- Database.SerialNumber / TenantId / ServiceInstanceId — fixed stubs
- Database.UserSecurityId stub — fixed non-null Guid stable across reads
- DateTime.ToText / Decimal.ToText — redirect session-free via AlCompat.Format
- Database.LastUsedRowVersion / MinimumActiveRowVersion stubs
- Database.LockTimeout / LockTimeoutDuration property-get stubs
- Record.Count and IsEmpty proving tests
- addlast_fieldgroup_modification coverage — tableextension addlast() in fieldgroups
- mark Session.BindSubscription / Session.UnbindSubscription as covered
- case statement on Code[N] type — NavCode equality comparison fix
- xmlport_attribute coverage — XmlPort with textattribute/fieldattribute compiles
- separator_action — pages with separator() actions compile correctly
- part/systempart section in pages — compilation support
- systemaction_declaration compilation support
- actionref_declaration — pages with actionref sections compile correctly
- Enum::"T".FromInteger(I) — type-qualifier enum conversion from integer
- pagecustomization_declaration compilation support
- Record.GetView/SetView roundtrip + GetFilter/CopyFilter/FilterGroup
- Report OnPreReport/OnPostReport trigger coverage
- Text built-ins — PadStr negative length support + proving tests
- SecretText.IsEmpty / Unwrap / SecretStrSubstNo
- Record.GetPosition / SetPosition — save and restore cursor position
- LockTable(Wait: Boolean) overload — no-op stub
- implement SelectStr, IncStr, ConvertStr, CopyStr text builtins
- Database.CurrentTransactionType() stub — returns TransactionType::Update
- List.Sort/Reverse/RemoveRange proving tests (issue #353)
- Record.FieldNo field metadata introspection
- prove Codeunit.Run(id, var rec) via 64-codeunit-run-record suite
- Time2HMS / DT2Time coverage — Hour, Minute, Second proving tests
- prove FieldRef.Active() via 62-fieldref-active test suite
- HasTableConnection() stub returning false
- CompanyProperty.ID stub + proving tests for DisplayName/UrlName/ID
- Date2DMY / Date2DWY coverage — Day, Month, Year, DayOfWeek, WeekNo
- Database.CompanyName — default stub returns CRONUS
- BigInteger.ToText coverage — proving tests for Format(BigInteger)
- table fieldgroup(Fixed) section coverage
- Boolean.ToText — Format(B) returns Yes/No; Standard Format 2 returns 1/0
- Guid.IsNullGuid — check if GUID is all-zeros
- Database.SessionId — stub returning 1
- UserId() default stub value + coverage
- Guid.CreateGuid / CreateSequentialGuid — GUID generation
- Record.Copy fix + coverage
- implement Record.CalcSums — sum numeric fields with current filters
- Record.SetRecFilter composite-PK fix + coverage
- UserId() configurable via --user-id CLI flag
- Record.Next(Steps) overload
- CompanyName() configurable — CLI flag and AL stub codeunit
- Record Mark / MarkedOnly / ClearMarks
- Format() multi-token decimal picture strings
- Record.FieldError — raise field-level validation error
- runtime-API coverage layer
- AL language coverage map with CI validation
- add --strict flag — exit 1 on runner limitations
- HTTP mock types — HttpClient/Response/Content/Headers/Request
- ReportHandler dispatch + Report.Run/RunModal/UseRequestPage
- improve codeunit-not-found diagnostics
- RecordRef/FieldRef API completeness + KeyRef support
- field metadata infrastructure — FieldCaption, TableCaption, TableName, FieldRef metadata
- temporary records & FlowField sum/count/lookup
- add FieldRef enum introspection, CalcSum & RecordRef system-field accessors (closes #126)
- improve XmlPort & Query runtime error messages
- implement ErrorInfo type & collectible errors framework
- add Notification, BigText, TaskScheduler & DataTransfer mocks (closes #121)
- add System, Database & Session utility stubs
- Record core methods — GetFilter, GetFilters, HasFilter, CurrentKey, Ascending, stubs
- Event subscriber parameter forwarding, implicit DB events, BindSubscription
- add BC 28.0 to test matrix; classify missing BC DLLs as runner limitations
- differentiated exit codes (0=pass, 1=fail, 2=runner-limitation, 3=AL-compile-error)"
- add MockXmlPortHandle so XmlPort variables compile in al-runner
- resilient against duplicate .app packages (AL0275 self-duplicate)
- ModalPageHandler dispatch — production RunModal intercepted by test handlers (closes TestPage gap)
- generate-stubs filters to referenced objects when source dirs provided
- --generate-stubs command scaffolds AL stubs from .app symbol packages
- RecordRef.GetTable/SetTable + FindFirst-returns-false on empty table
- RecordRef + FieldRef runtime support (Open, Field, Value, Insert, Find, Filter, Delete)
- add --iteration-tracking CLI flag
- wire IterationTracker into pipeline with enable/reset/disable and JSON serialization
- add IterationInjector Roslyn rewriter for loop tracking
- add IterationTracker static collector and expose AlScope.GetHitStatements()
- add Message capture and OnRun value capture to JSON output
- add stage and per-test timing to output
- mock IsolatedStorage as in-memory key-value store
- auto-wrap bare AL statements in -e flag
- add --guide flag with test-writing reference for AI agents
- add ALAssign to MockCodeunitHandle for codeunit variable assignment
- add --stubs flag for overriding dependency objects
- add --verbose flag, default output shows only actionable results
- add -h/--help flag with proper usage text
- add manually triggerable coverage demo workflow
- generate Cobertura XML coverage report with --coverage
- matrix testing across BC versions + NuGet publish pipeline
- auto-download AL compiler from NuGet, zero manual setup
- auto-download BC DLLs during build via MSBuild target
- self-contained cross-platform dotnet tool with auto-download
- add coverage to CI pipeline with job summary
- add --coverage flag for statement-level test coverage
- add CI pipelines, intentional failure sample, update docs
- implement Assert mock, composite PKs, sort ordering, ALFieldNo

### Fixed
- **provision:** preserve merged CLI behavior
- **capture-values:** record one value per statement execution, not one end-of-test snapshot
- **metadata:** evict base NCLMetaTable on precompiled tableextension merge too
- resolve TestPage field controls on pages precompiled in dependency .apps
- **help:** describe execute for what it does, add --dap to USAGE, drop stale debug-adapter TODO
- reject a non-rooted $HOME resolution instead of silently passing on a relative artifact/cache root
- **server:** stop execute from silently discarding Message() output
- **ci:** reject CI-skip directives in PR title/body, recover sync workflow by hand or on schedule
- **changelog:** classify scoped conventional-commit prefixes, strip repeated (#N), sync [Unreleased]
- **publish:** stop hardcoding main in the release-commit push, fail fast on an unpushable ref
- **startup:** report the true final package-cache search set
- **tests:** make SiblingSourceDep_CompilesWithZeroPackageCacheDirs hermetic
- **startup:** defer the remaining per-generation startup lines past re-exec
- declare _BCVersion default once in Directory.Build.props
- **provisioning:** derive transitive no-fallback platform-app need via a closure walk, not a hand-maintained list
- **deps:** report missing/too-old third-party deps as provisioning gaps, not COMPILE-FAIL
- **startup:** defer startup trio across every re-exec generation, not just the shadow hop
- **provision:** expose platform-apps/test-apps/service-tier download from the shipped binary
- **cli:** -v/-V/version alias --version, --help prints version, --guide tells agents where and how to report gaps
- **provision:** detect transitive Application Test Library need, provision the selected BC version not the cache's
- **testpage:** GoToRecord on a precompiled page derives the key from the record's metadata
- **testpage:** build a linkless subpage part without demanding the host's record
- **dap:** bypass the per-test watchdog while a DAP session has the thread paused
- **testpage:** type-aware conversion for page-global Code/Date/Enum controls
- **tests:** stop a swallowed cleanup failure in StartupOutputReexecDedupTests from breaking its sibling
- **dap:** surface --dap variable ToString() failures instead of flattening to null
- **capture-values:** surface field-read/ToString() failures instead of dropping or faking them
- print startup reporting once per invocation, not once per re-exec generation
- gate the explicit-engine-minor warning on shipped variants, surface re-exec explanations
- **provision:** target the exact built BC build when no version is pinned
- **provision:** default auto-provisioning to the engine's own build, not latest
- **provision:** auto-provision the BC artifact cache by default
- **pack:** stop shipping Microsoft.Dynamics.Nav.Ncl.dll in the tool nupkg
- **pack:** stop shipping BC service-tier/Aspose/Graph binaries in the tool nupkg
- **diagnostics:** stop the Field virtual-table ctor-failure message from asserting an unverified cause
- **hooks:** delete the 8-registration ALDatabase orphaned-JmpHook cluster, Cecil-own get_ALSerialNumber
- **testpage:** dispatch [ModalPageHandler] for a page with no SourceTable
- **watch:** make RadSelfBaselineLoader's "not mine" signal agree with the rest of the composite chain
- **hooks:** delete the NavCancellationToken orphaned-JmpHook cluster
- **provision:** reject a warm app set below the manifest's version floor
- **release:** build before write, and stop pinning a BC build MS can withdraw
- **hooks:** delete the ALSystemErrorHandling + ALSystemString orphaned-JmpHook clusters
- **provisioning:** manifest-driven platform/test-app need detection
- **pages:** pageextension action stops dispatching when it also adds a part()
- **provision:** bootstrap Microsoft apps from manifests
- **watch:** surface WHY a --watch cycle fell back to a full rebuild
- **tests:** isolate BcCompilerSharedReferenceMemoTests from the machine's real symbol caches
- **provision:** floor reuse at the version the gap actually needs
- **provision:** reuse an already-provisioned artifact set instead of re-downloading it
- **hooks:** delete the 17-hook ALIsolatedStorage orphaned-JmpHook cluster
- **cli:** resolve tests/expectations relative to bundle path, not just cwd
- **record:** fail loudly instead of silently disabling rowversion stamping
- **record:** stamp rowversions on database-backed writes so HasBeenInserted answers truthfully
- **deps:** read a sibling app's manifest from its app root, not just its source folders
- **layered:** decide prebuilt-shadow staleness on content, not mtime
- **watch:** keep one module per AL identity across bundles under --watch
- **deps:** ask same-SourcePath before identity in TryGetByAppId
- **testpage:** dispatch triggers of page members whose AL names contain spaces
- **rad:** stop the reference-graph walk swallowing its own faults
- **phaselog:** report a real peak RSS on macOS instead of a silent zero
- **scripts:** parse VSTest timestamps on Python 3.9, and hold the server-mode FIFO open portably
- **ci:** publish.yml must run the BC engine tests, not skip them
- **watch:** stale page-metadata bookkeeping and cross-cycle AL stack leak
- **events:** preserve subscriber stack traces on rethrow; recognize a table publisher's INavRecordHandle sender
- **hooks:** resolve the single-Hook()-call-site cluster of orphaned JmpHook registrations
- **pages:** Rec resolves inside a pageextension-contributed OnAction
- **watch:** clear page and xmlport metadata on a non-preserving reload
- **tests:** WatchBurstSwitchTests no longer scores a verdict off the burst's own cycle(s)
- **transactions:** DeleteAll()/ModifyAll() open a write transaction even with zero matches
- **transactions:** a failed Delete() no longer wipes rows a nested BC transaction already committed
- **testpage:** pageextension-contributed action Invoke() dispatches its own OnAction
- **pages:** resolve real MasterPage for a precompiled dependency page
- **runtime:** TestField's navigate-action lookup no longer hijacks the real TestField error
- **navapp:** NavApp.GetCurrentModuleInfo source polyfill returns void — CS0023 on boolean-context calls
- **tests:** final-cycle window must exclude earlier burst cycles
- **compiler:** manifest-derived ParseOptions/CompilationOptions on every compile path
- **xmlport:** delete 3 more orphaned JmpHook registrations in the NavXmlPort cluster
- **watch:** make WatchBurstSwitchTests's burst assertion deterministic, not real-clock-dependent
- **watch:** make a warm cycle report the same run as the cold one
- **diagnostics:** stop two out-of-test failure paths from misreporting themselves
- **rad:** repair namespace-free packaged binding
- **rad:** stop inventing AL0133 when the packaged surface will not resolve
- **rad:** refuse a bundle whose apps compile under one AppId
- **rad:** restore the provenance strip, shipped empty by mistake
- **watch:** show the cross-app rebind reason where the developer is looking
- **runtime:** end manual event bindings a SingleInstance codeunit owns
- **rad:** rebind a sibling app whose callee moved a member id
- **rad:** rebind bystanders that lose surface when a delta strips their target
- **testpage:** SetValue(Boolean) on Rec-bound Boolean controls
- **server:** classify inline AL code with BC's own parser, not a keyword prefix
- **testpage:** resolve Enum-typed controls by Caption and refuse the member name
- **page:** materialise pages with Enum-typed controls via AlEnumMetadataRegistry
- **report:** apply the caller's table view before OnPreReport so GetFilter() reads it
- **runtime:** static Page.RunModal(0, Record) resolves the page via the table's LookupPageId
- **testpage:** TestPage field Visible() must walk the enclosing group chain
- **watch:** make the burst quiescence test deterministic, not real-clock-dependent
- **runtime:** construct Page{id} for static Page.RunModal(id, Record)
- **rad:** give the delta path the app root, and stop fingerprinting where a symbol came from
- **watch:** wait for quiescence instead of a fixed debounce
- **deps:** honour the dependency's app.json manifest in source-dependency compiles
- **runtime:** resolve AL objects against the current generation across a cross-app cycle boundary
- **corpus:** keep main's al-language pin through the merge
- **compile:** give BC's compiler a file system so ControlAddIn resources resolve
- **tests:** use TestArtifacts' skip gates in the two watch suites
- **server:** register per-request bundles in the cross-bundle AppId cache
- **parser:** thread --define symbols into the table-metadata parser
- **cache:** key ncl-cecil on the runner's content hash, not its mtime
- **phase-log:** stop the cohort-ratio false verdict; wire phase-log into --server mode
- **unit-tests:** recover CollectionCostOrderer's stale weight table
- **xmlport:** resolve the NavXmlPort orphaned-JmpHook cluster
- **cache:** stop embedding the git commit SHA into al-runner.dll's bytes
- **record:** run OnValidate triggers on tableextension-added fields
- **events:** unwrap ByRef-wrapped publisher scope args for by-value subscriber parameters
- **cache:** compiled-deps / workspace-deps / ncl-cecil / bc-symbols honour --cache
- **testpage:** eliminate literal Visible = false controls from the page
- **cache:** content-address the bc-symbols cache key, not .app mtime
- **testpage:** Boolean SetValue() works on page-variable-bound controls
- **tests:** make WatchTests' warm-timing assertion independent of pump scheduling
- **deps:** discriminate genuine same-app reuse from AppId collisions
- **tests:** replace ServerCancelTests' calibrated spin race with a deterministic barrier
- **ci:** bump xunit.runner.visualstudio to 3.1.5 so a green run cannot exit 1
- **cache:** content-address the runner fingerprint in cache keys, add an explicit bc: line
- **watch:** arm FileSystemWatchers before announcing --watch is ready
- **tests:** make the in-process BC engine tests actually execute on CI
- **cache:** publish AL-output cache entries atomically
- **events:** dispatch manually-declared IntegrationEvents from Page/Report/Query/XmlPort
- **mediaset:** hook BC 27.x's synchronous AddMediaToSet, hard-error on unknown shapes
- **record:** make NavRecord.GetCallerRecord track the real AL frame
- Field virtual table now reports declared ObsoleteState/ObsoleteReason
- **reports:** route static Report.Run/RunModal through real execution, not a dead JmpHook
- **testpage:** TestPage.Caption() and field Caption() now return real captions
- **streams:** match BC's Rename BLOB-loss for temporary records
- TestPage field DrillDown() dispatches OnDrillDown
- **test-executor:** run [Test] procedures in AL source declaration order
- **events:** dispatch manually-declared table-published IntegrationEvents
- MediaSet.ImportStream membership lost after Modify()+Get()
- **enum:** ingest declared Caption at emit time so Format(enum) returns it
- **testpage:** capture the insert position at New() so AutoSplitKey covers mid-grid, negative and typed keys
- **record:** resolve a CalcFormula where-condition whose field() names a FlowField
- **record:** keep a database-backed row's BLOB out of the record that inserted it
- **testpage:** number AutoSplitKey rows from the data, not from a constant
- **events:** honor EventSubscriberInstance = Manual for table-event subscribers
- **events:** honor EventSubscriberInstance = Manual in codeunit-event dispatch
- **record:** apply the flow-filter family of CalcFormula where-conditions
- **testpage:** action invoke saves the new subpage row, and AutoSplitKey assigns its key
- **record:** CalcFields on a BLOB keeps an uncommitted in-memory write
- **page:** page-driven Modify holds the before-image in xRec
- **report:** Report.Run() iterates the data item, and SetTableView no longer NREs
- **record:** Rename propagates through conditional and where-filtered TableRelations
- **deps:** persist source-dep enum sidecar and build the symbols loader with zero package caches
- **expectations:** expect-oos matches the message convention, add expect-divergence
- **expectations:** wire the ExpectationManifest into the run
- **page:** bind Rec on a plain page-variable, not just TestPage
- **scheduler:** CreateTask hits the documented NavCreateScheduledTasksNotAllowedException, not a codeunit-resolution error
- **record:** Rename propagates to validated TableRelation fields
- **moduleinfo:** GetCallerModuleInfo must name the caller across an app boundary
- close the seven gaps split out of the parser migration (#1708-#1714)
- **server:** stale-green multi-bundle requests — per-request reset, content-stamped cache key, loud EMIT-EXCLUDED
- **loader:** serve Microsoft.Dynamics.Nav.CodeAnalysis from the selected artifact dir, not bin
- **parser:** AL comments no longer read as properties in the sibling parsers
- **deps:** name the dependency no loader tier can implement
- **parser:** AL comments no longer read as table/field properties
- **pkgdedup:** transient Windows move failure no longer kills the run
- **reporter:** --output-json crashed when two bundles shared a basename
- platform-table metadata lost when a dependency app extends the table
- **dispatch:** dedupe module identity across bundles to fix event-subscriber TargetException
- **provision:** fold bundle .alpackages into the platform-app R2R gate
- **win32stubs:** never intercept Win32 imports on Windows
- correct filter-only query-column slot aliasing in multi-dataitem joins
- **deps:** resolve relative --package-cache dirs before pkgdedup symlinks
- **record-metadata:** strip quoted-identifier InitValue on enum fields so blank-named values evaluate
- **cli:** --output-json redirects progress banners to stderr, purifying stdout
- **win32stubs:** stop silently swallowing shim-build failures — throw loudly instead
- **cli:** --test-isolation method now maps to per-test reset, not codeunit isolation
- **provisioning:** --auto-provision downloads into the runner artifact cache, not the project's package cache
- **enum:** merge enumextension values into Enum.Ordinals()/Names()
- **server:** runTests/execute honour every sourcePaths entry, not just [0]
- **provisioning:** DownloadArtifacts fails loud, not with a raw stack trace, on a 404
- **ci:** Tests-updated gate now also matches AlRunner.Tests/
- **release:** dedupe the duplicated v2.0.0 CHANGELOG section, make publish.yml idempotent
- **ci:** publish.yml's test job was missing the platform-apps package cache
- Record.Get with fewer key values than PK fields binds type defaults instead of prefix-matching
- make TableFieldRegistry.GetSourceTableId a pure reader to remove rewrite-phase race
- raise platform error when Record.Get receives more key values than PK fields — closes #1630
- skip AL string literals when counting braces in StripPatternedBlock/StripNamedBlock — closes #1600
- emit auto-stubs in consumer using-namespace to resolve AL0118 for namespace-aware consumers
- exclude PagePartSyntax names from usercontrol stub injection guard — closes #1597
- drop event subscribers targeting missing codeunit to prevent CS0131 / BadExpression crash
- BC-faithful error formats + clean ATL stub override (Library Management gaps)
- ensure enum auto-stubs include a synthetic value to prevent NRE in EnumExtensionTypeMetadataEmitter — closes #1590
- prevent CS0101 duplicate-class collision when source-dir input also exists in package cache
- derive CLEANSCHEMA defaultMax from BC application version, not hardcoded 25
- catch bare emit exceptions and write partial symbols.json on compile-dep failure
- strip DotNet procedures and skip pure assembly files in extract-deps — closes #1524
- evict stale dep DLL when .app content changes without version bump
- Report.SaveAs*/SaveAsPdf*/etc. static stubs return bool not void
- case-insensitive SubType = Test gate at Pipeline.cs:796 — closes #1520
- add ClearReference() to MockInterfaceHandle — closes #1565
- sharpen ScoreMethodMatch + broaden retry catch for auto-stub multi-overload dispatch
- **MockFile:** ALOpen/ALErase/ALCopy return bool to fix CS0023 in boolean contexts (issue #1530)
- **MockFile:** add 7-arg ALUpload overload to fix CS1501 (issue #1531)
- **MockHttpClient:** replace ALUseDefaultNetworkWindowsAuthentication property with method to fix CS1955 (issue #1532)
- **MockHttpClient:** add NavSecretText overloads for UseWindowsAuthentication and AddCertificate to fix CS1503 (issue #1533)
- **rewriter:** skip ToText()→AlCompat.Format() for 0-arg user-defined methods
- Format() <Filler Character,N> directive emits nothing instead of literal token text
- MaxStrLen(Record.Field) returns declared length after InitValue/Get
- revert MockImage SA reimplementation to blank shell
- merge same-arity overloads in auto-stub generator to prevent NavOption/NavCode cast
- Format(Option) renders member name and Text relational operators no longer NRE
- tighten ALSetAutoCalcFields wrapper signature to typed overload pair
- make ALMark(bool) return bool to resolve CS0019 in if-expression context — closes #1492
- initialize page Rec as temporary when SourceTableTemporary=true — closes #1490
- add ALEvaluate(ByRef<char>) overload — closes #1483
- triager must grep runtime before labelling telemetry issues needs-input
- add MockVariant/object overloads to MockFieldRef.ALValidate/ALValidateSafe — closes #1487
- persist TestPage Field.SetValue to underlying record — closes #1486
- tighten triager closing rule and add impl-agent claim race-condition check
- add MockJsonHelper integer-index overloads for JsonArray.GetText/GetInteger/GetDecimal/GetBoolean/GetArray — closes #1426
- initialize Rec backing on Page<N> var instantiation — closes #1422
- add TestPage.Filter.SetFilter object overload and regression tests — closes #1459
- preserve Enum/Option parameter types in auto-stubbed codeunits — closes #1419
- parse image dimensions from header — closes #1421
- add object/NavText overloads to MockTestPageFilter.ALSetFilter — closes #1442
- only discover tests with [NavTest] attribute, not by name — closes #1420
- preserve AutoIncrement and PK schema on auto-stubbed packaged tables — closes #1418
- prevent ByRef<int> CS1503 in auto-stub var-param signatures — closes #1433
- IsolatedStorage.Set, XmlNode.AddBeforeSelf/AddAfterSelf/Remove, ReportInstance.SaveAs return bool — closes #1432
- ALCompressArray returns int — closes #1446
- handle Duration to Integer implicit conversion in HttpClient.Timeout — closes #1445
- inject GetGlobalVariable/SetGlobalVariable on ReportExtension<N> — closes #1450
- add MockArray<T>.Clear(int) single-int overload — closes #1448
- implement TestRequestPage.GetDataItem — closes #1457
- implement missing CurrPage.Run() stub on Page<N> — closes #1444
- implement MockStream.ALWriteString — closes #1437
- implement ALStopSession 3-arg overload — closes #1443
- handle ALSystemVariable.ALEvaluate<MockVersion> CS0452 — closes #1429
- implement ALStartSession 3-arg overload — closes #1435
- implement ALTruncate 2-arg overload — closes #1431
- implement Report.Run 2-arg (StaticRun 2-arg) — closes #1427
- implement MockPartFormHandle.PageCaption — closes #1440
- implement ALFieldError 0-arg overload — closes #1428
- implement RecordRef.Insert(Boolean) and Insert(Boolean,Boolean) overloads — closes #1430
- implement MockHttpClient.ALAssign — closes #1447
- implement MockFieldRef.ALKeyIndex — closes #1434
- implement MockRecordRef.ALWritePermission — closes #1441
- shim ALAddLoadFields and ALAreFieldsLoaded on record wrapper — closes #1412
- type MockTestPageField.ALValue as string to match BC's TestPageField.Value — closes #1407
- rewrite MockArray<MockInterfaceHandle> Factory ctor to lambda — closes #1406
- qualify shadowed PromptMode enum reference inside page class — closes #1404
- shim ALSetLoadFields(DataError, params int[]) on record wrapper — closes #1405
- prove XmlAttribute.Create(Text, Text, Text) — closes #1399
- implement XmlAttributeCollection namespace-qualified overloads — closes #1376
- implement miscellaneous single-method gaps — closes #1382
- implement HttpContent.WriteFrom(Text/SecretText) and HttpHeaders.GetSecretValues(Text, List) — closes #1381
- implement ReportInstance/QueryInstance missing methods — closes #1379
- implement Report.Execute/Run/RunModal Text-name overloads and mark RunRequestPage(Integer,Text) covered — closes #1377
- implement Xml*.SelectNodes/SelectSingleNode with XmlNamespaceManager — closes #1371
- implement System missing overloads — closes #1375
- implement ErrorInfo/Dialog/FilterPageBuilder/TestField missing overloads — closes #1380
- implement Text/Label/TextConst missing overloads — closes #1378
- implement Page.Run/RunModal 3-arg overloads — closes #1374
- implement XmlDocument/Element/DocumentType missing overloads — closes #1372
- implement Xml*.WriteTo per-format overloads — closes #1370
- implement Table.FullyQualifiedName + mark Insert/FindSet/FieldError/TransferFields/CopyLinks overloads as covered — closes #1373
- implement Json.* per-primitive-type overloads — closes #1368
- implement Table.TestField typed and ErrorInfo overloads — closes #1369
- add MockObjectList.ALAssign for List of [RecordRef] var params — closes #1335
- detect duplicate pageextension names within same extension as AL0197 — closes #1345
- detect duplicate object names across apps as AL0197 — closes #1344
- add Report instance RunRequestPage 1-arg overload — closes #1333
- add ALTestFieldNavValueSafe object-arg overload — closes #1324
- route ALCompiler.NavValueToNavValue<T> through AlCompat for Date/Code/Text/Boolean filter fields — closes #1341
- add Page<N>.CallGetAutoFormatStringExtensionMethod/EnsureGlobalVariablesInitialized — closes #1332
- add Report.Run 3-arg overload (no systemPrinter) — closes #1336
- add ALTransferFields 3-arg overload — closes #1337
- inject ALRecordId/ALCurrentCompany/ALTestFieldNavValueSafe into Record classes — closes #1330
- add Page.EnqueueBackgroundTask 5-arg overload — closes #1327
- add ALViewFromStream 3-arg and 4-arg static overloads — closes #1331
- add StaticRunRequestPage 2-arg overload — closes #1329
- add MockVersion.ALCreate 2-arg and 3-arg overloads — closes #1323
- add MockReportHandle.ALAssign — closes #1328
- add RecordRef.ReadPermission/SetAutoCalcFields — closes #1326
- add MockPartFormHandle.Close/GetRecord — closes #1325
- add string overload to MockVersion.ALCreate — closes #1322
- add MockHttpClient.Clear() for global Clear(client) syntax — closes #1334
- route CalcDate through AlCompat to avoid NavNCLDateInvalidException
- bump AL compiler to v17.0 for BC 28 runtime 17 support — closes #1255
- add object overloads for string-expecting methods — closes #1297
- add object catch-all overloads for record operations — closes #1260
- add ALInvoke<T> extension for string receivers — closes #1298
- rewrite Page<N>.PromptMode static self-reference — closes #1266
- add BookmarkType, CheckType, and SetRecord stubs on Page classes — closes #1262
- add implicit string conversion to MockInStream — closes #1273
- add implicit int→MockFieldRef conversion for BC-emitted field numbers
- add 3-arg Invoke(extensionId, memberId, args) to MockFormHandle and MockReportHandle — closes #1282
- add DataError-prefixed overloads for field-level record methods
- add 3-arg overloads for JsonObject.GetInteger/GetBoolean/GetDecimal
- add string overload for AlCompat.CreateErrorInfo — closes #1278
- read application_Version instead of customDimensions for version
- don't reopen closed issues from telemetry
- add missing GetUrl overloads (1-arg, 2-arg) — closes #1299
- add Clear method to MockDataTransfer — closes #1269
- add CheckType no-op stub on Record classes — closes #1280
- rewrite MockRecordRef.Factory for RecordRef array declarations
- change MockMedia.ALMediaId from method to property
- remove MockDialog.ALUpdate(int, int) to resolve CS0121 NavValue/int ambiguity
- use source_indices instead of source_group_keys for row matching
- implicit MockRecordHandle to MockRecordRef conversion
- resolve Codeunit305002 ID collision between 96-validate-no-value and 305-filterpagebuilder-assign
- add 2-arg ALValidateSafe overload to injected Record class
- add ALAssign to MockFilterPageBuilder — closes #1276
- normalize Record/Codeunit types in triage aggregation
- skip TableNo codeunits in OnRun; add --run-codeunit; remove implicit RunOnRun
- ALReadAs returns bool so 'if Content.ReadAs(T) then' compiles — closes #1250
- increase GitHub Models timeout to 120s, add 2 retries on transient timeouts
- anchor triage time window to last completed run, not last successful
- rewrite ALCompiler.ToRecordRef to MockRecordRef.FromHandle
- NavObjectDictionary → MockObjectDictionary to lift ITreeObject constraint on codeunit-value dictionaries
- preserve page InitializeComponent field inits to fix CopyArray on page-level text arrays
- add string overloads for NavApp.IsEntitled to accept literal Text arguments
- actionable error messages for access-denied + Windows install docs
- --init-events fires once at startup, snapshotted as DB baseline — closes #1220
- NavValue→NavCode coercion for `in` operator on Code fields — closes #1211
- TestPage.GoToKey accepts TestField reference — closes #1215
- settable RecordRef.CurrentKeyIndex with re-sort on change
- add DataError-typed UploadIntoStream 2-arg overload — closes #1213, #1214
- stub GetDataItem/ParentObject on ReportExtension — closes #1212
- add session-aware TestField.ALAsDateTime overload — closes #1216
- add 2-arg UploadIntoStream overload — closes #1210
- add --test-isolation method to publish workflow (matches test-matrix.yml)
- implement IConvertible on MockRecordHandle — closes #1201
- NavOption → NavText coercion in CoerceToExpectedType and ConvertArgInternal — closes #1199
- handle extra scope ctor params in RunTests() — closes #1200
- handle NavMediaSystemRecord base class and Media stream overloads — closes #1188, closes #1190
- MockNotification.Default, CurrReport.ObjectID, MockPartFormHandle stubs
- resolve Dialog.Update NavCode ambiguity, Variant→Codeunit extraction, NavCode list ops — closes #1179, closes #1184, closes #1185
- add Clear() to MockOutStream, MockFile, MockRecordArray — closes #1178, #1181, #1182
- add missing method overloads — Report.Execute(Text), File.Create bool return, RecordRef.AddLink, RecordRef.GetView(bool)
- ByRef<MockVariant> parameter conversion for Record arguments — closes #1160
- CreateDateTime/DT2Time round-trip on non-UTC hosts
- Format(Record) returns position string; guard ConvertArgInternal against MockRecordHandle→primitive — closes #1161
- MockNotification — ALRecall returns bool, add ALAssign and Clear — closes #1153
- emit LF (not OS newline) from TextBuilder.AppendLine
- skip all built-in stub IDs in --generate-stubs and auto-stub
- actionable error messages for missing codeunit methods + auto-stub tip
- default test-isolation to codeunit (BC default) + better error output
- prevent StackOverflow from recursive record triggers
- rewrite CompareTo(x) == 0 to == operator for NavText/NavCode comparisons
- route codeunit 130002 (real BC Library Assert ID) to MockAssert
- InitializeUninitializedObject in OnValidate and record method dispatch
- initialize null backing fields after GetUninitializedObject in event/trigger dispatch
- pre-seed User table + tolerate duplicate-PK errors in init events
- Boolean→NavText cast + --test-isolation codeunit|method flag
- coerce NavBoolean→NavText in CoerceToExpectedType to prevent cast crash
- correct init-events publisher IDs — OnInstallApp uses 2000000010, not 2
- coerce plain T to ByRef<T> in InvokeSubscriber for implicit DB event subscribers
- coverage.yaml entry names and layer values
- null guard in CloneTaggedOption prevents crash on uninitialized enum fields
- system codeunit (1-9999) Invoke/RunCodeunit is a no-op instead of throwing
- NavText→NavCode cast + base.Parent.Bind() null-deref
- StubGenerator detects EventSubscriber refs and supports multiple package dirs
- coverage.yaml invalid layer compiler-rewriter → runtime-api
- inject implicit NavForm conversion on Page<N> classes — issue #1106
- change AlScope.Parent from static to virtual instance — issue #1105
- inject RunModal(), LookupMode, and CurrPage members on Page<N> class — issues #1079 #1082
- resolve codeunit 165001 ID collision between testfield-errorinfo and secrettext-http
- SecretText in HTTP headers/content — NavSecretText→MockInStream and string→NavText (#1086, #1091)
- normalize generated type IDs in telemetry dedup — collapse Page<N> variants
- enrich telemetry dedup keys for CS1503/CS1501/CS0117/CS1729/CS1674 — issue #1074
- normalize generated-type numeric IDs in triage grouping
- make RewriterFailure test BC-version-stable — match by class name not call order
- assign _parent in scope constructors for nested BC types — issue #1013
- rewrite ALCompiler.ObjectToNavOutStream/InStream on chained-call path
- stop silently removing objects from compilation — issue #1040
- skip .app packages that overlap with source objects — issue #1034
- resolve CS0121 ambiguous ALTestFieldSafe overload — issue #1018
- strip InherentPermissionsList from generated scope classes
- MockHttpContent.ALGetHeaders as method (not property)
- MockFilterPageBuilder.ALRunModal returns bool to fix CS0019 in compound boolean expressions
- NavIndirectValueToNavValue<MockRecordRef> — rewrite to direct cast
- TestPage.GetRecord — narrow ALGetRecord interception, document BC 26+ removal
- TestRequestPage field value proving tests + NavInteger cast in ExtractDecimal (issue #848)
- Guid.ToText() — correct format B (38 chars) and format N (32 chars)
- renumber errorinfo-title suite to resolve bucket-1 ID collision
- renumber EI Verbosity suite to 83700/83701 to avoid CS0101 collision
- Database.SID() stub
- ErrorInfo.ControlName get/set
- Table.Ascending get/set
- Table.ModifyAll coverage.yaml gap → covered
- mark Table.Validate as covered in coverage.yaml
- resolve codeunit 54800 ID collision between userid and next-steps suites
- GetFilters returns real field names
- resolve CI warnings (Node.js 20 deprecation + compiler warnings)
- resolve CS1061 'Parent' on ReportExtension scope classes
- resolve compilation gaps from telemetry issues #168-174 + Report.Skip
- triage workflow groups by root cause, not per error row
- Skip RDLC layout generation + compilation gap telemetry
- update stubs path in publish workflow after bucket restructure
- ALRename properly updates table rows and validates keys
- honor DataError level in ALInsert and ALDelete
- CS1503 for HttpContent.WriteFrom(InStream) / ReadAs(var InStream)
- SetSelectionFilter no longer references this.Rec on pages without SourceTable
- FieldRef.SetRange(Variant) and NavCode filter comparison
- GlobalLanguage() NullRef (#82) and FieldRef.SetRange CS0121 ambiguity
- enforce PK uniqueness on Insert() for all tables
- filter internal runtime types from captured values
- remove leaked IterationTracking_EmitsSourceFile test (belongs to PR #90)
- add ALAssign, binary ALWrite/ALRead, ALCopyStream to stream mocks
- Dialog variable Open/Update/Close no longer fails with NavComplexValue conversion error
- stub NavXmlPort-derived classes so XmlPort test suite compiles
- update test-84 and C# tests for all-or-nothing compilation
- remove stale RoslynCompiler.ExcludedFiles references in Pipeline and Server
- distinguish compilation errors from test failures in --output-json
- Variant-to-Record cast and Nav wrapper type checks
- rename test 67→68, use unique codeunit IDs 56670/56671 (CI conflict with 01-pure-function's codeunit 50100)
- nested loop hit-list corruption + cleanup dead API and stale guard
- filter coverage to executable statements in user files only
- place EndIteration before break check in for-loops
- convert 0-based SourceSpan lines to 1-based
- record per-iteration line hits via StmtHit hook
- match trigger scope names (OnRun_Scope) in SourceLineMapper regex
- map StmtHit IDs to AL source lines in iteration tracking
- use unique object IDs in tests 28-32 to avoid CI conflicts
- remove stale done/exit from CI script
- revert to per-suite invocation (ID conflicts in single invocation)
- ExpectedError uses substring containment like BC (not exact match)
- add missing IsolatedStorage overloads for actual transpiler signatures
- iterative Roslyn retry only excludes files with direct errors
- show why codeunits were excluded when tests fail with 'not found'
- add Format(value, length, formatString) overload for AL date formatting
- route codeunit 130000 (BC test toolkit Assert) to MockAssert
- compile source directories as single group for internal visibility
- skip Assert stubs when real Assert.app in packages, fix verbose leaks
- walk up directory tree for app.json, load all packages as symbols
- search subdirectories recursively for .al files
- include README.md in NuGet package
- rename NuGet package ID to MSDyn365BC.AL.Runner
- write coverage output directly to job summary, skip flaky parser
- add sources element and use relative paths in Cobertura XML
- use invariant culture for Cobertura line-rate decimals
- simplify publish summary step to avoid JSON parse crash
- update default BC version to sandbox 27.5.46862.48827
- pass BC_SERVICE_TIER_PATH to runtime in matrix test jobs
- add retry logic and fix HttpClient disposal in artifact download
- use markdown emoji shortcodes in job summary table
- resolve IsTrue/IsFalse ambiguity, remove hardcoded path
- add LICENSE, fix warnings, implement AreNearlyEqual and Fail
- use AL object names in Roslyn compilation diagnostics
- download all Nav DLLs in a single range request
- restore NuGet restore step, skip Service/ subdirectories
- use --no-build for sample runs after explicit build step
- download only the 8 needed DLLs instead of all 774
- normalize backslash paths in BC artifact zip entries
- use HTTP range requests for BC artifact download in CI
- chmod extracted BC artifacts to fix permission denied in CI
- CI artifact download handles Windows-format zip warnings
- CI workflows download BC artifacts and pin AL compiler version
- add artifacts/ to .gitignore
- remove out-of-scope code (MockFormHandle, NavTestPageHandle, MockRecordRef, RegexRewriter)

### Documentation
- **rules:** a backgrounded foreground command promises nothing either
- **agents:** say how to wait for CI, not just how to read it
- **rules:** fold pr-ci-monitoring.md into ci-verdicts.md
- **dap:** correct VS Code launch-config guidance for --dap
- **plans:** record the version floor and candidate-list decisions
- **plans:** design the destination-first provisioning check
- **readme:** drop the exact commit and file counts from the fork summary
- **readme:** rewrite the fork section from the commits, and delete delta-compile.md
- **readme:** state the re-run test count as a constant, not a column
- **readme:** say how many tests each cycle re-runs
- **readme:** show where a warm cycle's seconds actually go
- **readme:** describe what this performance fork changes, and measure it
- correct five claims the compile/watch merge left stale
- **rad:** point the by-name suites at symbols instead of rotted line numbers
- **graphify:** note the optional local knowledge graph, and which build to install
- **delta-compile:** what a warm cycle still pays, and what it no longer does
- **delta-compile:** the cold->warm drift was three defects, now fixed
- **rad:** measure the member-level win, and correct the figure it replaces
- **rad:** the cross-app hole a cache HIT walks straight through
- name the dependency-target exception to the delta-on-first-edit claim
- **rad:** the rebind is decided member by member now
- **rad:** correct the record on provenance, now that both sides record it
- **rad:** what the schema refusal costs, and the dependency-target hole
- **rad:** state the schema bump's measured cost, not "one full compile"
- **rad:** name the third producer in the equivalence suite's header
- **rad:** record the reference graph's one-way hole
- **rad:** the reference graph now holds cross-app edges
- **rad:** rewrite delta-compile.md around what the tests now measure
- **agents:** rebut the run_in_background rationalization behind the stall
- **agents:** name the no-poll guidance conflict that keeps killing agent work
- **agents:** widen the no-backgrounding rule and split flake budgets asymmetrically
- **agents:** land the unmerged rules branch, and stop agents re-running the whole suite before every push
- **agents:** fix impl-agent.md for v2 layout, add operational rules
- the documented build command fails on a fresh clone
- fix v1-to-v2-migration.md --version row (v1 rejects it, not supports it)
- correct stale limitations — event subscribers and task scheduler both work
- remove stale MockImage guide bullet and add SA boundary note
- audit 29 stub entries — add tests and justification notes
- audit not-possible/out-of-scope entries in coverage.yaml — add cross-references and re-classify StartSession overloads
- align CONTRIBUTING/CLAUDE bucket-loop with CI — closes #1409
- clarify CHANGELOG.md is auto-generated from commits, not manually edited (closes #1340)
- CHANGELOG for 1.0.20
- comprehensive README rewrite
- update --guide and README with built-in test toolkit stubs
- coverage gap audit — add 11 missing runtime-api entries
- add merge conflict checking to impl agent prompt
- streamline README — focus on getting started, link to detailed docs
- add overload-level coverage tracking to agent prompts
- mark System.CanLoadType and System.GetDotNetType as not-possible
- List.Sort() coverage status corrected to not-possible
- mark XmlDocument methods covered in coverage.yaml
- mark FieldRef enum introspection as covered
- orchestrator loops continuously until idle
- split and update agent-prompts — separate orchestrator/impl files, fix CHANGELOG rule
- remove CHANGELOG editing requirement from agent instructions
- register Table.Rename as covered in coverage.yaml
- reclassify type_declaration as out-of-scope
- restructure CLAUDE.md — remove historical logs, focus on TDD mandate and gap reporting
- move #128/#130 fixes into [1.0.13], clean up orphan text in [Unreleased]
- update CLAUDE.md — fix stale Remaining Gaps and Known Limitations
- reframe vision to broad AL compatibility, add CONTRIBUTING.md
- complete [Unreleased] changelog for all merged PRs
- add exit code table to README
- add Agent Working Rules section to CLAUDE.md
- correct event subscriber limitation — partial support, not absent
- add limitations reference document
- remove verbose test case table from README
- update README and --guide for JSON, BLOB, Variable Storage, TestPage navigation
- update README — RecordRef supported, TestPage partial support noted
- clarify --generate-stubs required vs optional parameters in --help
- update README for RecordRef, generate-stubs, CompanyName, StrSubstNo, iteration-tracking
- update CLAUDE.md, CHANGELOG.md, and PrintGuide for RecordRef/FieldRef support
- add architecture section explaining real BC types vs mocked I/O boundary
- audit and update all documentation for current state
- add testing requirements to CLAUDE.md — positive AND negative tests mandatory
- add guide maintenance note to CLAUDE.md

### Changed
- sync StefanMaron upstream
- reject PR bodies with missing or unintended closing references
- add a stdio transport so VS Code can launch the adapter directly
- **engine:** centralize the engine's hardcoded AssemblyLoadContext.Default assumptions
- Revert "chore: release v2.4.0"
- **watch:** generalize incremental (RAD) recompile to every object kind and file operation
- **runner-extras:** cover a shared library with three sideways-linked dependents
- **runner-extras:** cover two peer apps extending one platform table
- **release:** tests gate every write — no more dead tags on main
- **record-patches:** re-parse only the AL files that moved on a warm cycle
- **cache:** stop asking two whole-tree questions when no cache entry is in play
- **compile:** run BC's emit across threads by default
- **compile:** drop two untested concurrency escape hatches
- **rad:** delete four members nothing calls
- **gc:** run the runner under Server GC — 2.7x off a cold npcore compile
- **orderer:** weigh the five watch/RAD collections the merge left unmeasured
- Merge pull request #1 from vhn/mmv/fix-trx-parse-and-fifo-holder
- one BC test matrix, called by both the pull-request and release paths
- Merge upstream/main into the merged compile+watch branch
- Merge mmv/watch_performance into mmv/initial-compile-investigation
- **rad:** walk the object-reference graph in parallel
- **memory:** release BC's compilation before the Roslyn compile
- **compile:** parallelise the AL source-tree parse and the declared-object census
- **roslyn:** parallel parse, shared metadata references, one-pass polyfill redirects
- **packages:** one .app read answers both metadata questions, and the scan fans out
- **compile:** drop the emit-phase deadline and AL_RUNNER_EMIT_TIMEOUT_SEC
- **watch:** stop three states from costing a whole-module compile
- **watch:** incremental recompile for a single content-edited object
- **record-patches:** parallelize source batch parsing
- **rad:** the by-name family only ever measured apps with a namespace
- **rad:** give the rejected-C# guard a setup the member diff still calls a move
- **rad:** flip the procedure-addition pin to the member-level answer
- **rad:** decide the rebind member by member, not by the whole object
- **rad:** a one-shot sidecar's cross-app edges rebind a sibling on the first watch edit
- **rad:** refuse a genuine schema-1 envelope, and round-trip the edges schema 2 adds
- **rad:** bundle-scope the cross-app rebind, and prove the scoping is load-bearing
- **rad:** make the four cross-app rebind claims falsifiable
- **rad:** pin the silent same-app overload hazard, and the id contract under it
- **rad:** pin the sidecar baseline as a third, already-round-tripped producer
- **rad:** prove the two producers describe one surface member-for-member
- Merge remote-tracking branch 'origin/main' into mmv/initial-compile-investigation
- **boot:** remove ~18s of fixed per-invocation overhead (GetTypes scan, install-baseline reseed, manifest re-reads)
- stop 30 RAD tests reporting Passed while asserting nothing
- **rad:** RED — adding an overload silently rebinds a cross-app caller to the old id
- **rad:** pin the by-name property shapes — six clean, three not
- **rad:** RED — a delta damages the surface untouched objects derive from stripped ones
- **rad:** index FileOf instead of scanning every file per key
- **rad:** RED — a cross-app member-id move leaves the caller bound to the old id
- Merge remote-tracking branch 'origin/main' into mmv/watch_performance
- **startup:** drop two JmpHook-era JIT guards; grant orchestrator GitHub access
- Merge remote-tracking branch 'origin/main' into mmv/watch_performance
- **tests:** share one --server process per test class, not one per fact
- **cli:** drop the --rad rejection test
- Merge remote-tracking branch 'origin/main' into mmv/watch_performance
- **parser:** parse each .al file once for all eight extractors
- Merge origin/main into mmv/watch_performance
- **watch:** split the parse dedup and the bulk-change debounce out of this branch
- **startup:** drop two JmpHook-era JIT guards, ~27% off every runner spawn
- **server:** add reload regression coverage for #1860 (refuted)
- cache dependency Install-trigger + Company-Initialize baseline per dependency set
- de-duplicate FindBucketRoot between Program.cs and WatchSource.cs
- **phase-log:** attribute the flat per-app-group tax inside test run
- **record-patches:** seed report ids from PE bytes instead of Assembly.GetTypes()
- **watch:** one parse per file, and one cycle per bulk change
- **unit-tests:** --print-cache-key mode cuts CacheKeyDependencyClosureTests cost
- consolidate 16 standalone bundles into one app
- **record-patches:** batch AddSourceDir into one NCLMetadata cache pass
- **compiler:** keep the warm loader when the dep set only narrows
- **corpus:** bump tests/al-language 15d18e8 -> dadffa2
- **corpus:** bump tests/al-language f915a4c -> 15d18e8
- **compiler:** hide the self-app instead of rescanning, so one warm loader serves every dep compile
- **diag:** attribute the runner-extras bundle overhead with PhaseLog stages
- **tests:** schedule test collections heaviest-first to remove the single-threaded tail
- **diag:** add AL_RUNNER_PHASE_LOG, a per-app/bundle/process cost instrument
- **tests:** parallelize the 76 subprocess integration tests, fix the TOCTOUs it exposed
- **tests:** spawn al-runner.dll directly instead of dotnet run --no-build
- **AlRunner.Tests:** one artifacts gate, visible skips, CI-fatal when artifacts are absent
- **server:** calibrate ServerCancelTests' workload live instead of a fixed dev-box constant
- Add managed providers for Page Metadata and Page Control Field virtual tables
- **rad:** proportionality suite for --watch --rad delta compilation
- Merge remote-tracking branch 'origin/main' into mmv/analyze-v2-new-features
- Add new BC versions 28.2, 28.3, and 28.4
- Port protocol-v2 building blocks (types, error/stack utilities, testIsolation)
- AL Runner v2: full architecture cutover
- Update telemetry-triage.yml
- Handle codeunit generic args
- Fix HttpClient error envelope
- Fix Variant record Code conversion
- Fix dep compiler placeholder versions
- Handle NavIndirectValueToGenericType
- Fix FieldRef.Validate overload ambiguity
- Fix mock gaps in HTTP and list
- Fix mock gaps from System Application
- Fix void mocks in bool contexts
- add regression suite for Format(Rec.EnumField) enum-type resolution — closes #1507
- Regroup test suites into thematic categories under 3 buckets
- audit and tighten BC diagnostic suppression cases — closes #1365
- track AL method coverage at per-overload signature granularity
- add unit tests for AL diagnostic source filename formatting (follow-up to #1321)
- iterate all excluded test folders instead of hardcoding one fixture
- fix windows alc resolution in tests
- Fix/triage record aggregation
- Clarify README description about requirements
- compile --stubs in main BC pass, skip separate TranspileMulti
- cache reflection lookups in MockCodeunitHandle.Invoke and TryFireRecordTriggerCore — closes #1164
- v1.0.18 changelog
- v1.0.17 changelog
- Report properties — UseRequestPage/Language/FormatRegion
- Page<N>.RunModal + LookupMode instance-form coverage
- CaptionClassTranslate proof tests
- GetLastErrorCallStack proof tests
- SessionInformation.AITokensUsed proof tests
- strengthen weak test assertions — issue #203
- JsonValue remaining — AsBigInteger/AsByte/AsChar/AsCode/AsDate/AsDateTime/AsDuration/AsOption/AsTime
- JsonValue — AsToken/Clone/IsUndefined/Path + typed As* round-trips
- Variant Is* type-checking — extended coverage
- JsonArray typed getters — GetBigInteger/Byte/Char/Date/DateTime/Duration/Option/Time
- XmlDocument remaining methods — 17 tests for 13 API methods
- XmlNode — IsXml*, AsXml* type-cast, WriteTo, SelectNodes, SelectSingleNode, Remove, ReplaceWith, GetParent, GetDocument, AddAfterSelf, AddBeforeSelf
- JsonArray.Path + typed getter coverage notes
- XmlElement extended — GetChildNodes/GetParent/AddFirst/Remove/RemoveNodes/WriteTo
- XmlProcessingInstruction — Create/GetTarget/GetData/SetTarget/SetData
- XmlDeclaration — Create/Version/Encoding/Standalone coverage
- XmlAttributeCollection — Get/Set/Remove/RemoveAll coverage
- Text static — DelChr/DelStr/InsStr/LowerCase/UpperCase/MaxStrLen/StrLen/StrSubstNo
- XmlAttribute — Create/Name/Value/LocalName/NamespaceUri/AsXmlNode coverage
- XmlElement — 13 methods proven via BC native NavXmlElement
- JsonObject mutations — Add/Contains/Get/Clone/Keys/AsToken — extended coverage
- JsonArray mutations — Add/Set/Insert/RemoveAt/IndexOf coverage
- QueryInstance methods — GetFilter/Filters, ColumnCaption/Name/No, SaveAsJson
- JsonArray.Get + typed extraction (Boolean/Integer/Text/Decimal/Object/Array)
- Label.Split/Substring/PadLeft/PadRight/Remove/Trim*/IndexOfAny/LastIndexOf
- Text.Split (3 overloads) — proving tests
- Text.PadLeft/PadRight/Remove/Replace/Trim* coverage
- Text.Contains/StartsWith/EndsWith/IndexOf/LastIndexOf coverage
- System.CalcDate — proving tests
- Guid.CreateSequentialGuid — proving tests
- Label string-method coverage — Contains/StartsWith/EndsWith/ToLower/ToUpper/Trim/Replace/IndexOf
- ternary_expression — inline if-then-else coverage
- ErrorInfo.RecordId getter coverage
- ErrorInfo.CustomDimensions coverage — Dictionary get/set round-trip
- ErrorInfo.FieldNo coverage — get/set round-trip
- ErrorInfo.TableId coverage — get/set round-trip
- attribute_item — DataClassification, ObsoleteState on table fields
- ErrorInfo.DetailedMessage coverage — get/set round-trip
- ErrorInfo.Callstack coverage — works on default-initialised ErrorInfo
- visible_attribute_condition coverage — conditional Visible on page controls
- Byte.ToText proving tests
- unary_expression — comprehensive proving tests
- XmlComment.Create / Value / AsXmlNode coverage
- XmlElement.RemoveAllAttributes proving tests
- XmlDocument.SelectSingleNode / XmlElement.SelectSingleNode coverage
- Database.IsInWriteTransaction proving tests + coverage
- addafter_dataset_modification coverage — reportextension addafter(DataItem)
- List.RemoveRange proving tests + coverage
- Blob stream round-trip coverage (CreateInStream / CreateOutStream)
- Record.GetRangeMin / GetRangeMax proving tests
- XmlNodeList.Count / XmlAttributeCollection.Count / XmlElement.IsEmpty
- System.CurrentDateTime coverage — value sanity + monotonic reads
- JsonArray.Count coverage — empty, multi-element, mixed-type, nested
- Duration.ToText() coverage
- Today / Time built-in coverage
- add_dataset_modification coverage — reportextension add() in dataset area
- addfirst (no anchor) in views area — proving tests
- Dictionary type coverage — Add/Get/Set/Remove/ContainsKey/Count/Keys/Values
- CreateDateTime / DT2Date / DT2Time built-in coverage
- addlast_views_modification coverage — pageextension addlast() in views
- movefirst_modification coverage — pageextension movefirst() in layout
- movelast_modification coverage — pageextension movelast in layout area
- addbefore_dataset_modification coverage — reportextension addbefore() in dataset area
- moveafter_modification coverage — pageextension moveafter()
- movebefore_modification coverage — pageextension movebefore in layout area
- pageextension object comprehensive coverage — fields + actions + triggers
- RED — reportextension addlast() in dataset area
- addfirst_dataset_modification coverage — reportextension addfirst in dataset area
- addbefore_views_modification coverage — pageextension addbefore() in views area
- addafter(views) coverage — pageextension views modification
- modify_action_modification coverage — pageextension modify() in actions area
- addlast_action_modification coverage — pageextension addlast in actions area
- customaction coverage — customaction() declarations in pages
- addfirst_action_modification coverage — pageextension addfirst in actions area
- addbefore_action_modification coverage — pageextension addbefore in actions area
- addafter_action_modification coverage — pageextension addafter in actions area
- add grid section coverage for pages with grid layout sections
- views section coverage — view_definition and views_section
- RED — actionref page declaration causes Roslyn compilation failure
- add cuegroup_section coverage for RoleCenter and CardPart pages
- add addafter/addbefore pageextension modification coverage
- add dedicated schema_section coverage for XmlPort schema declarations
- add explicit tests for local interface variable declaration and dispatch
- TestField(Field, Value) scalar coverage — Text/Code/Integer/Decimal
- Record.Count / Record.CountApprox coverage
- EnumType.AsInteger / FromInteger proving tests
- RED — DT2Date / DT2Time / CreateDateTime proving tests
- Record.Init reset coverage
- RED — page with fixed() layout group, TestPage field access
- Database.SelectLatestVersion proving tests — no-op stub coverage
- Record.IsEmpty coverage
- Database.Commit proving tests — no-op stub coverage
- Record.SetAscending sort direction coverage
- Record.TestField with enum field coverage
- Record.FindSet SetCurrentKey iteration coverage
- Record.FindFirst / Record.FindLast coverage
- table_relation_expression syntax coverage
- Record.DeleteAll proving tests — delete all with/without filters
- Variable attribute syntax coverage — [Protected] and [InternallyVisible]
- Record.CopyFilters coverage
- fieldgroups section syntax coverage
- Record.Get proving tests — retrieve by primary key
- Enum.Names / Enum.Ordinals full coverage
- SetCurrentKey iteration-order coverage
- Field InitValue applied on Record.Init()
- Record.SetCurrentKey traversal-order coverage
- Count with SetFilter expressions coverage
- Record.Count with filters coverage
- SetFilter %1/%2 placeholder proving tests
- Record.FindLast() coverage
- Record.HasFilter coverage
- Record.IsTemporary() coverage
- Record.LockTable coverage
- RecordRef.FieldCount coverage
- Record.SetRange(Field) clears field filter
- TransferFields coverage
- lookup CalcFormula coverage
- enum extension coverage
- Merge PR #206: feat: AL language coverage map — two-layer (syntax + runtime-API)
- Suppress cross-extension AL0275/AL0197 false collisions
- Add SendNotificationHandler dispatch and TestPage method stubs
- [WIP] Fix version 28 pipeline test failures
- Fix Codeunit.Run with record parameter & StartSession record forwarding
- Generic catch-all error reporting across all pipeline stages
- Feat/add bc28 matrix
- Improve standalone report request page and test page support
- Add Query object support (MockQueryHandle)
- cold-start + warm-server optimizations (45%/61% faster)
- Add CHANGELOG entry for CS1503 ITreeObject fix
- Fix CS1503: replace `this` with `null!` in unhandled BC Nav* type constructors
- Fix CS1503: add `ALFind(DataError)` overload to `MockRecordRef` for no-arg `RecRef.Find()`
- Multi-target net8.0 and net9.0 for broader runtime compatibility
- Add --output-junit flag for JUnit XML CI test reporting
- Deduplicate repeated error blocks in PrintResults output
- Merge pull request #92 from StefanMaron/stefanmaron/fix-insert-primary-key-uniqueness
- Use PkValuesEqual in RowMatchesPrimaryKey; soften CHANGELOG wording
- Add summary line at end of test runs
- Merge pull request #90 from SShadowS/feat/iteration-sourcefile
- Address PR review: profile/controladdin regex, OnRun branch, comments
- Address review suggestions: comments, robust regex, unit tests
- Pass AL object name through ValueCapture chain
- Add tests for GetFileForScope prefix-match fallback
- Add sourceFile to captured values with prefix-match fallback
- Add sourceFile to captured values JSON output
- Register .app package sources with SourceFileMapper
- Add integration test: iteration JSON includes sourceFile
- Emit sourceFile in iteration JSON, refactor coverage to use SourceFileMapper
- Wire SourceFileMapper into pipeline input loading
- Add AL declaration parsing to SourceFileMapper
- Add SourceFileMapper for object-name-to-file mapping
- Merge pull request #88 from StefanMaron/stefanmaron/fix-mockoutstream-alassign-and-stream-co
- Merge pull request #87 from StefanMaron/copilot/add-differentiated-exit-codes
- Merge pull request #89 from StefanMaron/stefanmaron/issue-63-mockdialog-cannot-convert-to-navcomplexv-c666d5
- retrigger test matrix
- accept exit code 2 (runner limitation) as non-blocking in test matrix
- Initial plan
- add stable 'All BC versions passed' summary job for ruleset required check
- Remove silent source file exclusion during Roslyn compilation
- [WIP] Fix Format method to consider picture-string tokens
- Add stubs workflow documentation to --guide
- separate Copilot classification from body generation
- refactor telemetry triage: use GitHub Copilot for per-problem extraction
- fix time window: exclude current run from last-success lookup
- fix KQL: replace outerStackTrace with tostring(details)
- fix telemetry triage: switch AI query to POST, check closed issues
- Runner error reclassification, pipeline gap telemetry, and tests
- Merge pull request #61 from StefanMaron/stefanmaron/add-crash-telemetry
- Add telemetry triage workflow and script
- Add opt-in crash telemetry via Application Insights
- Merge pull request #60 from StefanMaron/stefanmaron/add-agent-working-rules
- Fix overloaded procedure dispatch in MockCodeunitHandle.Invoke
- RecRef.FieldIndex, RecRef.Caption, TestPage field Visible/Editable/Lookup/DrillDown, FieldRef.SetRange(Variant)
- Add feature request issue template
- Issue template: add AL reproduction code snippets
- Add issue template for runner gaps / missing mocks
- Rewrite ALDatabase.ALIsInWriteTransaction() to false (fixes NullReferenceException)
- GuiAllowed + FieldClass comparison + NavComplexValue rewrite (fixes #54)
- RecRef.Duplicate, RecRef/Record.ReadIsolation, InStream.ALAssign (fixes #53, #49)
- Session API: StartSession dispatches synchronously, StopSession/SessionActive stubs (fixes #50)
- RecRef.Duplicate, RecRef/Record.ReadIsolation, InStream.ALAssign (fixes #53, #49)
- Session API support: StartSession dispatches synchronously (fixes #50)
- Fix exit(this) in fluent-chaining codeunits (fixes #45)
- MockFormHandle stubs + TestPage GetAction (fixes #51, #52)
- v1.0.8 (JSON, BLOB/streams, Variable Storage, Codeunit.Run bool, NavScope fix)
- BLOB / InStream / OutStream support via in-memory mocks (fixes #46)
- BLOB / InStream / OutStream support via in-memory mocks (fixes #46)
- Support JSON types (JsonObject, JsonArray, JsonToken, JsonValue) (fixes #47)
- Rewrite NavScope to object in generated C# (fixes #44)
- Add built-in Library - Variable Storage stub (fixes #43)
- Add built-in Library - Variable Storage stub (fixes #43)
- Add TestPage Caption, First, GoToKey, Filter.SetFilter stubs (fixes #37)
- Codeunit.Run() returns bool + keeps outer OnRun wrapper (fixes #42)
- Add stub methods: TestPage Caption, RecRef SetLoadFields/Name, Page Update tests (#38, #39, #40, #41)
- Add MockRecordRef.ALAssign for RecordRef := assignment (fixes #35, #36)
- TestPage foundation — field access, actions, confirm/message handlers
- Update CLAUDE.md and CHANGELOG for TestPage + handler support
- Add more TestPage tests: question validation, field defaults, overwrite
- Add ConfirmHandler and MessageHandler dispatch (Phase 5)
- Add TestPage support (Phases 1-2): lifecycle, field access, actions
- Fix CompanyName/UserId NullReferenceException in standalone mode
- --generate-stubs command
- add SetFilter, GetTable, SetTable, full round-trip, and negative tests for RecordRef
- [Unreleased] — StrSubstNo integer fix (#33), iteration tracking (#34), coverage fixes
- Merge PR #34: per-iteration tracking for loop debugging
- Fix StrSubstNo with integer args (NullRef in NavSession) (fixes #33)
- remove EndIteration, finalize in EnterIteration and ExitLoop
- Revert "chore: add version tag to JSON output for debug verification"
- add integration test for iteration tracking
- v1.0.7 (#30 RecRef, #31 page helpers, #32 event subscribers)
- RecRef backed by in-memory store + page helper dispatch + event subscribers (fixes #30, #31, #32)
- v1.0.6 (#22-#29)
- Fire table OnInsert trigger via reflection (fixes #27)
- Enum.Names() via tagged NavOption + PK uniqueness on Insert (fixes #28, #29)
- ALGet tolerates Guid<->Text round-trip variants (fixes #26)
- Hyperlink no-op stub + DateFormula field default (fixes #24, #25)
- v1.0.5 (#19 option filter + #23 NavValueToString perf)
- Option-aware filter matching + NavValueToString fast paths (fixes #19, #23)
- v1.0.4 (#22 NavApp stub + #15 multi-field exist)
- NavApp.GetModuleInfo stub so tests don't crash on missing CodeAnalysis.dll (fixes #22)
- FlowField exist(): multi-field where clauses (fixes #15 follow-up)
- v1.0.3 adds #14-#21 fixes
- NumberSequence + FlowField exist() support (fixes #14, #15)
- Enum-implements-interface dispatch (fixes #20)
- Exclude tests/53 from bulk run (WIP #20)
- Renumber test codeunit/table/enum IDs to unique ranges
- RecordRef test: assert on explicit exit values, not IsEmpty truth
- full AND (&) support + exhaustive coverage (fixes #19)
- Loosen RecordRef open regression test for BC version compat
- Rec.Init applies field InitValue defaults (fixes #18)
- :X.Ordinals() / Names() via an AL-source enum registry (fixes #17)
- 3-arg Open + IsEmpty property (fixes #16)
- Stub NavFormHandle to MockFormHandle for Page variables (fixes #6, #21)
- v1.0.3 entries for #7-#13
- Per-statement variable capture (fixes #11)
- Server mode: add execute command for inline AL / run-mode (fixes #12)
- Exclude tests/46-missing-dep-hint/ from bulk AL test run
- Surface AL source column alongside the line (fixes #13)
- Multi-slot LRU cache + changedFiles in server response (part of #10)
- Create a GitHub Release on tag push, seeded from CHANGELOG.md
- Hint at namespace mismatch in missing-dep diagnostic (fixes #9)
- Add CHANGELOG.md and ship it with the NuGet package
- Demote AL0791 unknown-namespace `using` to a non-blocking diagnostic (fixes #8)
- Regression test for single-arg Record.Validate
- Build AlRunner.slnx in publish workflow so tests exist
- Mirror test-matrix invocation in publish workflow
- Stub NavRecordRef to a MockRecordRef so RecordRef locals compile (fixes #5)
- Support List of [Interface X] via MockObjectList<T> (fixes #3)
- Support [TryFunction] via TryInvoke on AlScope (fixes #4)
- Strip NavForm.RunModal/SetRecord statements (fixes #6)
- Merge pull request #2 from SShadowS/feat/message-and-value-capture-onrun
- Document new CLI flags in --help, --guide, and CLAUDE.md
- Add error line mapping via last-statement tracking and run C# tests in CI
- Add C# test infrastructure and implement requested features
- Load Microsoft.BusinessCentral.*.dll from service tier alongside Nav DLLs
- Compile dependency stubs separately to avoid package conflicts
- Fix --stubs to replace source objects instead of duplicating them
- Add CurrPage stub for page extensions and MockCurrPage class
- Fix NavEventScope → object type resolution in generated C#
- Make page extensions compilable to prevent cascade exclusions
- Rewrite ParentObject to Rec for table extensions, add 2-arg ALValidateSafe
- Support table extension OnValidate triggers and ParentObject
- Update CLAUDE.md with new test entries and implemented features
- Fix interface return from functions and nested interface dispatch
- Add ALModifyAllSafe to MockRecordHandle
- Add extension-scoped field overloads and ALRecordId to MockRecordHandle
- Run SourceLineMapper.Build in parallel with Roslyn compilation
- Use curated .NET runtime references instead of all System.*.dll
- Use RemoveSyntaxTrees for incremental retry compilation
- Overlap reference loading with rewriting stage
- Optimize rewriting stage: parallel execution + skip re-parse
- deduplicate test IDs/names for single-invocation CI (~3s vs ~75s)
- run all tests in single invocation (~1s vs ~75s)
- Update CLAUDE.md with new Assert methods, test cases, and known limitations
- Add Assert.ExpectedTestFieldError mock
- Fix NavTime formatting crash (NullReferenceException from NavSession)
- Add Assert.ExpectedErrorCode(Text) single-arg overload
- Fix ALCompiler.ToSecretText rewrite and NavSecretText storage in IsolatedStorage
- add negative tests and missing coverage for ExpectedError substring, record persistence
- add coverage for IsolatedStorage, TextBuilder, Validate trigger, table procedures, option fields
- Fix 6 categories of test failures found against real BC project
- add coverage for codeunit 130000 routing and codeunit assignment
- add comprehensive test cases for composite PK, sorting, filters, cross-codeunit, variant
- rename samples/ to tests/, remove old demos, add docs
- add comprehensive examples demonstrating all runner features
- Initial commit: extract AlRunner from alDirectCompile

## [2.2.0] - 2026-08-17

### Added
- --count-baseline so a shrunken run can no longer report green

### Fixed
- Field virtual table now reports declared ObsoleteState/ObsoleteReason
- TestPage field DrillDown() dispatches OnDrillDown
- MediaSet.ImportStream membership lost after Modify()+Get() (#1773)

### Changed
- chore(corpus): bump al-language pin to eb300810, and remap AssertError exceptions as BC does
- fix(testpage): SetValue(Boolean) on Rec-bound Boolean controls
- fix(server): classify inline AL code with BC's own parser, not a keyword prefix
- fix(testpage): resolve Enum-typed controls by Caption and refuse the member name
- feat(server): compile and run inline AL code from the execute command
- fix(page): materialise pages with Enum-typed controls via AlEnumMetadataRegistry
- fix(report): apply the caller's table view before OnPreReport so GetFilter() reads it
- fix(runtime): static Page.RunModal(0, Record) resolves the page via the table's LookupPageId
- feat(coverage): --coverage via BC's own StmtHit instrumentation
- fix(testpage): TestPage field Visible() must walk the enclosing group chain
- fix(watch): make the burst quiescence test deterministic, not real-clock-dependent
- fix(runtime): construct Page{id} for static Page.RunModal(id, Record)
- perf(startup): drop two JmpHook-era JIT guards; grant orchestrator GitHub access
- fix(watch): wait for quiescence instead of a fixed debounce
- fix(deps): honour the dependency's app.json manifest in source-dependency compiles
- fix(runtime): resolve AL objects against the current generation across a cross-app cycle boundary
- perf(tests): share one --server process per test class, not one per fact
- fix(compile): give BC's compiler a file system so ControlAddIn resources resolve
- perf(parser): parse each .al file once for all eight extractors
- fix(server): register per-request bundles in the cross-bundle AppId cache
- fix(parser): thread --define symbols into the table-metadata parser
- fix(cache): key ncl-cecil on the runner's content hash, not its mtime
- perf(startup): drop two JmpHook-era JIT guards, ~27% off every runner spawn
- fix(phase-log): stop the cohort-ratio false verdict; wire phase-log into --server mode
- fix(unit-tests): recover CollectionCostOrderer's stale weight table
- test(server): add reload regression coverage for #1860 (refuted)
- fix(xmlport): resolve the NavXmlPort orphaned-JmpHook cluster
- fix(cache): stop embedding the git commit SHA into al-runner.dll's bytes
- fix(record): run OnValidate triggers on tableextension-added fields
- chore(corpus): bump al-language pin dadffa2 -> 848831a
- perf: cache dependency Install-trigger + Company-Initialize baseline per dependency set
- fix(events): unwrap ByRef-wrapped publisher scope args for by-value subscriber parameters
- refactor: de-duplicate FindBucketRoot between Program.cs and WatchSource.cs
- fix(cache): compiled-deps / workspace-deps / ncl-cecil / bc-symbols honour --cache
- fix(testpage): eliminate literal Visible = false controls from the page
- fix(cache): content-address the bc-symbols cache key, not .app mtime
- fix(testpage): Boolean SetValue() works on page-variable-bound controls
- perf(phase-log): attribute the flat per-app-group tax inside test run
- fix(tests): make WatchTests' warm-timing assertion independent of pump scheduling
- fix(deps): discriminate genuine same-app reuse from AppId collisions
- fix(tests): replace ServerCancelTests' calibrated spin race with a deterministic barrier
- perf(record-patches): seed report ids from PE bytes instead of Assembly.GetTypes()
- docs(agents): rebut the run_in_background rationalization behind the stall
- docs(agents): name the no-poll guidance conflict that keeps killing agent work
- perf(unit-tests): --print-cache-key mode cuts CacheKeyDependencyClosureTests cost
- docs(agents): widen the no-backgrounding rule and split flake budgets asymmetrically
- runner-extras: consolidate 16 standalone bundles into one app
- perf(record-patches): batch AddSourceDir into one NCLMetadata cache pass
- docs(agents): land the unmerged rules branch, and stop agents re-running the whole suite before every push
- fix(ci): bump xunit.runner.visualstudio to 3.1.5 so a green run cannot exit 1
- perf(compiler): keep the warm loader when the dep set only narrows
- test(corpus): bump tests/al-language 15d18e8 -> dadffa2
- test(corpus): bump tests/al-language f915a4c -> 15d18e8
- perf(compiler): hide the self-app instead of rescanning, so one warm loader serves every dep compile
- perf(diag): attribute the runner-extras bundle overhead with PhaseLog stages
- perf(tests): schedule test collections heaviest-first to remove the single-threaded tail
- perf(diag): add AL_RUNNER_PHASE_LOG, a per-app/bundle/process cost instrument
- fix(cache): content-address the runner fingerprint in cache keys, add an explicit bc: line
- perf(tests): parallelize the 76 subprocess integration tests, fix the TOCTOUs it exposed
- fix(watch): arm FileSystemWatchers before announcing --watch is ready
- fix(tests): make the in-process BC engine tests actually execute on CI
- perf(tests): spawn al-runner.dll directly instead of dotnet run --no-build
- fix(cache): publish AL-output cache entries atomically
- test(AlRunner.Tests): one artifacts gate, visible skips, CI-fatal when artifacts are absent
- chore(corpus): bump al-language pin 9e75879 -> f915a4c
- fix(events): dispatch manually-declared IntegrationEvents from Page/Report/Query/XmlPort
- fix(mediaset): hook BC 27.x's synchronous AddMediaToSet, hard-error on unknown shapes
- docs(agents): fix impl-agent.md for v2 layout, add operational rules
- test(server): calibrate ServerCancelTests' workload live instead of a fixed dev-box constant
- fix(record): make NavRecord.GetCallerRecord track the real AL frame
- fix(reports): route static Report.Run/RunModal through real execution, not a dead JmpHook
- fix(testpage): TestPage.Caption() and field Caption() now return real captions
- fix(streams): match BC's Rename BLOB-loss for temporary records
- fix(test-executor): run [Test] procedures in AL source declaration order
- fix(events): dispatch manually-declared table-published IntegrationEvents
- Add managed providers for Page Metadata and Page Control Field virtual tables
- fix(enum): ingest declared Caption at emit time so Format(enum) returns it
- feat(server): add the cancel command — cooperative mid-run cancellation (#1641)
- chore(runner): remove dead protocol-v2 TestFilter record, add --test filter coverage
- fix(testpage): capture the insert position at New() so AutoSplitKey covers mid-grid, negative and typed keys
- chore(corpus): bump al-language pin to 9e75879, declare the four AutoSplitKey range gaps
- fix(record): resolve a CalcFormula where-condition whose field() names a FlowField
- fix(record): keep a database-backed row's BLOB out of the record that inserted it
- fix(testpage): number AutoSplitKey rows from the data, not from a constant
- fix(events): honor EventSubscriberInstance = Manual for table-event subscribers
- feat(server): populate protocol-v2 errorKind + stackFrames on streamed test events
- fix(events): honor EventSubscriberInstance = Manual in codeunit-event dispatch
- fix(record): apply the flow-filter family of CalcFormula where-conditions
- fix(testpage): action invoke saves the new subpage row, and AutoSplitKey assigns its key
- fix(record): CalcFields on a BLOB keeps an uncommitted in-memory write
- fix(page): page-driven Modify holds the before-image in xRec
- fix(report): Report.Run() iterates the data item, and SetTableView no longer NREs
- fix(record): Rename propagates through conditional and where-filtered TableRelations
- fix(deps): persist source-dep enum sidecar and build the symbols loader with zero package caches
- fix(expectations): expect-oos matches the message convention, add expect-divergence
- chore(corpus): bump al-language pin to c1a6733, retire migrated bundles, declare report gaps
- Add new BC versions 28.2, 28.3, and 28.4
- fix(expectations): wire the ExpectationManifest into the run (#1734)
- fix(page): bind Rec on a plain page-variable, not just TestPage
- fix(scheduler): CreateTask hits the documented NavCreateScheduledTasksNotAllowedException, not a codeunit-resolution error
- fix(record): Rename propagates to validated TableRelation fields
- fix(moduleinfo): GetCallerModuleInfo must name the caller across an app boundary
- feat(metadata): populate the Table Metadata virtual table; fix the docs-only CI bypass

## [2.1.2] - 2026-08-10

### Fixed
- close the seven gaps split out of the parser migration (#1708-#1714)

### Documentation
- the documented build command fails on a fresh clone

### Changed
- feat(parser): move the remaining six AL parsers onto BC's syntax tree
- fix(server): stale-green multi-bundle requests — per-request reset, content-stamped cache key, loud EMIT-EXCLUDED (#1706)
- feat(parser): parse AL tables with BC's own syntax tree, not regexes
- fix(loader): serve Microsoft.Dynamics.Nav.CodeAnalysis from the selected artifact dir, not bin

## [2.1.1] - 2026-08-08

### Changed
- fix(parser): AL comments no longer read as properties in the sibling parsers (#1697)
- fix(deps): name the dependency no loader tier can implement (#1689)
- fix(parser): AL comments no longer read as table/field properties (#1690)
- fix(pkgdedup): transient Windows move failure no longer kills the run (#1691)
- fix(reporter): --output-json crashed when two bundles shared a basename (#1692)

## [2.1.0] - 2026-08-07

### Fixed
- platform-table metadata lost when a dependency app extends the table
- correct filter-only query-column slot aliasing in multi-dataitem joins

### Changed
- fix(dispatch): dedupe module identity across bundles to fix event-subscriber TargetException
- fix(provision): fold bundle .alpackages into the platform-app R2R gate
- feat(server): stream runTests as protocol-v2 NDJSON (second slice of #1641)
- Port protocol-v2 building blocks (types, error/stack utilities, testIsolation)
- fix(win32stubs): never intercept Win32 imports on Windows
- feat(win32stubs): ship a prebuilt libwin32_stubs.so so Linux needs no C compiler
- fix(deps): resolve relative --package-cache dirs before pkgdedup symlinks

## [2.0.1] - 2026-08-07

### Documentation
- fix v1-to-v2-migration.md --version row (v1 rejects it, not supports it)

### Changed
- feat(cli): restore --test-timeout and clarify the --run -> --test/--filter redesign
- fix(record-metadata): strip quoted-identifier InitValue on enum fields so blank-named values evaluate
- fix(cli): --output-json redirects progress banners to stderr, purifying stdout
- fix(win32stubs): stop silently swallowing shim-build failures — throw loudly instead
- fix(cli): --test-isolation method now maps to per-test reset, not codeunit isolation
- fix(provisioning): --auto-provision downloads into the runner artifact cache, not the project's package cache
- fix(enum): merge enumextension values into Enum.Ordinals()/Names()
- fix(server): runTests/execute honour every sourcePaths entry, not just [0]
- fix(provisioning): DownloadArtifacts fails loud, not with a raw stack trace, on a 404
- fix(ci): Tests-updated gate now also matches AlRunner.Tests/

## [2.0.0.0] - 2026-08-05

### Changed
- feat(windows): real Windows support via VirtualProtect (#1650)
- fix(release): dedupe the duplicated v2.0.0 CHANGELOG section, make publish.yml idempotent
- fix(ci): publish.yml's test job was missing the platform-apps package cache

## [2.0.0] - 2026-08-05

### Fixed
- Record.Get with fewer key values than PK fields binds type defaults instead of prefix-matching
- make TableFieldRegistry.GetSourceTableId a pure reader to remove rewrite-phase race
- raise platform error when Record.Get receives more key values than PK fields — closes #1630

### Changed
- AL Runner v2: full architecture cutover
- Update telemetry-triage.yml
- publish.yml's test job was missing the platform-apps package cache

## [1.0.31] - 2026-05-06

### Fixed
- skip AL string literals when counting braces in StripPatternedBlock/StripNamedBlock — closes #1600
- emit auto-stubs in consumer using-namespace to resolve AL0118 for namespace-aware consumers
- exclude PagePartSyntax names from usercontrol stub injection guard — closes #1597
- drop event subscribers targeting missing codeunit to prevent CS0131 / BadExpression crash

## [1.0.30] - 2026-05-04

### Fixed
- BC-faithful error formats + clean ATL stub override (Library Management gaps)

## [1.0.29] - 2026-05-04

### Added
- auto-inject stub usercontrol + ControlAddin for stripped dep pages
- add --fail-on-stub flag to catch blank-shell stub and no-op test passes (issue #1519)
- support custom preprocessor symbols in compile-dep/extract-deps
- extract-deps --packages <dir> auto-discovers .app dep sources
- extract-deps — reachability-based dependency slicing from .app artifacts

### Fixed
- ensure enum auto-stubs include a synthetic value to prevent NRE in EnumExtensionTypeMetadataEmitter — closes #1590
- prevent CS0101 duplicate-class collision when source-dir input also exists in package cache
- derive CLEANSCHEMA defaultMax from BC application version, not hardcoded 25
- catch bare emit exceptions and write partial symbols.json on compile-dep failure
- strip DotNet procedures and skip pure assembly files in extract-deps — closes #1524
- evict stale dep DLL when .app content changes without version bump
- Report.SaveAs*/SaveAsPdf*/etc. static stubs return bool not void
- case-insensitive SubType = Test gate at Pipeline.cs:796 — closes #1520
- add ClearReference() to MockInterfaceHandle — closes #1565
- sharpen ScoreMethodMatch + broaden retry catch for auto-stub multi-overload dispatch (#1577)

### Documentation
- correct stale limitations — event subscribers and task scheduler both work

### Changed
- Handle codeunit generic args
- Fix HttpClient error envelope
- Fix Variant record Code conversion
- Fix dep compiler placeholder versions
- Handle NavIndirectValueToGenericType
- Fix FieldRef.Validate overload ambiguity
- Fix mock gaps in HTTP and list
- Fix mock gaps from System Application
- Fix void mocks in bool contexts
- fix(MockFile): ALOpen/ALErase/ALCopy return bool to fix CS0023 in boolean contexts (issue #1530)
- fix(MockFile): add 7-arg ALUpload overload to fix CS1501 (issue #1531)
- fix(MockHttpClient): replace ALUseDefaultNetworkWindowsAuthentication property with method to fix CS1955 (issue #1532)
- fix(MockHttpClient): add NavSecretText overloads for UseWindowsAuthentication and AddCertificate to fix CS1503 (issue #1533)
- fix(rewriter): skip ToText()→AlCompat.Format() for 0-arg user-defined methods

## [1.0.28] - 2026-04-27

### Added
- sweep 160 not-tested overloads to covered (issue #1400)

### Fixed
- Format() <Filler Character,N> directive emits nothing instead of literal token text
- MaxStrLen(Record.Field) returns declared length after InitValue/Get

### Documentation
- remove stale MockImage guide bullet and add SA boundary note
- audit 29 stub entries — add tests and justification notes
- audit not-possible/out-of-scope entries in coverage.yaml — add cross-references and re-classify StartSession overloads

### Changed
- test: add regression suite for Format(Rec.EnumField) enum-type resolution — closes #1507

## [1.0.27] - 2026-04-27

### Fixed
- revert MockImage SA reimplementation to blank shell
- merge same-arity overloads in auto-stub generator to prevent NavOption/NavCode cast
- Format(Option) renders member name and Text relational operators no longer NRE
- tighten ALSetAutoCalcFields wrapper signature to typed overload pair
- make ALMark(bool) return bool to resolve CS0019 in if-expression context — closes #1492
- initialize page Rec as temporary when SourceTableTemporary=true — closes #1490
- add ALEvaluate(ByRef<char>) overload — closes #1483
- triager must grep runtime before labelling telemetry issues needs-input
- add MockVariant/object overloads to MockFieldRef.ALValidate/ALValidateSafe — closes #1487
- persist TestPage Field.SetValue to underlying record — closes #1486
- tighten triager closing rule and add impl-agent claim race-condition check
- add MockJsonHelper integer-index overloads for JsonArray.GetText/GetInteger/GetDecimal/GetBoolean/GetArray — closes #1426
- initialize Rec backing on Page<N> var instantiation — closes #1422
- add TestPage.Filter.SetFilter object overload and regression tests — closes #1459
- preserve Enum/Option parameter types in auto-stubbed codeunits — closes #1419
- parse image dimensions from header — closes #1421
- add object/NavText overloads to MockTestPageFilter.ALSetFilter — closes #1442
- only discover tests with [NavTest] attribute, not by name — closes #1420
- preserve AutoIncrement and PK schema on auto-stubbed packaged tables — closes #1418
- prevent ByRef<int> CS1503 in auto-stub var-param signatures — closes #1433
- IsolatedStorage.Set, XmlNode.AddBeforeSelf/AddAfterSelf/Remove, ReportInstance.SaveAs return bool — closes #1432
- ALCompressArray returns int — closes #1446
- handle Duration to Integer implicit conversion in HttpClient.Timeout — closes #1445
- inject GetGlobalVariable/SetGlobalVariable on ReportExtension<N> — closes #1450
- add MockArray<T>.Clear(int) single-int overload — closes #1448
- implement TestRequestPage.GetDataItem — closes #1457
- implement missing CurrPage.Run() stub on Page<N> — closes #1444
- implement MockStream.ALWriteString — closes #1437
- implement ALStopSession 3-arg overload — closes #1443
- handle ALSystemVariable.ALEvaluate<MockVersion> CS0452 — closes #1429
- implement ALStartSession 3-arg overload — closes #1435
- implement ALTruncate 2-arg overload — closes #1431
- implement Report.Run 2-arg (StaticRun 2-arg) — closes #1427
- implement MockPartFormHandle.PageCaption — closes #1440
- implement ALFieldError 0-arg overload — closes #1428
- implement RecordRef.Insert(Boolean) and Insert(Boolean,Boolean) overloads — closes #1430
- implement MockHttpClient.ALAssign — closes #1447
- implement MockFieldRef.ALKeyIndex — closes #1434
- implement MockRecordRef.ALWritePermission — closes #1441
- shim ALAddLoadFields and ALAreFieldsLoaded on record wrapper — closes #1412
- type MockTestPageField.ALValue as string to match BC's TestPageField.Value — closes #1407
- rewrite MockArray<MockInterfaceHandle> Factory ctor to lambda — closes #1406
- qualify shadowed PromptMode enum reference inside page class — closes #1404
- shim ALSetLoadFields(DataError, params int[]) on record wrapper — closes #1405
- prove XmlAttribute.Create(Text, Text, Text) — closes #1399

### Documentation
- align CONTRIBUTING/CLAUDE bucket-loop with CI — closes #1409

### Changed
- Regroup test suites into thematic categories under 3 buckets

## [1.0.26] - 2026-04-26

### Added
- support app.json feature flags (NoImplicitWith, NoPromotedActionProperties, TranslationFile)
- Enhance AL diagnostic formatting with source filename support

### Fixed
- implement XmlAttributeCollection namespace-qualified overloads — closes #1376
- implement miscellaneous single-method gaps — closes #1382
- implement HttpContent.WriteFrom(Text/SecretText) and HttpHeaders.GetSecretValues(Text, List) — closes #1381
- implement ReportInstance/QueryInstance missing methods — closes #1379
- implement Report.Execute/Run/RunModal Text-name overloads and mark RunRequestPage(Integer,Text) covered — closes #1377
- implement Xml*.SelectNodes/SelectSingleNode with XmlNamespaceManager — closes #1371
- implement System missing overloads — closes #1375
- implement ErrorInfo/Dialog/FilterPageBuilder/TestField missing overloads — closes #1380
- implement Text/Label/TextConst missing overloads — closes #1378
- implement Page.Run/RunModal 3-arg overloads — closes #1374
- implement XmlDocument/Element/DocumentType missing overloads — closes #1372
- implement Xml*.WriteTo per-format overloads — closes #1370
- implement Table.FullyQualifiedName + mark Insert/FindSet/FieldError/TransferFields/CopyLinks overloads as covered — closes #1373
- implement Json.* per-primitive-type overloads — closes #1368
- implement Table.TestField typed and ErrorInfo overloads — closes #1369
- add MockObjectList.ALAssign for List of [RecordRef] var params — closes #1335
- detect duplicate pageextension names within same extension as AL0197 — closes #1345
- detect duplicate object names across apps as AL0197 — closes #1344
- add Report instance RunRequestPage 1-arg overload — closes #1333
- add ALTestFieldNavValueSafe object-arg overload — closes #1324
- route ALCompiler.NavValueToNavValue<T> through AlCompat for Date/Code/Text/Boolean filter fields — closes #1341
- add Page<N>.CallGetAutoFormatStringExtensionMethod/EnsureGlobalVariablesInitialized — closes #1332
- add Report.Run 3-arg overload (no systemPrinter) — closes #1336
- add ALTransferFields 3-arg overload — closes #1337
- inject ALRecordId/ALCurrentCompany/ALTestFieldNavValueSafe into Record classes — closes #1330
- add Page.EnqueueBackgroundTask 5-arg overload — closes #1327
- add ALViewFromStream 3-arg and 4-arg static overloads — closes #1331
- add StaticRunRequestPage 2-arg overload — closes #1329
- add MockVersion.ALCreate 2-arg and 3-arg overloads — closes #1323
- add MockReportHandle.ALAssign — closes #1328
- add RecordRef.ReadPermission/SetAutoCalcFields — closes #1326
- add MockPartFormHandle.Close/GetRecord — closes #1325
- add string overload to MockVersion.ALCreate — closes #1322
- add MockHttpClient.Clear() for global Clear(client) syntax — closes #1334

### Documentation
- clarify CHANGELOG.md is auto-generated from commits, not manually edited (closes #1340)

### Changed
- architecture: audit and tighten BC diagnostic suppression cases — closes #1365
- tooling: track AL method coverage at per-overload signature granularity
- test: add unit tests for AL diagnostic source filename formatting (follow-up to #1321)
- ci: iterate all excluded test folders instead of hardcoding one fixture

## [1.0.25] - 2026-04-24

### Added
- AL test count badge + remove redundant perf check step
- add Version.Create(Text) 1-arg ALCreate overload — closes #1296
- add missing ALFieldError, AutoFormat, EnsureGlobalVars on Record types

### Fixed
- route CalcDate through AlCompat to avoid NavNCLDateInvalidException
- bump AL compiler to v17.0 for BC 28 runtime 17 support — closes #1255
- add object overloads for string-expecting methods — closes #1297
- add object catch-all overloads for record operations — closes #1260
- add ALInvoke<T> extension for string receivers — closes #1298
- rewrite Page<N>.PromptMode static self-reference — closes #1266
- add BookmarkType, CheckType, and SetRecord stubs on Page classes — closes #1262
- add implicit string conversion to MockInStream — closes #1273
- add implicit int→MockFieldRef conversion for BC-emitted field numbers
- add 3-arg Invoke(extensionId, memberId, args) to MockFormHandle and MockReportHandle — closes #1282
- add DataError-prefixed overloads for field-level record methods
- add 3-arg overloads for JsonObject.GetInteger/GetBoolean/GetDecimal
- add string overload for AlCompat.CreateErrorInfo — closes #1278
- read application_Version instead of customDimensions for version
- don't reopen closed issues from telemetry
- add missing GetUrl overloads (1-arg, 2-arg) — closes #1299
- add Clear method to MockDataTransfer — closes #1269
- add CheckType no-op stub on Record classes — closes #1280
- rewrite MockRecordRef.Factory for RecordRef array declarations
- change MockMedia.ALMediaId from method to property
- remove MockDialog.ALUpdate(int, int) to resolve CS0121 NavValue/int ambiguity
- use source_indices instead of source_group_keys for row matching
- implicit MockRecordHandle to MockRecordRef conversion
- resolve Codeunit305002 ID collision between 96-validate-no-value and 305-filterpagebuilder-assign
- add 2-arg ALValidateSafe overload to injected Record class
- add ALAssign to MockFilterPageBuilder — closes #1276
- normalize Record/Codeunit types in triage aggregation

### Changed
- fix windows alc resolution in tests
- Fix/triage record aggregation

## [1.0.24] - 2026-04-24

### Fixed
- skip TableNo codeunits in OnRun; add --run-codeunit; remove implicit RunOnRun
- ALReadAs returns bool so 'if Content.ReadAs(T) then' compiles — closes #1250
- increase GitHub Models timeout to 120s, add 2 retries on transient timeouts
- anchor triage time window to last completed run, not last successful

## [1.0.23] - 2026-04-24

### Fixed
- rewrite ALCompiler.ToRecordRef to MockRecordRef.FromHandle
- NavObjectDictionary → MockObjectDictionary to lift ITreeObject constraint on codeunit-value dictionaries

## [1.0.22] - 2026-04-24

### Fixed
- **`NavApp.IsEntitled` accepts string literals** — `IsEntitled('standard_1')` no longer
  fails with CS1503 (`string` → `NavText`). String overloads added to the mock following
  the same pattern as `GetResource`. (#1231, #1236)
- **`CopyArray` on page-level `array[N] of Text[M]` vars** — page classes with fixed-length
  text array fields no longer produce CS0411 or NullReferenceException. The rewriter now
  preserves a cleaned `InitializeComponent` (field inits kept, BC-only calls stripped) so
  `MockArray` fields are properly initialised before use. (#1232, #1237)
- **Actionable error on access-denied** — writing the BC DLL cache to `%LOCALAPPDATA%`
  now prints a specific message pointing to antivirus / corporate security policies instead
  of a bare exception. (#1234, #1235)

### Documentation
- **Windows install prerequisites** — README now documents the NuGet feed workaround
  (`dotnet nuget add source`) for corporate environments where NuGet.org is not configured,
  and the required .NET SDK version (8, 9, or 10). (#1235)
- **Multi-root workspace usage** — README documents passing multiple source directories
  as separate arguments. (#1233)

## [1.0.21] - 2026-04-24

### Added
- **AL-level call stacks** — runtime errors now report procedure names and line numbers
  from AL source, not C# internals. `GetLastErrorCallStack()` returns a rendered AL frame
  list; `FormatStackFrames`/`FormatSingleFrame` drive the test-output rendering. DAP
  server now starts its listener in the constructor so `Port` is available before
  `RunAsync`, eliminating test port collisions. (#1206, #1208)

### Fixed
- **`--init-events` fires once, snapshot is the baseline** — `OnInstallAppPerDatabase/Company`
  and `OnCompanyInitialize` subscribers now fire a single time per run. After they complete,
  `MockRecordHandle._tables` and `MockIsolatedStorage` are snapshotted; test isolation
  restores from that snapshot between tests instead of clearing to empty. Dramatically
  reduces per-test cost when init-events are enabled. SingleInstance codeunit state
  remains session-scoped by design (subscribers must seed tables/IsolatedStorage).
  (#1220, #1227)
- **NavValue→NavCode coercion for `in` operator** — `CodeField in ['A', 'B']` no longer
  fails type resolution against a set of text literals. (#1211, #1224)
- **TestPage.GoToKey accepts TestField reference** — `GoToKey(TestField)` now resolves
  the field's current value and navigates. (#1215, #1226)
- **RecordRef.CurrentKeyIndex is settable** — assigning a new key index re-sorts the
  underlying row iteration. (#1218, #1225)
- **UploadIntoStream DataError-typed 2-arg overload** — `UploadIntoStream(DataError, OutStream)`
  compiles and runs. (#1213, #1214, #1223)
- **ReportExtension.GetDataItem / ParentObject** — stubbed so extension-side code
  compiles. (#1212, #1222)
- **Session-aware TestField.ALAsDateTime overload** — added; matches BC signature with
  explicit session parameter. (#1216, #1221)
- **UploadIntoStream 2-arg overload** — base `UploadIntoStream(Text, OutStream)` added.
  (#1210, #1219)

## [1.0.20] - 2026-04-23

### Added
- **In-memory Query support** — single-dataitem Query objects now work without the BC
  service tier. `Query.Open()` reads from the in-memory table store, `Query.Read()`
  iterates rows with column access, `SetFilter`/`SetRange`/`TopNumberOfRows` filter
  and limit results. Multi-dataitem JOINs and aggregation are not yet supported.
  (#1162, #1175)
- **DAP debugger (experimental)** — `--dap [port]` starts a Debug Adapter Protocol
  server for VS Code breakpoint debugging. Set breakpoints in AL source files and
  inspect variable values during test execution. (#528, #1011)
- **Library - Utility stub** (codeunit 131003) — `GenerateGUID`, `GenerateRandomCode`,
  `GenerateRandomCode20`, `GenerateRandomText` auto-loaded like Library Assert. (#1139, #1176)
- **`--generate-stubs` symbol table pass** — now queries the BC compiler's symbol table
  in addition to SymbolReference.json, discovering platform/system codeunits (Rest Client,
  No. Series, etc.) that have no SymbolReference.json entry. (#1163, #1193)
- **MockNotification.Default** — global `Notification` variables now initialize correctly. (#1189)
- **CurrReport.ObjectId(bool)** — available inside report triggers. (#1191)
- **MockPartFormHandle.SetTableView/Update** — page part operations compile and run. (#1186)
- **Media ImportStream/ExportStream** — stream-based overloads for Media fields. (#1190)
- **Tenant Media table support** — NavMediaSystemRecord base class handled by rewriter. (#1188)
- **MockRecordHandle.IConvertible** — prevents cast errors when auto-stubbed methods
  return default Record values used in primitive contexts. (#1161, #1201)

### Fixed
- **CreateDateTime/DT2Time timezone round-trip** — `CreateDateTime(D, T)` followed by
  `DT2Time()` now round-trips correctly on non-UTC hosts (Windows). Deterministic DST
  policy for ambiguous/invalid local times. (#1159, #1170)
- **ByRef\<MockVariant\> for Record arguments** — passing a Record to a `var Variant`
  parameter no longer throws. (#1160, #1173)
- **NavOption to NavText conversion** — Option field values can now be assigned to Text
  variables or passed to Text parameters. (#1199, #1205)
- **Dialog.Update NavCode ambiguity** — `Dialog.Update(fieldNo, codeValue)` no longer
  produces CS0121 ambiguity error. (#1179, #1198)
- **Variant-to-codeunit extraction** — `NavIndirectValueToNavCodeunitHandle` rewriter
  rule added for Variant holding a codeunit. (#1184, #1198)
- **Executor parameter count mismatch** — test methods with extra scope constructor
  parameters (from AL procedure params) no longer crash. (#1200, #1202)
- **MockVariant.Clear()** — `Clear(Variant)` now resets to default. (#1152, #1167)
- **CopyArray 3-arg overload** — `CopyArray(Dest, Source, FromIndex)` without count
  copies all remaining elements. (#1155, #1166)
- **GetPosition(Boolean)** — `Record.GetPosition(false)` uses field numbers,
  `GetPosition(true)` uses field names. (#1154, #1168)
- **Report.Run 4-arg overload** — `Report.Run(Id, RequestPage, SystemPrinter, Record)`
  compiles and runs. (#1156, #1169)
- **MockNotification.Recall returns bool** — was void, now returns true. ALAssign and
  Clear also added. (#1153, #1171)
- **Format(Record) returns position string** — was returning CLR type name. (#1161, #1172)
- **Clear on OutStream, File, record array element** — all three now have Clear()
  methods. (#1178, #1181, #1182, #1195)
- **Report.Execute(Text)**, **File.Create bool return**, **RecordRef.AddLink**,
  **RecordRef.GetView(bool)** — missing method overloads added. (#1180, #1183, #1187, #1192, #1194)

### Changed
- **README comprehensive rewrite** — clarifies that .app package code is not executed,
  adds Working with Dependencies section, DAP debugger docs, updated feature list. (#1204)

### Performance
- **Reflection caching** — `MockCodeunitHandle.Invoke` and `TryFireRecordTriggerCore`
  cache MethodInfo/FieldInfo lookups. Estimated 40-50% reduction in reflection overhead. (#1164, #1174)
- **Stubs in main BC pass** — `--stubs` sources now compile in the main TranspileMulti
  call instead of a separate compilation, saving ~2.3s per run. (#1165, #1177)

## [1.0.19] - 2026-04-23

### Added
- **Symbol-table auto-stub generation** — the headline feature of this release. After
  the main AL compilation, the runner queries the BC compiler's symbol table for every
  referenced codeunit and table that has no compiled class. It generates proper AL stubs
  with full method signatures (parameter types, `var` modifiers, return types) extracted
  from the symbol table, then compiles them in a second BC pass to produce scope classes
  with correct member IDs and default return values. This means calling methods on
  dependency objects (e.g., LibraryERM, Rest Client, No. Series) now returns proper
  typed defaults instead of null. The second pass only runs when stubs are needed —
  zero overhead for self-contained tests. The runner reports exactly which objects were
  auto-stubbed and from which packages.
- **AutoIncrement field support** — table fields with `AutoIncrement = true` now get
  `max(existing) + 1` automatically when inserted with value 0, matching BC behavior.
- **Full AL stack traces** — removed artificial 3/5 frame caps. Developers now see the
  complete AL call chain to diagnose failures.
- **`--compile-dep` dependency validation** — before attempting compilation, reads the
  .app manifest and checks all declared dependencies against available packages. If
  dependencies are missing, prints exactly which .app files are needed with publisher
  and version, instead of the cryptic "no C# code was generated" error.
- **Auto-stub transparency** — auto-stubbed codeunits are now listed by ID and name
  in the console output. When a test fails involving a stubbed codeunit, the output
  annotates the stack trace with guidance on how to compile real implementations
  with `--compile-dep`.
- **CI performance check** — each test bucket must complete within 60 seconds; CI
  fails fast if the runner regresses on performance.

### Changed
- **`--compile-dep` skips DotNet files** — files using DotNet interop (unsupported
  without BC service tier) are automatically excluded, allowing the remaining pure-AL
  objects to compile successfully.
- **Skip built-in stub IDs in `--generate-stubs` and auto-stub** — codeunits with
  runner-native implementations (Assert, Variable Storage, etc.) are no longer
  duplicated in generated stubs.

### Fixed
- **Auto-stub return type mismatch (#1150)** — when auto-stubs had many methods with
  the same parameter count, the dispatch fallback could pick the wrong overload, causing
  `InvalidCastException` (e.g., Int32 cast to NavCode). Symbol-table stubs with correct
  return types eliminate this class of error.

### Performance
- **Auto-stub package scanning** — only scans .app files until all missing codeunit IDs
  are found (early exit). Skips scanning entirely when no IDs are missing.

## [1.0.18] - 2026-04-22

### Added
- **Page<N>.RunModal / LookupMode / CurrPage members (#1079, #1082)** — generated page
  classes now have `RunModal()`, `LookupMode`, `Editable`, `PageCaption`, `PromptMode`,
  and `ObjectID()`. Fixes CS1061 and CS1503 Page→NavForm conversion errors.
- **TestField with ErrorInfo overloads (#1083, #1084, #1089)** — `TestField(Field, Value,
  ErrorInfo)` and `TestField(Field, ErrorInfo)` forms now compile. Adds `NavALErrorInfo`-
  specific overloads to `MockRecordHandle.ALTestFieldSafe`.
- **NavSecretText in HTTP patterns (#1086, #1091)** — `HttpContent.WriteFrom(SecretText)`,
  `HttpHeaders.Add(name, SecretText)`, and `TryAddWithoutValidation` now compile. Secrets
  treated as plain text in standalone mode.
- **ALGetResource 4-arg and Report.SaveAs 5/6-arg overloads (#1087, #1088)** —
  `NavApp.GetResource(Name, InStream, Encoding)` and `Report.SaveAs(Id, RequestData,
  Format, OutStream, RecordRef)` forms now compile as no-op stubs.
- **NavList<NavText> → MockArray conversion (#1080)** — `HttpHeaders.GetValues` with
  `List of [Text]` parameter now compiles via a `NavList<NavText>` overload.
- **XmlDocument.ReadFrom(InStream) (#1081)** — rewriter redirects `NavXmlDocument.ALReadFrom`
  to `AlCompat.XmlDocumentReadFrom` with MockInStream/NavText/string overloads.
- **AlScope.Parent static stub (#1092)** — fixes CS0117 when BC compiler emits static
  `AlScope.Parent` access in certain scope class patterns.
- **Telemetry: AL source line in CompilationGap (#1093)** — telemetry now includes the
  sanitized AL source line that triggered each compilation error (string literals replaced
  with `'...'`). Enables fully actionable issue creation without source access.

### Changed
- **Telemetry dedup precision (#1074, #1077, #1078)** — CS1503 keys now show both types
  (`'FromType' → 'ToType'`), CS1501 shows arg count, CS0117 shows member name, CS1729/1674
  show constructor args. Generated type IDs normalized (`Page<N>` not `Page72336585`).
- **Triage script grouping (#1075, #1076)** — generated type IDs in triage grouping
  normalized to `<N>` so all pages with the same missing member collapse to one issue.

## [1.0.17] - 2026-04-22

### Added
- **JsonObject.GetText(key, bool) and JsonArray.GetObject(int) overloads (#1025)** —
  `GetText(key, requireValueExists)` throws when key is missing and `true`, returns
  empty when `false`. `GetObject(index)` retrieves by integer index from arrays.
- **ALUploadIntoStream 4-arg overload (#1021)** — the BC 4-arg AL form
  `UploadIntoStream(Title, Filter, FileName, InStream)` (without fromFolder) now
  compiles. No-op stub matching existing behavior.
- **Report.RunModal 2/3/4-arg static overloads (#1043)** — all four overloads of
  `Report.RunModal` now compile and dispatch to handler when registered.
- **Page.GetPart(partHash) for subpage access (#1042)** — generated page classes now
  support `CurrPage.SubPart.Page.MyProc()` chained calls via `MockPagePartHandle`.
- **CurrReport.Preview / PreviewCanPrint stubs (#1055)** — stub properties returning
  `false` so report code referencing preview mode compiles without CS1061.
- **Database.KeyGroupEnabled/Disable/Enable stubs (#1054)** — `KeyGroupEnabled`
  returns `true`; `KeyGroupDisable`/`KeyGroupEnable` are no-ops. Forward-compatible
  with newer AL compiler versions.
- **customaction FlowTemplateGallery coverage (#1044)** — pages with Power Automate
  `customaction` blocks compile; BC emits no C# for these elements.
- **Auto-detect .alpackages folder (#1033)** — when `--packages` is not specified,
  the runner now auto-discovers `.alpackages` directories adjacent to source paths.
  No manual `--packages` flag needed for standard BC project layouts.
- **Coverage gap audit (#1053)** — reflected over BC Service Tier DLLs to identify
  undocumented coverage; added 11 entries to coverage.yaml (7 already covered but
  undocumented, 4 new gaps now resolved).

### Fixed
- **CS0121 ambiguous ALTestFieldSafe overload (#1018)** — consolidated type-specific
  `ALTestFieldSafe` overloads (bool/string/int/Decimal18) into a single `object`
  catch-all. Same proven pattern as the earlier ALSetRange fix.
- **NavOutStream/MockOutStream on chained calls (#1026)** — `TempBlob.CreateOutStream().Write(...)`
  now works. Rewriter redirects `ALCompiler.ObjectToNavOutStream/InStream` to
  `AlCompat.ObjectToMockOutStream/InStream`.
- **ReportExtension scope _parent assignment (#1013)** — nested BC inner types like
  `RequestPageExtension` inside `ReportExtension` now get `_parent` assigned in their
  constructor, fixing NullReferenceException when trigger bodies access parent fields.
- **Silent object removal from compilation (#1040)** — the pipeline no longer drops
  objects whose rewriter throws. Failed objects get a minimal fallback class so
  dependent objects can still compile and tests are not silently absent.
- **Duplicate object errors with .alpackages (#1034)** — when .alpackages contains
  a compiled .app of the same extension being compiled from source, the runner now
  skips the redundant package reference. Source always wins.

### Changed
- **Telemetry enrichment (#1039)** — CompilationGap messages now include specific
  missing member names (e.g., `CS1061: missing 'ParentObject', 'GetPart'`), AL line
  hints are preserved through scrubbing, RuntimeGap includes test codeunit/procedure
  identity, and RewriterGap includes the AL object type prefix.
- **README streamlined (#1032)** — README trimmed from ~296 to ~170 lines; detailed
  feature lists moved to docs/coverage.yaml and `--guide`. Fixed stale InitValue
  claim in docs/limitations.md.
- **Agent prompt improvements** — implementation agents now required to track coverage
  at overload level and check for merge conflicts after PR creation.

### Added
- **`actionref_declaration` coverage (#388)** — Pages and page extensions containing
  `actionref` sections (promoted-action bindings) now compile and run correctly.
  The existing `RoslynRewriter` already handles the BC-generated C# for actionref
  (the actionref declarations are inside `InitializeComponent` which is stripped).
  New suite `tests/bucket-1/72-actionref` adds 2 proving tests confirming that a
  codeunit in the same compilation unit as a page with actionref compiles and executes.
  Coverage map: `actionref_declaration` moved from `gap` to `covered`.
- **`Database.SelectLatestVersion` coverage (#313)** — `SelectLatestVersion()` was already
  a no-op (stripped by `StripEntireCallMethods` in `RoslynRewriter`) but had no proving
  tests. New suite `tests/bucket-1/58-database-select-latest-version` adds 3 tests:
  call without error, call after insert still shows record with correct field value,
  multiple calls are all no-ops. Coverage map: `Database.SelectLatestVersion` moved
  from `gap` to `covered`.
- **`UserId()` default stub value fix (#314)** — `AlScope.UserId` previously defaulted
  to `""` (empty string), causing `Assert.AreNotEqual('', UserId())` to fail. Changed
  default to `"TESTUSER"` so AL code calling `UserId()` gets a stable non-empty value
  without requiring the `--user-id` CLI flag. New suite `tests/bucket-1/57-userid`
  adds 2 proving tests: UserId is non-empty, UserId is consistent across calls.
  Coverage map: `Database.UserId` moved from `gap` to `covered`.
- **`Database.SessionId` (#316)** — `SessionId()` now returns a stable non-zero integer (1).
  The BC compiler lowers this global function to `ALDatabase.ALSessionId` (a property access).
  `RoslynRewriter` now redirects that to `MockSession.GetSessionId()`. New suite
  `tests/bucket-1/58-database-sessionid` proves: positive call returns > 0, and consecutive
  calls return the same value. Coverage map: `Database.SessionId` moved from `gap` to `stub`.
- **`Guid.IsNullGuid` (#318)** — `IsNullGuid(G)` now correctly returns `true` for the
  all-zeros GUID and `false` for any non-zero GUID. The BC compiler lowers this global
  function to `ALDatabase.ALIsNullGuid(G)`; `RoslynRewriter` now redirects that to
  `AlCompat.ALIsNullGuid(G)`, which checks `NavGuid.ToGuid() == Guid.Empty`. New suite
  `tests/bucket-1/58-is-null-guid` proves both directions. Coverage map: `Guid.IsNullGuid`
  moved from `gap` to `covered`.
- **`Record.IsEmpty` coverage (#299)** — `ALIsEmpty` was already implemented in
  `MockRecordHandle` (`GetFilteredAndMarkedRecords().Count == 0`) but had no proving
  tests. New suite `tests/bucket-1/56-isempty` adds 5 proving tests: empty table →
  true, records exist → false, filter excludes all → true, filter matches some →
  false, Reset clears filter → false. Coverage map: `Table.IsEmpty` moved from `gap`
  to `covered`.
- **`Guid.CreateGuid` (#310)** — `CreateGuid()` now returns unique `NavGuid` values.
  The BC compiler lowers this global function to `ALDatabase.ALCreateGuid()`.
  `RoslynRewriter` now redirects that call to `AlCompat.ALCreateGuid()`, which wraps
  `System.Guid.NewGuid()`. New suite `tests/bucket-1/57-create-guid` proves the function
  returns a non-empty GUID, that two calls return distinct values, and that calling via
  a codeunit helper works correctly. Coverage map: `Guid.CreateGuid` moved from `gap`
  to `covered`. (`CreateSequentialGuid` remains a gap — see #318.)
- **`Database.Commit` coverage (#311)** — `Commit()` was already a no-op (stripped by
  `StripEntireCallMethods` in `RoslynRewriter`) but had no proving tests. New suite
  `tests/bucket-1/56-database-commit` adds 4 tests: commit without error, commit after
  insert preserves records, multiple commits are all no-ops, commit after modify preserves
  modified values. Coverage map: `Database.Commit` moved from `gap` to `covered`.
- **`Table.SetAscending` coverage (#305)** — New suite `tests/bucket-1/55-setascending`
  adds 6 proving tests: default PK ascending, `SetAscending(Name, false)` → descending,
  explicit `SetAscending(Name, true)`, composite key with mixed directions (Priority asc +
  Code desc), `Reset()` restores default ascending, and `FindLast` with descending key.
  Coverage map: `Table.SetAscending` moved from `gap` to `covered`.
- **`Table.TestField` enum coverage (#302)** — New suite `tests/bucket-1/54-testfield-enum`
  adds 6 proving tests for `TestField` with enum fields: matching enum value passes,
  wrong value errors, default-vs-non-zero, non-default passes the no-value check, and
  default value fails the no-value check. Coverage map: `Table.TestField` moved from
  `gap` to `covered` (also surfaces the existing `27-testfield-error` suite).
- **`Table.FindSet` / `SetCurrentKey` iteration coverage (#301)** — New suite
  `tests/bucket-1/53-findset` adds 8 proving tests: PK-order iteration (no
  `SetCurrentKey`), Name-key iteration, Priority-key iteration, filter+key
  combined, `FindSet` returns true/false, empty table → false, no-match filter
  → false, and `FindLast` with `SetCurrentKey`. `SetCurrentKey` sort was already
  wired via the PK-sort fix in #297. Coverage map: `Table.FindSet` moved from
  `gap` to `covered`.
- **`Table.FindFirst` coverage + PK sort fix (#297)** — New suite
  `tests/bucket-1/52-findfirst` adds 7 proving tests for `FindFirst`. The tests
  revealed a bug: `GetFilteredRecords` only sorted when `SetCurrentKey` had been
  called; without it, records were returned in insertion order instead of PK order.
  Fixed `GetFilteredRecords` to always sort — by `_currentKeyFields` when set,
  falling back to PK fields otherwise (matches BC behaviour). Also marks
  `Table.FindLast` as covered (existing suite `tests/bucket-1/258-findlast`).
  Coverage map: both `Table.FindFirst` and `Table.FindLast` moved from `gap` to
  `covered`.
- **`Table.ModifyAll` coverage.yaml fix (#292)** — `ALModifyAllSafe` was already
  implemented in `MockRecordHandle`; coverage map incorrectly listed it as `gap`.
  Existing suite `tests/bucket-1/30-modify-all` has 4 proving tests (update all,
  filter-scoped update, empty table no-op, runTrigger overload). Coverage map:
  `Table.ModifyAll` moved from `gap` to `covered`.
- **`Record.CalcSums` implementation (#293)** — `ALCalcSums` was a no-op stub; now
  sums each requested field across all records matching the current filters and writes
  the result back into the record's fields. Integer fields stay `NavInteger`; Decimal
  fields become `NavDecimal`. New suite `tests/bucket-1/51-calcsums` adds 6 proving
  tests: Decimal sum, Integer sum, filtered sum, multi-field sum, empty result (→ 0),
  and filter-excludes-all (→ 0). Coverage map: `Table.CalcSums` moved from `gap` to
  `covered`.
- **`Record.Copy` fix + coverage (#295)** — `ALCopy` only copied filters when
  `shareFilters=true` (wrong parameter name and wrong default). Fixed: filters
  are now always copied (AL `Copy` always transfers both field values and
  filters). Added ShareTable=true support for temporary records: when
  `shareTable=true` both temp record variables share the same row list so that
  inserts/deletes via one are visible via the other. New suite
  `tests/bucket-1/51-record-copy` adds 7 proving tests: field value transfer,
  filter transfer (GetFilters match, Count restricted), ShareTable=true shares
  temp data and new inserts are visible in both, ShareTable=false creates
  independent temp copies, and source filters not mutated by target changes.
  Coverage map: `Table.Copy` moved from `gap` to `covered`.
- **`Record.DeleteAll` coverage (#289)** — `ALDeleteAll` was already fully
  implemented in `MockRecordHandle` (no-filter variant clears all rows; filter
  variant removes only matching rows). New suite `tests/bucket-1/50-deleteall`
  adds 6 proving tests: delete all, SetRange partial delete, SetFilter partial
  delete, empty table (no error), non-matching filter on empty table, and
  count-after-partial-delete. Coverage map: `Table.DeleteAll` moved from `gap`
  to `covered`.
- **`table_relation_expression` syntax coverage (#285)** — tables with
  `TableRelation` field properties compile and all record operations work.
  New suite `tests/bucket-1/49-tablerelation` adds 6 tests: Insert+Get
  with no parent (FK not enforced), Insert with existing parent, Modify,
  Delete, Count with filter, and the explicit negative test proving orphan
  inserts succeed without error. Coverage map:
  `table_relation_expression` moved from `gap` to `covered`.
- **`Table.Validate` coverage.yaml fix (#270)** — `Table.Validate` was
  listed as `status: gap` in `docs/coverage.yaml` despite 5 proving tests
  already existing in `tests/bucket-1/18-validate-trigger` (OnValidate fires
  on name-uppercase, computed amount, direct-Validate, direct-assign-skips,
  zero-quantity). Corrected status to `covered`.
- **`Record.SetRecFilter` composite-PK fix + coverage (#286)** — `ALSetRecFilter`
  previously only filtered on field 1 of the PK, leaving composite-PK records
  under-filtered. Fixed to iterate all PK fields via `GetPrimaryKeyFields()` and
  set a range filter on each. New suite `tests/bucket-1/49-setrecfilter` adds 7
  proving tests: single-field PK (Count=1, FindSet iterates only current record,
  correct field data returned), composite PK (Count=1, correct row isolated),
  Reset clears the filter, and SetRecFilter on one variable does not affect
  another variable. Coverage map: `Table.SetRecFilter` moved from `gap` to `covered`.
- **`Table.CopyFilters` coverage (#274)** — `ALCopyFilters` was already
  implemented in `MockRecordHandle` but had no proving tests. New suite
  `tests/bucket-1/48-copyfilters` adds 7 tests: SetRange transfer, SetFilter
  expression transfer, multi-field transfer, overwrite of existing target
  filters, Count respects copied filter, empty source clears target filters,
  and source is not mutated by the copy. RED confirmed by temporarily no-oping
  `ALCopyFilters`. Coverage map: `Table.CopyFilters` moved from `gap` to
  `covered`.
- **`Record.Rename` coverage (#281)** — `MockRecordHandle.ALRename` was already
  fully implemented but `Table.Rename` remained `status: gap` in coverage.yaml
  because the existing `tests/bucket-2/107-rename` suite was not registered.
  That suite provides 9 proving tests: single-field PK update + old-key removal,
  composite PK rename, duplicate-key error, non-existent-record error, return-value
  (false) variants, and count-preservation. Coverage map: `Table.Rename` moved
  from `gap` to `covered`.
- **`fieldgroups` section syntax coverage (#279)** — tables with `fieldgroups`
  declarations (e.g. `DropDown`, `Brick`) compile and all record operations
  work correctly. New suite `tests/bucket-1/48-fieldgroups`: 5 positive tests
  (Insert+Get, Modify, Delete, FindSet iteration, Count) and 2 negative tests
  (Get non-existent key, duplicate-key Insert error). Coverage map:
  `fieldgroup_declaration` and `fieldgroups_section` moved from `gap` to `covered`.
- **`Table.SetCurrentKey` iteration-order coverage (#223)** — `ALSetCurrentKey`
  was already implemented in `MockRecordHandle` but had no proving tests that
  verify `FindSet`/`Next` actually returns records in the specified field order.
  Adds four new tests to `tests/bucket-2/109-currentkey`: sort by Name
  (ascending), sort by Sequence (ascending), default PK sort (no SetCurrentKey),
  and descending Name order via `SetAscending`. RED confirmed by temporarily
  setting `if (false && _currentKeyFields ...)` — all four new tests fail.
  Coverage map: `Table.SetCurrentKey` moved from `gap` to `covered`.
- **`Enum.Names` / `Enum.Ordinals` coverage (#271)** — both were `status: gap`
  in coverage.yaml despite having basic suites. Extended `tests/bucket-2/61-enum-names`
  with 4 new proving tests: second/third name by index, `Contains` positive, type-qualifier
  syntax (`Enum::"T".Names()`), and 2 negative tests (unknown name not contained,
  count ≠ 2). Extended `tests/bucket-2/50-enum-ordinals` with 6 new proving tests:
  all 4 ordinals by index, `Contains` positive, instance-variable syntax, and 2 negative
  tests (ordinal 9 not contained, count ≠ 3). Coverage map: `EnumType.Names` and
  `EnumType.Ordinals` moved from `gap` to `covered`.
- **Test coverage: `Record.Get` by primary key (#275)** — `MockRecordHandle.ALGet` was
  already implemented; new suite `tests/bucket-1/48-record-get` adds 6 proving tests:
  single-key Get retrieves correct record, Get returns true on match, Get on missing key
  throws "not found" error, Get distinguishes between different keys, composite PK Get
  loads the correct row, and composite-key not-found also errors correctly.
- **Record.Count with SetFilter expressions coverage (#260)** — `Count` with
  SetFilter comparators / OR-lists / range expressions was already honoured
  in `MockRecordHandle.ALCount` but had no dedicated proving test. New suite
  `tests/bucket-1/46-count-setfilter` covers `'>1'`, `'<2'`, `'<>2'`,
  `'1|3'`, `'2..3'`, no-match (0), and restoration after `Reset`. RED
  confirmed by pointing `ALCount` at the unfiltered row list.
- **Field `InitValue` property applied on `Record.Init()` (#237)** — `MockRecordHandle.ALInit()`
  now calls `TableInitValueRegistry.ApplyInitValues` which parses `InitValue = X` attributes
  from AL field declarations and applies them when `Init()` is called. Supports Integer,
  Text, Boolean, Decimal, and Enum field types. Fields without `InitValue` continue to
  receive type defaults (0, empty string, false). New suite `tests/bucket-1/47-initvalue`
  covers Integer/Text/Boolean/Decimal InitValue application, fields-without-InitValue
  staying at defaults, and `Init()` overwriting previously-set values.
- **`Record.Next(Steps)` overload (#262)** — `MockRecordHandle.ALNext(int)` is
  new (previously only the parameterless `ALNext()` existed). Positive steps
  move forward, negative steps move backward, and the return value is the
  signed number of steps actually moved — clamped to the remaining records
  at either end so the absolute return may be less than the request. Honors
  active filters (advances within the filtered result set). New suite
  `tests/bucket-1/45-next-steps` covers Next(1), Next(N) skip, past-end,
  at-end (returns 0), negative-step backward, past-start, and filter
  traversal. RED confirmed by compile error (overload missing). Coverage
  map: `Table.Next` moved from `gap` to `covered`.
- **Test coverage: `Record.FindLast()` (#258)** — `MockRecordHandle.ALFindLast`
  was already implemented; new suite `tests/bucket-1/258-findlast` adds 6
  proving tests: unfiltered positions to last PK, empty table returns false,
  filtered set returns filtered last, filter with no matches returns false,
  `SetFilter('<>Z')` proves filters are honoured (returns M, not Z), and
  `FindFirst`/`FindLast` return different records.
- **Record.Count with filters coverage (#257)** — `MockRecordHandle.ALCount`
  already honoured active filters via `GetFilteredAndMarkedRecords`, but had
  no dedicated proving test. New suite `tests/bucket-1/44-count-filtered`
  covers empty table (0), total count (5), filtered subset (3 for Status=1,
  2 for Status=2), zero-match filter, restoration after `Reset`, and range
  filter (`Amount` in 20..40). RED confirmed by temporarily pointing
  `ALCount` at the unfiltered row count. Coverage map: `Table.Count` moved
  from `gap` to `covered`.
- **`Record.SetCurrentKey` traversal-order coverage (#264)** — sort-order
  behavior was implemented in `MockRecordHandle` but had no proving tests for
  FindSet/Next traversal. Extended `tests/bucket-2/109-currentkey` with 5 new
  tests: SetCurrentKey by Name changes traversal order, SetCurrentKey by
  Sequence changes traversal order, resetting to primary key restores PK order,
  Name sort does not traverse in PK order (negative), and descending sort
  reverses traversal order.
- **Test coverage: `Record.IsTemporary()` (#254)** — `MockRecordHandle.ALIsTemporary`
  was already implemented; new suite `tests/bucket-1/254-record-istemporary`
  adds 5 proving tests: normal Record → false, `temporary` Record → true,
  stays temporary after Insert, temp store is isolated from the persisted
  table, and normal Record stays non-temporary after Insert.
- **Record.HasFilter coverage (#253)** — `MockRecordHandle.ALHasFilter`
  (`_filters.Count > 0`) now has dedicated proving tests. New suite
  `tests/bucket-1/43-hasfilter` covers fresh record (false), `SetRange`
  (true), `SetFilter` (true), `Reset()` (false), and clearing filters
  one-by-one (remains true until last cleared). RED confirmed by
  temporarily stubbing `ALHasFilter` to always return false. Coverage map:
  `Table.HasFilter` moved from `gap` to `covered`.
- **Record.LockTable coverage (#250)** — `ALLockTable` in `MockRecordHandle`
  is a correct no-op (the runner has no SQL transaction isolation) but
  previously had no proving test. New suite `tests/bucket-1/42-locktable`
  covers: LockTable does not throw on an empty table, subsequent Modify /
  Insert / Delete succeed, and repeated LockTable calls are idempotent. RED
  confirmed by temporarily making ALLockTable throw. Coverage map:
  `Table.LockTable` moved from `gap` to `covered`.
- **`CompanyName()` configurable (#242)** — was hard-coded to empty string.
  Now three-way configurable:
    * `--company-name <name>` CLI flag sets the default returned between tests.
    * AL tests can set it at runtime via the new stub codeunit
      `131100 "AL Runner Config"` → `SetCompanyName(Name: Text)`.
    * Defaults to empty string when neither is used (backwards-compatible).
  Per-test reset restores the CLI default so tests don't leak across each
  other. Rewriter maps `ALDatabase.ALCompanyName` to `MockSession.GetCompanyName()`.
  New suite `tests/bucket-1/242-company-name` covers default, set, clear,
  per-test reset, and composition with `StrSubstNo`.
- **`SetFilter` format-placeholder tests (#245)** — added proving tests for
  `SetFilter` with `%1`, `%2` substitution arguments: single placeholder (`>%1`),
  two-placeholder AND expression (`>%1&<%2`), wildcard suffix (`%1*`), exact
  equality, and integer field range. New suite
  `tests/bucket-1/44-setfilter-placeholder` confirms positive matches and
  negative exclusion.
- **`UserId()` configurable (#243)** — `UserId()` now returns the value set via
  the new `--user-id <value>` CLI flag (or `PipelineOptions.UserId`), defaulting
  to empty string for backwards compatibility. Tests that branch on user identity
  can now be driven with a configured user ID.
- **Record.GetFilters coverage & field-name fix (#246)** — `Record.GetFilters()`
  now emits real AL field names (e.g. `"Status: 1"`) instead of positional
  stubs (`"Field2: 1"`). `MockRecordHandle.GetFieldNameByNo` now prefers the
  transpile-time `TableFieldRegistry` metadata before falling back to the
  runtime-registered name dictionary. New suite
  `tests/bucket-1/41-getfilters` covers the empty case, single-field filter,
  combined multi-field filter, post-`Reset` clearing, and range-filter
  rendering (`1..5`). Coverage map: `Table.GetFilters` moved from `gap` to
  `covered`.
- **RecordRef.FieldCount coverage (#238)** — `MockRecordRef.ALFieldCount` and
  `MockRecordHandle.FieldCount` already preferred the schema field count
  (from `TableFieldRegistry`) over the runtime written-field count, but this
  behaviour was listed as a limitation and had no proving test. New suite
  `tests/bucket-1/40-recordref-fieldcount` covers fresh RecordRef (3 schema
  fields), invariance after writing, a different 5-field table, and the
  negative case that FieldCount is not the write count. Removed the
  `Record.FieldCount via RecordRef` row from `docs/limitations.md`.

### Changed
- **Coverage: `type_declaration` reclassified as out-of-scope (#232)** — this
  tree-sitter-al node is the `.NET` type alias declared inside
  `dotnet { assembly { type(...) {} } }` blocks (required field `dotnet_type`),
  not a general user-defined type alias. It requires BC runtime .NET interop,
  which is an architectural limit like `assembly_declaration`. Moved from
  `gap` to `out-of-scope` in `docs/coverage.yaml`.

### Added
- **Test coverage: `Record.SetRange(Field)` clears field filter (#240)** — new
  suite `tests/bucket-1/240-setrange-clear` proves that calling
  `SetRange(Field)` with no value argument removes only that field's filter
  while leaving other field filters intact, and that `FindSet()` iterates
  the full record set afterwards. Behaviour was already implemented in
  `MockRecordHandle.ALSetRangeSafe`; this adds the proving tests.
- **Record.TransferFields coverage (#224)** — `ALTransferFields` in
  `MockRecordHandle` now has dedicated proving tests. New suite
  `tests/bucket-1/39-transferfields` verifies matching fields are copied by
  field number (not name), the default overload copies the PK, the
  `TransferFields(src, false)` overload preserves the target's PK, and
  target-only fields (no counterpart on source) remain untouched. Coverage
  map: `Table.TransferFields` moved from `gap` to `covered`.
- **CalcField `lookup` formula coverage (#231)** — the `lookup(...)` CalcFormula
  kind in `MockRecordHandle.ALCalcFields` now has dedicated proving tests. New
  suite `tests/bucket-1/38-lookup-formula` exercises text lookup, decimal
  lookup, first-match disambiguation, and the no-match default path (empty
  text / zero decimal). Coverage map: `lookup_formula` moved from `gap` to
  `covered`.
- **Multi-token decimal format strings (#225)** — `Format(decimal, 0, '<Precision,2:2><Standard Format,0>')`
  now parses every `<...>` token in the picture string instead of only the
  first. For decimals, `<Precision,min:max>` wins over `<Standard Format,N>`
  when both are present. Single-token strings are unchanged. New suite
  `tests/bucket-1/225-format-multi-token` covers integer, fractional, and
  rounding cases.
- **`Record.FieldError` (#228)** — raises a field-level validation error
  (`"<FieldCaption> <Message> in <TableCaption>: <PK>"`). Supports both
  `FieldError(Field)` (default `"must have a value"` message) and
  `FieldError(Field, Text)`. Errors are catchable via `asserterror`.
- **Record.Mark / Record.MarkedOnly / Record.ClearMarks (#226)** — the
  record-variable marking surface is now functional (previously no-ops).
  `Mark(true/false)` flips the mark for the current record, `Mark()` returns
  the current state, `MarkedOnly(true)` filters subsequent `FindSet` /
  `FindFirst` / `FindLast` / `Next` / `Count` / `IsEmpty` iteration to the
  marked subset, and `ClearMarks()` wipes all marks. Marks are per
  record-variable instance, keyed on primary key values. New suite
  `tests/bucket-1/37-record-mark` exercises positive (marked subset),
  negative (MarkedOnly off), and reset (ClearMarks) paths.
- **Enum extension test coverage (#227)** — new suite
  `tests/bucket-1/36-enum-extension` confirms that `enumextension` objects
  transpile and run correctly: base enum values retain their ordinals,
  extension values resolve to their declared ordinals (100, 101), and
  `Format()` / `AsInteger()` work against extension members. Coverage map
  updated: `enumextension_declaration` moved from `gap` to `covered`.

## [1.0.15] - 2026-04-15

### Added
- **`--strict` flag** — New CLI flag that promotes exit code 2 (runner limitations)
  to exit code 1. In strict mode, any non-passing test fails the pipeline — use
  in CI to catch regressions where tests go from passing to blocked. Both CI
  workflows (test-matrix.yml and publish.yml) now use `--strict`.
- **AL language coverage map** — `docs/coverage.yaml` (machine-readable) and
  `docs/coverage.md` (rendered table) track every AL language construct from
  `tree-sitter-al` as covered, gap, not-possible, or out-of-scope. A generation
  script (`scripts/coverage-gen.js`) supports `--fetch`, `--render`, and
  `--validate` modes. CI validates that all covered entries reference existing
  test paths.
- **Runtime-API coverage layer (#202)** — the coverage map now has two layers.
  In addition to the syntax layer (tree-sitter constructs), a new `runtime-api`
  layer enumerates every BC built-in method from
  `Microsoft.Dynamics.Nav.CodeAnalysis` symbol tables via
  `tools/RuntimeApiEnumerator`, producing `scripts/runtime-api.json`
  (1294 methods across 95 types). `scripts/coverage-gen.js` scans
  `AlRunner/Runtime/Mock*.cs` + `AlScope.cs` for AL-prefixed methods to
  determine per-method coverage. Each `docs/coverage.yaml` entry now carries a
  `layer: syntax | runtime-api` field; curation is preserved across
  regenerations. CI runs `scripts/tests/coverage-gen.test.js` plus the schema
  validator.
- **HTTP mock types** — `NavHttpClient`, `NavHttpResponseMessage`, `NavHttpContent`,
  `NavHttpHeaders`, and `NavHttpRequestMessage` are replaced with in-memory mocks
  (`MockHttpClient`, `MockHttpResponseMessage`, `MockHttpContent`, `MockHttpHeaders`,
  `MockHttpRequestMessage`) that work without `NavSession`. `HttpContent.WriteFrom(Text)`
  / `ReadAs(var Text)` round-trips text. `HttpResponseMessage` defaults to status 200.
  `HttpHeaders.Add/Contains/Remove` work. `HttpClient.Send/Get/Post/Put/Delete/Patch`
  throw descriptive `NotSupportedException` recommending AL interface injection.
  `HttpContent.WriteFrom(InStream)` / `ReadAs(var InStream)` now round-trip content
  (previously ReadAs returned an empty stream). (#123)
- **RecordRef/FieldRef API completeness** — Mark/MarkedOnly/ClearMarks are now
  functional (in-memory HashSet tracking). FieldRef.GetFilter returns the active
  filter expression. FieldRef.GetRangeMin/GetRangeMax return the active range
  bounds. RecordRef.Ascending setter wires through to the handle's sort direction.
  FieldRef.Record() returns the owning RecordRef. RecordRef.KeyCount/KeyIndex/
  CurrentKeyIndex provide basic key metadata. (#115)
- **KeyRef support** — New `MockKeyRef` class replacing `NavKeyRef`. Provides
  FieldCount, FieldIndex(n), Record, Active, and ALAssign. The RoslynRewriter
  maps `NavKeyRef` → `MockKeyRef` with constructor arg stripping. (#115)
- **ReportHandler dispatch** — `[ReportHandler]` procedures now intercept `Report.Run()`,
  `Report.RunModal()`, and report variable `.Run()`/`.RunModal()` calls. The handler
  receives a `TestRequestPage` parameter, matching BC's test framework semantics.
  Static `Report.Run(id)` / `Report.RunModal(id)` calls (emitted as `NavReport.Run/RunModal`)
  are rewritten to `MockReportHandle.StaticRun/StaticRunModal`. `MockReportHandle.RunModal()`
  and `UseRequestPage(false)` (emitted as `UseRequestForm` property) are now supported.
  Running a report without a handler silently succeeds (no error). (#118)
- **SendNotificationHandler dispatch** — `HandlerRegistry` now supports
  `[SendNotificationHandler]` test handlers. `MockNotification.ALSend()` invokes
  the registered handler (passing a `ByRef<MockNotification>`) so tests can
  intercept and inspect `Notification.Send()` calls. Without a handler, Send
  remains a no-op. (#119)
- **TestPage method stubs** — `MockTestPageHandle` gains `ALEditable` (property,
  returns `true`), `ALValidationErrorCount()` (returns `0`), `ALLast()` (returns
  `false`), `ALPrevious()` (returns `false`), `ALExpand(bool)` (no-op), and
  `ALGetRecord()` (returns empty `MockRecordHandle`). These prevent CS1061
  compilation errors for common TestPage member accesses. (#119)
- **Field metadata infrastructure** — `TableFieldRegistry` now parses and stores
  field-level metadata (name, caption, type, length) and table-level metadata
  (name, caption) from AL source at transpile time. `MockRecordHandle.ALFieldCaption`,
  `ALTableCaption`, `ALTableName` return real values from the registry (falling back
  to stub defaults for unregistered tables). `MockFieldRef.ALName`, `ALCaption`,
  `ALType`, `ALLength` use the registry. `MockRecordRef.ALName` and `ALFieldCount`
  return schema-based values. `MockRecordHandle.FieldCount` returns the schema field
  count when metadata is available. Caption values with embedded apostrophes
  (e.g. `'Vendor''s Name'`) are unescaped correctly. (#114)
- **Temporary records** — `Record "X" temporary` variables now use an isolated in-memory
  store per handle instance, fully separated from non-temporary records of the same table.
  `IsTemporary()` returns the correct value. `RecordRef.Open(tableId, true)` creates a
  temporary RecordRef. (#120)
- **FlowField CalcFormula: count, sum, lookup** — `CalcFields` now evaluates `count(...)`,
  `sum(...)`, and `lookup(...)` formulas in addition to the existing `exist(...)` support.
  Count returns the number of matching rows, Sum aggregates a decimal field, and Lookup
  returns the target field value from the first matching row. (#120)
- **FieldRef enum introspection** — `MockFieldRef` now supports `ALIsEnum`,
  `ALOptionValueCount()`, `ALGetOptionValueName(index)`,
  `ALGetOptionValueCaption(index)`, and `ALGetOptionValueOrdinal(index)`.
  These methods use `TableFieldRegistry` (which now parses `Enum "X"` field
  type declarations) and `EnumRegistry.GetMembersByName()` to resolve enum
  metadata at runtime. (#126)
- **FieldRef.CalcSum** — `MockFieldRef.ALCalcSum()` sums a field's values
  across all filtered records in the underlying table. The result is returned
  via the next `ALValue` read, matching BC's CalcSum semantics. (#126)
- **RecordRef system-field number accessors** — Added `ALSystemCreatedAtNo`
  (2000000001), `ALSystemCreatedByNo` (2000000002), and `ALSystemModifiedByNo`
  (2000000004) to `MockRecordRef`. (#126)
- **ErrorInfo type & collectible errors** — `Error(ErrorInfo)` now uses
  `ErrorInfo.Message` for the error text (previously used `.ToString()` which
  included internal field metadata). Collectible errors are fully supported:
  mark `ErrorInfo.Collectible := true` and annotate procedures with
  `[ErrorBehavior(ErrorBehavior::Collect)]` to collect errors instead of
  throwing. Global functions `HasCollectedErrors()`, `GetCollectedErrors()`,
  `ClearCollectedErrors()`, and `IsCollectingErrors()` all work. (#117)
- **MockNotification** — In-memory replacement for `NavNotification`. Message,
  Send, Recall, SetData/GetData/HasData, AddAction, Id, Scope. Send and Recall
  are no-ops; data store is in-memory; Id auto-generates a Guid. (#121)
- **MockTaskScheduler** — CreateTask dispatches codeunit synchronously via
  MockCodeunitHandle (same pattern as MockSession.StartSession), returns a Guid.
  TaskExists returns false, CancelTask/SetTaskReady are no-ops. (#121)
- **MockDataTransfer** — Minimal stub so code using DataTransfer compiles and
  runs without error. SetTables, AddFieldValue, AddConstantValue, AddJoin,
  AddSourceFilter, CopyFields, CopyRows are all no-ops. (#121)
- **System, Database & Session utility stubs** — `Session.LogMessage()` (no-op),
  `Session.ApplicationArea()` (returns empty string), `Session.GetExecutionContext()` /
  `GetModuleExecutionContext()` (return `ExecutionContext.Normal`),
  `Database.LockTimeout(bool)` (no-op), `CompanyProperty.DisplayName()` / `UrlName()`
  (return stub company values), `RoundDateTime(dt, precision, direction)` (full implementation
  with ms precision and direction rounding). `ProductName.Full/Short/Marketing` use
  real BC types. `NormalDate/ClosingDate` wrappers added with explicit 0D handling. (#185)
- **ReportExtension scope class `Parent` property** — The rewriter now injects a
  public `Parent` property on scope classes (alongside the existing `_parent` field),
  fixing CS1061 errors when BC-generated report extension trigger scopes access
  `Parent` without the `base.` prefix. Also strips the broken `CurrReport` property
  on report extensions (which cast `ParentObject` from the removed base) and injects
  `CurrReport => this` as a self-referencing stub so `CurrReport.Skip()` /
  `CurrReport.Break()` still compile. (#177, #178, #179, #181)
- **Codeunit-not-found diagnostics** — When `Codeunit.Run(id)` fails because
  the target codeunit is absent from the assembly, the error message now:
  identifies system (1–9999) and test-toolkit (130000–139999) ranges, lists
  available codeunit IDs (up to 20), and suggests `--stubs` / `--generate-stubs`
  as resolution. (#176)
- **Cross-extension AL0275/AL0197 suppression** — When multiple AL source directories
  are compiled together (e.g., two extensions), false "ambiguous reference" (AL0275)
  and "already declared" (AL0197) errors from name collisions between different
  extensions are now suppressed. The classifier only suppresses extension object types
  (PageExtension, TableExtension, etc.) and uses a two-pass approach to avoid hiding
  genuine codeunit/table name collisions. (#182)

### Improved
- **XmlPort & Query runtime error messages** — `MockXmlPortHandle.Import/Export`
  and `MockQueryHandle.Open/Read` now throw descriptive `NotSupportedException`
  messages that mention "BC service tier" and suggest "AL interface injection"
  (XmlPort) or "Record operations" (Query) as actionable alternatives. (#124)

### Fixed
- **BigText mock (`MockBigText`)** — `NavBigText` is now replaced with `MockBigText`
  by the rewriter. In BC 28+, `NavBigText`'s static initializer loads
  `Microsoft.BusinessCentral.Telemetry.Abstractions` which is unavailable outside
  the service tier, causing `TypeInitializationException`. `MockBigText` provides
  the same API surface (`ALAddText`, `ALGetSubText`, `ALTextPos`, `ALLength`,
  `ALWrite`, `ALRead`) using a plain `StringBuilder`.
- **RoundDateTime avoids Telemetry.Abstractions** — `AlCompat.RoundDateTime` now
  uses `NavDateTime + Int64` (milliseconds) arithmetic instead of
  `NavDateTime.Create(DateTime)` which triggers `Telemetry.Abstractions` loading
  in BC 28+.
- **NavDateTime formatting** — `AlCompat.Format()` now handles `NavDateTime`
  values directly by casting to `DateTime`, avoiding the `NullReferenceException` in
  `NavDateTimeFormatter.GetStandardFormat` that occurred when `NavSession` was null.
  This fixes `Assert.AreEqual`/`AreNotEqual` comparisons involving DateTime values.

## [1.0.14] - 2026-04-14

### Added
- **Report `CurrReport.Skip()` and `CurrReport.Break()` support** — Report
  classes now include `Skip()` and `Break()` method stubs injected by the
  rewriter. Previously these calls caused CS1061 because the `NavReport` base
  class was stripped. (#168 related)
- **MockInStream: ALLength, ALPosition, ALResetPosition** — `MockInStream` now
  exposes `ALLength` (total stream length), `ALPosition` (current read position),
  and `ALResetPosition()` to reset the stream to the beginning. (#169)
- **MockRecordRef: 20+ missing methods** — Added `ALMark()`, `ALMarkedOnly`,
  `ALClearMarks`, `ALChangeCompany`, `ALAscending`, `ALHasFilter`, `ALGetFilters`,
  `ALGetPosition`, `ALSetPosition`, `ALRename`, `ALFieldExists`, `ALModifyAll`,
  `ALGetFilter`, `ALCurrentCompany` to `MockRecordRef`. (#170)
- **MockFile: ALUploadIntoStream / ALDownloadFromStream overloads** — Added the
  5-arg and 6-arg `ALUploadIntoStream` overloads (with dialog title, folder,
  filter, filename, and upload GUID) plus `ALDownloadFromStream` overloads.
  The rewriter now also redirects `NavFile.ALDownloadFromStream` to `MockFile`.
  (#171, #174)
- **MockFieldRef.ALSetTable** — No-op stub for `ALSetTable` emitted by BC
  compiler for page API extension code. (#172)
- **AlScope static stubs** — Added `ExitStatementNumber`, `MaxStackDepth`,
  `LastErrorCallStack`, `FindTryMethodScope()`, `MethodName()` static members
  to `AlScope` for NavMethodScope compatibility. (#173)
- **MockRecordHandle: FiltersActive, HasField** — `FiltersActive` property
  returns whether any SetRange/SetFilter is active. `HasField(int)` checks
  if a field has been set on the record.
- **Codeunit OnRun with record parameter** — `Codeunit.Run(codeunitId, record)`
  now correctly forwards the record to the target codeunit's `OnRun` trigger.
  Previously the record was silently dropped, causing `NullReferenceException`
  inside codeunits that declare `TableNo`. The rewriter now passes the 3rd
  argument of `NavCodeunit.RunCodeunit(DataError, id, record)` through to
  `MockCodeunitHandle.RunCodeunit(DataError, id, record)`. `RunCodeunitCore`
  looks up `OnRun(MockRecordHandle)` by exact signature and passes the record
  directly. `MockSession.StartSession` with a record parameter also forwards
  it to `OnRun`. Job-queue and batch-posting patterns that dispatch via
  `Codeunit.Run(codeunitId, rec)` now work correctly. (#135)
- **Generic catch-all runner-limitation detection** — The test executor now
  classifies additional exception types as runner limitations (`Status = Error`,
  `IsRunnerBug = true`) instead of misreporting them as test failures
  (`Status = Fail`):
  - `MissingMethodException` / `MissingMemberException` — a BC runtime method
    that has not yet been mocked by the runner.
  - Any exception whose call stack originates in `Microsoft.Dynamics.Nav.*` or
    `Microsoft.BusinessCentral.*` code — the BC service-tier context required
    by that method is not available in standalone mode.
  These cases now surface as `ERROR` with the "⚑ Runner limitation" hint
  and `IsRunnerBug = true` in JSON output, and contribute to exit code 2
  instead of exit code 1. (#131)
- **Per-object rewriter error handling** — When `RoslynRewriter.RewriteToTree`
  throws for an AL object (e.g. unexpected AL language construct in the C# AST),
  the runner now:
  - Catches the exception per-tree in the rewriter's `Parallel.For` loop
  - Reports a clean `⚑ These objects contain AL constructs not yet handled…` error
    block naming each failing object and the exception type/message
  - Populates `PipelineResult.RewriterErrors` so telemetry can include the gap
  - Fails with exit code 2 (runner limitation) instead of crashing with an
    unhandled `AggregateException`
- **Roslyn compilation failure hint** — When the C# compiler rejects the rewritten
  code, the error output now includes:
  `⚑ These errors may indicate AL constructs not yet handled by the runner's rewriter.`
  and a pointer to `--dump-rewritten` for debugging. Compiler errors are also
  stored in `PipelineResult.CompilationErrors` for telemetry.
- **Telemetry covers all pipeline stages** — `TelemetryReporter.TryReportPipelineGapsAsync`
  now accepts and reports rewriter gaps (`RewriterErrors`) and compilation gaps
  (`CompilationErrors`) in addition to runtime gaps, all in a single combined prompt.
- **Compilation error deduplication in telemetry** — Compilation errors are now
  grouped by CS error code + target type before display and telemetry send. E.g.
  74 CS1061 errors on `Report70400` collapse into one line `CS1061 on 'Report70400' (74×)`.
  Each deduplicated group is sent as its own telemetry report (instead of one
  joined blob), making the triage workflow able to create separate issues per
  unique error type.

### Removed
- **Dead `CompilationExcludedException` code** — The file-exclusion mechanism was
  removed in #80. The exception class and its two unreachable catch blocks have
  been deleted. The `TryReportPipelineGapsAsync` docstring no longer references
  the removed "iterative Roslyn retry" path.

### Fixed
- **Skip RDLC report layout generation** — `Compilation.Emit()` now uses
  `CompilationGenerationOptions.Code | Navigation` instead of `All`, skipping
  RDLC layout generation that crashes with `NullReferenceException` in
  `ReportRdlcUtilities.GenerateRdlcLayout` when running standalone. Report
  objects still emit C# code for dataset columns and triggers.
- **Telemetry triage KQL groups by message** — The triage workflow's KQL query
  now groups by `type, outerMessage` instead of just `type`, so each unique
  error message gets its own row instead of all `AlRunner.CompilationGap`
  exceptions collapsing into a single row.
- **Telemetry triage root-cause aggregation** — The triage workflow now pre-
  aggregates compilation gaps by root-cause pattern before sending to Copilot.
  CS0103 label-like variables (`*Lbl`, `*Txt`, etc.) collapse into one group;
  CS1061 errors on generated types (Report/Page/Extension) collapse by target;
  CS1061 errors on mock types keep separate entries per missing method. Handles
  scope-qualified type names (`Report70400.SomeScope` → `Report70400`) and
  truncated telemetry messages gracefully. A safety cap aborts issue creation if
  Copilot returns more than 15 new problems (likely a grouping failure).
- **Telemetry message truncation limit** — `ScrubMessage` now truncates at 500
  characters instead of 200, preserving full error context for long generated
  type names like `ReportExtension50506.DtldCustLedgEntries_…`.
- **`ALTransferFields` skips all PK fields** — When `initPrimaryKey=false`,
  `TransferFields` now skips all registered primary key fields instead of only
  field 1. Correctly handles composite primary keys. (#113)

### Added
- **`GetFilter` / `GetFilters` / `HasFilter`** — `GetFilter(fieldNo)` now returns
  the actual filter expression (equality value, `FROM..TO` range, or SetFilter
  expression) instead of empty string. `GetFilters` returns all active filters
  as a combined string. `HasFilter` returns true when any filter is active. (#113)
- **`CurrentKey` / `Ascending`** — `CurrentKey` property returns the current sort
  key field name(s), defaulting to PK. `Ascending` property returns whether the
  sort order is ascending (default true). (#113)
- **Record stub methods** — `CountApprox` (returns Count), `Consistent(bool)`
  (no-op), `FieldActive(fieldNo)` (returns true), `AddLink`/`DeleteLink`/
  `DeleteLinks`/`HasLinks` (in-memory tracking), `WritePermission` (returns true),
  `SetPermissionFilter` (no-op). (#113)
- **New test suite**: `108-getfilter` — 11 tests covering GetFilter, GetFilters,
  and HasFilter with range/expression filters and reset behavior.
- **New test suite**: `109-currentkey` — 4 tests covering CurrentKey and Ascending
  property getters.
- **New test suite**: `110-transferfields` — 3 tests covering TransferFields with
  PK handling.
- **New test suite**: `111-record-stubs` — 8 tests covering CountApprox, Consistent,
  FieldActive, AddLink/HasLinks/DeleteLinks, WritePermission, SetPermissionFilter.

## [1.0.13] - 2026-04-14

### Added
- **Event subscriber parameter forwarding** — Publisher event arguments (`ByRef<T>`
  and value parameters) are now forwarded from `βscope.RunEvent()` to subscriber
  methods via positional matching. Subscribers that modify `var` parameters (e.g.
  `var IsHandled: Boolean`) now write back correctly through shared `ByRef<T>`
  references. (#116)
- **Implicit DB trigger events** — `MockRecordHandle` now fires
  `OnBeforeInsertEvent`/`OnAfterInsertEvent`, `OnBeforeModifyEvent`/`OnAfterModifyEvent`,
  `OnBeforeDeleteEvent`/`OnAfterDeleteEvent` from `ALInsert`/`ALModify`/`ALDelete`, and
  `OnBeforeValidateEvent`/`OnAfterValidateEvent` from `ALValidateSafe`/`ALValidate`.
  Events fire regardless of `runTrigger` (matching BC behavior). xRec snapshots
  are captured before mutations. (#116)
- **BindSubscription / UnbindSubscription** — Manual event subscriber codeunits
  (`EventSubscriberInstance = Manual` in AL) are now detected via
  `[ManualEventSubscriber]` marker attribute emitted by the rewriter. The rewriter
  rewrites `ALSession.ALBindSubscription()`/`ALUnbindSubscription()` to
  `MockCodeunitHandle.Bind()`/`Unbind()`. Manual subscribers only fire when bound.
  Bindings are reset between tests. (#116)
- **`EventSubscriberRegistry` refactored** — Uses 3-tuple key
  `(ObjectType, ObjectId, EventName)` to prevent table/codeunit ID collision.
  Supports both automatic and manual subscriber classification.
- **New test suites**: `97-event-params` (2), `98-db-trigger-events` (5),
  `99-validate-events` (3), `100-bind-subscription` (3), `101-multi-subscribers` (2),
  `102-sender-pattern` (6), `103-before-db-events` (7), `104-xrec-behavior` (3),
  `105-subscriber-error` (4) — 35 new test cases total.
- **IncludeSender support** — `IntegrationEvent(true, false)` and
  `BusinessEvent(true)` now correctly pass the publishing codeunit instance as
  the first subscriber parameter via `MockCodeunitHandle.FromInstance(this)`.
  Subscribers can read/write publisher state through the sender handle. (#116)

### Fixed
- **`ALRename` properly updates table rows** — `MockRecordHandle.ALRename()` was
  a broken stub that only modified the handle's field bag without touching the
  in-memory table store. Now it: (1) finds the current record by its PK,
  (2) checks for key conflicts, (3) updates the actual table row, and
  (4) honors `errorLevel` (throws or returns false). Tested by `tests/bucket-2/107-rename/`
  (9 test cases). (#130)
- **`ALInsert` honors `DataError` level** — `MockRecordHandle.ALInsert()` now
  checks the `errorLevel` parameter before throwing on duplicate primary key.
  When AL code captures the return value (`if not Rec.Insert() then …`), the
  BC compiler passes `DataError.Never` and the method returns `false` instead
  of throwing. Previously it always threw regardless of error level. (#128)
- **`ALDelete` throws on missing record** — `MockRecordHandle.ALDelete()` now
  throws when the record does not exist and `errorLevel` is `DataError.ThrowError`
  (i.e. the return value is not captured in AL). Previously it silently returned
  `false` regardless of error level. Tested by `tests/bucket-2/106-dataerror-suppress/`
  (21 test cases). (#128)
- **`CS1503` in codeunits that call `HttpContent.WriteFrom(InStream)` or `HttpContent.ReadAs(var InStream)`** —
  After the `NavInStream → MockInStream` type rename in the rewriter, calls to
  `NavHttpContent.ALLoadFrom(MockInStream)` and `NavHttpContent.ALReadAs(ITreeObject, DataError, ByRef<MockInStream>)`
  failed with `CS1503` because `MockInStream` is not a subtype of `NavInStream`.
  The rewriter now intercepts:
  - `content.ALLoadFrom(arg)` (1-arg form) → `AlCompat.HttpContentLoadFrom(content, arg)`
  - `content.ALReadAs(scope, dataError, stream)` (3-arg form) → `AlCompat.HttpContentReadAs(content, scope, dataError, stream)`

  `AlCompat.HttpContentLoadFrom` has overloads for both `NavText` (delegating to the real
  method) and `MockInStream` (reads text from the stream then delegates).
  `AlCompat.HttpContentReadAs` is a no-op that initialises the stream variable to an empty
  `MockInStream` (HTTP is not available in standalone mode).
  The 2-arg text form of `ALReadAs(DataError, ByRef<NavText>)` is not affected.
  Codeunits such as "GO Express Request Builder" (codeunit 50611) that call
  `HttpContent.WriteFrom(InStream)` now compile and their pure-logic methods are testable.
  Fixes [#105](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/105).
  Tested by `tests/bucket-2/96-httpcontent-stream/` (5 test cases).
- **Missing BC runtime DLL classified as runner limitation** — `FileNotFoundException`
  and `FileLoadException` for `Microsoft.Dynamics.Nav.*` or `Microsoft.BusinessCentral.*`
  assemblies are now reported as ERROR (runner limitation, exit 2) instead of FAIL
  (test assertion failure, exit 1). This correctly classifies missing BC runtime DLLs
  (e.g. `Microsoft.BusinessCentral.Telemetry.Abstractions` introduced in BC 28) as
  a runner gap rather than a code bug.
- **Page without SourceTable compiles cleanly** — `SetSelectionFilter` no longer
  injects `this.Rec` into page classes that have no source table, fixing a CS1061
  Roslyn error for pages that only define helper procedures.

### Changed
- **Test matrix extended to BC 28.0** — `28.0` added to the version prefix list in
  `test-matrix.yml`. BC 28 introduced `Microsoft.BusinessCentral.Telemetry.Abstractions`
  as a new runtime dependency not yet fetched by the artifact downloader; one test
  (`SingleArgValidateFiresTrigger`) shows as a runner limitation (ERROR) on BC 28 only.
- **Vision reframe** — project rationale updated from "pure-logic codeunits only"
  to broad AL language compatibility. Docs, guide, and limitations page updated to
  reflect that unsupported AL constructs are gaps to fix, not design boundaries.
- **CONTRIBUTING.md** — added contributor guide covering TDD requirements, CI matrix,
  CHANGELOG policy, documentation checklist, and code-quality rules.
- **Test folder restructured into buckets** — `tests/` now has `bucket-1/`, `bucket-2/`,
  `stubs/`, and `excluded/` subdirectories. Each bucket is one `al-runner` invocation
  (all suites compile and run together), eliminating per-suite startup overhead. CI
  updated to loop over `bucket-*` directories. `39-stubs` moved to `stubs/`,
  `06-intentional-failure` and `46-missing-dep-hint` moved to `excluded/`. The
  `06-intentional-failure` fixture is now actively verified in CI (exit code must be 1).

### Added
- **Report variable support** — `NavReportHandle` is rewritten to `MockReportHandle`,
  a standalone replacement that supports `SetTableView()`, `Run()` (no-op), and
  `RunRequestPage()` (dispatches to `[RequestPageHandler]`). Report and report-extension
  generated classes are stubbed so BC-only layout/runtime infrastructure does not block
  compilation. `rendering { ... }` blocks and `DefaultRenderingLayout` properties are
  stripped from report AL source before transpilation.
  Tested by `tests/91-report-handle/` (6 test cases) and `tests/95-rendering-strip/` (2 test cases).
- **`[RequestPageHandler]` dispatch** — `HandlerRegistry` now registers and invokes
  `[RequestPageHandler]` procedures for `Report.RunRequestPage()` calls, with fallback
  to `[ModalPageHandler]` when no dedicated request-page handler is registered.
  Tested by `tests/92-request-page-handler/` (2 test cases).
- **Extended TestPage support** — `MockTestPageHandle` gains `GoToRecord()`, `Next()`,
  `New()`, `ClearReference()`, and `GetPart()` for subpage navigation.
  `MockTestPageField` gains assignable `ALValue`, `ALAsDecimal()`, and `ALEnabled()`.
  `MockTestPageFilter` now tracks filters with `ALGetFilter()`.
  Tested by `tests/90-testpage-extended/` (10 test cases).
- **`GetBySystemId`** — `MockRecordHandle` and `MockRecordRef` now support
  `ALGetBySystemId(Guid)` for looking up records by their system ID.
  Tested by `tests/93-record-getbysystemid/` (2 test cases).
- **`ClearFieldValue`** — `MockRecordHandle.ClearFieldValue(fieldNo)` resets a single
  field to its default. The rewriter redirects `ALSystemVariable.Clear(x)` to
  `x.Clear()` for RecordRef and similar types.
  Tested by `tests/94-clear-field-value/` (6 test cases).
- **`ALGetView` / `ALSetView`** — `MockRecordHandle` stores and returns view text.
  `MockRecordRef.ALSetView` now delegates to the underlying handle.
- **Global array variables** — `MockRecordHandle.GetGlobalArrayVariable()` returns
  typed `MockArray<T>` instances for Code, Text, Integer, Decimal, and Boolean.
- **`AlCompat.ObjectToMockArray<T>()`** — replacement for `ALCompiler.ObjectToNavArray`,
  converting runtime objects into the rewritten `MockArray` shape.
- **`MockFile.ALUploadIntoStream()`** — standalone replacement for `NavFile` upload
  dialogs. Returns `false` (no client surface) and clears the target stream.
- **`MockTextBuilder.ALAppendLine()`** — parameterless `AppendLine` overloads for
  appending a bare newline.
- **`MockInStream.Clear()`** — resets the in-memory stream to empty.
- **`MockSystemOperatingSystem.ALGetUrl()`** — returns a mock URL string.
- **`ClearApplicationMemberVariables()` stub** — injected into all codeunit classes
  so `TestRunner` codeunits compile after base-class removal.
- **`SetSelectionFilter` on page classes** — delegates to `ALCopy` + `ALSetRecFilter`.
- **Improved stub isolation** — built-in test stubs (Assert, Variable Storage) are
  compiled in isolation when real BC test packages are present, preventing symbol
  collisions. Stubs are skipped entirely when the source contains no test-library usage.
- **TestRunner codeunit support** — `NavTestRunnerCodeUnit` is now handled alongside
  other codeunit base types. BC-specific override members
  (`OnTestRunMethodsHaveTestPermissionsParameter`, `CommitTestCodeunits`,
  `CommitTestFunctions`) are stripped during rewriting.
- **Query object support** (`AlRunner/Runtime/MockQueryHandle.cs`, `AlRunner/RoslynRewriter.cs`) —
  AL `Query` objects now compile and run in standalone mode.  The BC compiler
  generates `QueryNNNN : NavQuery` classes that reference `NCLMetaQuery` and
  service-tier SQL views; the rewriter replaces the entire class with a minimal
  stub extending `MockQueryHandle` (same pattern used for XmlPort objects).
  `NavQueryHandle` is rewritten to `MockQueryHandle`.
  **Supported operations (no-ops allowing pre-Open setup code to run):**
  - `Q.Close()` — no-op
  - `Q.SetFilter(column, expression)` — no-op
  - `Q.SetRange(column [, from [, to]])` — no-op
  - `Q.TopNumberOfRows(n)` / `Q.ColumnCaption` / `Q.ColumnName` — property stubs
  **Operations that throw `NotSupportedException`** (query data access requires
  the BC service tier):
  - `Q.Open()`, `Q.Read()`
  - `Q.SaveAsCsv()`, `Q.SaveAsXml()`, `Q.SaveAsJson()`, `Q.SaveAsExcel()`
  Inject query dependencies via an AL interface to make query-dependent code
  unit-testable.  Tested by `tests/90-query-object/` (12 test cases).
  Fixes [#86](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/86).

### Performance
- **Cold-start optimizations** — `TieredPGO=false` and `QuickJitForLoops=true`
  reduce JIT overhead for short-lived CLI runs (~77 ms saved). `PublishReadyToRun`
  pre-compiles AlRunner to native code at publish time (~420 ms saved with
  `dotnet publish -r <rid>`).
- **Parallel AL parsing** — `ParseObjectText` calls run via `Parallel.For` instead
  of a sequential loop (3.3x speedup on multi-file projects).
- **Pre-sized MemoryStream** — Roslyn emit uses a 512 KB pre-allocated stream,
  avoiding 10+ resize-and-copy cycles per compilation.
- **Per-file rewrite cache (server mode)** — Rewritten Roslyn SyntaxTrees are
  cached by transpiled C# content. On warm re-run with one file changed, only
  that file is re-rewritten (41 ms → 1.7 ms, 24x speedup).
- **SyntaxTree cache (server mode)** — Parsed AL SyntaxTrees are cached by file
  content hash. Unchanged files skip `ParseObjectText` entirely.
- **Collectible AssemblyLoadContext** — Compiled test assemblies load into
  collectible ALCs. Memory is bounded by the 8-slot LRU cache instead of growing
  indefinitely.

### Fixed
- **`ObjectToDecimal` crash on `NavDecimal`** — `AlCompat.ObjectToDecimal()` now
  routes through `ExtractDecimal()` to handle BC's `NavDecimal` type, which does not
  implement `IConvertible`. Previously threw `InvalidCastException` when `TestPage`
  field `AsDecimal()` was called.
- **`CS1503` in codeunits that declare HTTP variables** — `AlScope` now implements
  `ITreeObject` (with stub `Tree`, `Type`, and `SingleThreaded` members), satisfying
  the parent-scope requirement of Nav* type constructors (`NavHttpClient`,
  `NavHttpRequestMessage`, `NavHttpResponseMessage`, `NavHttpContent`). Previously
  any codeunit that declared an `HttpClient`, `HttpRequestMessage`, `HttpResponseMessage`,
  or `HttpContent` local variable was excluded as a `CompilationGap` with
  `CS1503: cannot convert from AlScope to ITreeObject`. The null! catch-all rewriter
  rule that was masking the root cause has been removed.
  Codeunits with HTTP variables now compile; pure-logic methods in those codeunits
  (ones that don't actually send HTTP requests) are fully testable.
  Tested by `tests/89-nav-type-constructors/` (3 test cases).
- **`RecRef.Find()` (no-arg) compilation error** — The BC compiler emits
  `recRef.ALFind(DataError.ThrowError)` for AL's no-argument `RecRef.Find()`.
  `MockRecordRef` now provides a matching `ALFind(DataError)` overload that routes
  through `TryFind` so an empty table returns `false` instead of throwing.
  Previously caused `CS1503: cannot convert from DataError to string` at Roslyn
  compilation. Tested by `tests/88-recref-find/` (6 test cases).

## [1.0.12] — 2026-04-13

### Added
- **Multi-target net8.0 and net9.0** — `al-runner` now ships as a single NuGet
  package containing binaries for both .NET 8 and .NET 9. `dotnet tool install`
  automatically selects the build matching the installed runtime, so users with
  only .NET 9 no longer need to install .NET 8 separately.
  Closes [#75](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/75).
- **JUnit XML output (`--output-junit <path>`)** — Writes a standard JUnit XML test
  report alongside normal console output. GitHub Actions, Azure DevOps, and GitLab CI
  natively render JUnit XML as test annotations, summaries, and trend graphs. Combined
  with `--coverage` (Cobertura XML), this completes the CI integration story:
  - `--coverage` → coverage tab (Cobertura)
  - `--output-junit` → test results tab (JUnit XML)

  Tests are grouped by AL codeunit name as `<testsuite>` elements. Real assertion
  failures use `<failure>`; runner limitations use `<error>`.
  Closes [#72](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/72).
- **Compact summary line at end of test runs** — After each test run, the output
  now ends with a concise one-liner analogous to pytest/jest:
  - All pass: `42 passed in 1.8s`
  - With failures: `9 passed, 2 failed, 3 blocked (runner limitation) in 1.8s`
  - With setup errors: `9 passed, 1 errors in 0.3s`
  Only non-zero counts are shown. Runner-limitation errors (`IsRunnerBug=true`) are
  labelled `blocked (runner limitation)`; other errors are labelled `errors`.
  Elapsed time is always included when timing is available.
  Closes [#71](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/71).
- **`sourceFile` field on iterations and captured values** — The `--output-json`
  iteration and captured-value records now include a `sourceFile` property with the
  path to the AL file that contains the loop or variable. A new `SourceFileMapper`
  class resolves AL object names to source files at input-loading time.
  Tested by `tests/67-iteration-tracking/`.
- **Stream assignment, binary I/O, and `COPYSTREAM`** — Three new stream capabilities:
  - `MockOutStream.ALAssign` — enables `OutStr2 := OutStr1` stream assignment in AL
  - `MockStream.ALWrite`/`ALRead` overloads for `Integer`, `Boolean`, `Decimal18` — binary read/write via `OStr.Write(value)` / `IStr.Read(value)` in AL
  - `MockStream.ALCopyStream` — implements `COPYSTREAM(OutStr, InStr)`; rewriter redirects `ALSystemVariable.ALCopyStream` to `MockStream.ALCopyStream`

  Tested by `tests/79-stream-surface/` (10 cases).
  Fixes [#65](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/65).
- **Picture-string tokens in `Format()`** — `Format(value, 0, formatString)` now
  handles AL decimal and time picture strings:
  - `<Precision,min:max>` — rounds a decimal to at most `max` decimal places and
    shows at least `min` (e.g. `Format(1.567, 0, '<Precision,1:2>')` → `'1.57'`).
  - `<Standard Format,N>` — N=0 uses default AL decimal formatting; N=1 rounds to
    the nearest integer (e.g. `Format(1.567, 0, '<Standard Format,1>')` → `'2'`).
  - Time picture strings (`<Hours24,N>:<Minutes,N>`) applied to `Time` variables
    (e.g. `Format(093000T, 0, '<Hours24,2>:<Minutes,2>')` → `'09:30'`).
  Tested by `tests/85-picture-format/` (9 test cases).
- **`--generate-stubs` workflow documented in `--guide`** — The agent guide now
  includes a section explaining when and how to use `--generate-stubs` to scaffold
  AL stubs for missing dependencies, including the filtered form that limits output
  to objects actually referenced by the source under test.
- **Differentiated exit codes for CI integration** — al-runner now returns distinct
  exit codes so CI scripts can distinguish real failures from runner gaps:
  - `0` — all tests passed
  - `1` — test assertion failures (real bugs in code) or usage/argument error
  - `2` — runner limitations only (no assertion failures; all blocked tests are due
    to Roslyn compilation gaps or missing mock support)
  - `3` — AL compilation error (the AL source itself does not compile)

  Previously, all non-success outcomes returned `1`. This change enables incremental
  CI adoption: tolerate exit code `2` while treating `1` and `3` as hard failures.
  Fixes [#46](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/46).

### Changed
- **Deduplicate repeated error blocks in output** — When multiple tests share the
  same runner-limitation error (e.g. 66 tests all blocked by the same
  `CompilationExcludedException`), `PrintResults` now prints the message once as a
  `WARN` block with a count, then compact `ERROR TestName (blocked)` lines. With
  `-v`/`--verbose`, the old full per-test detail is preserved. Single/unique errors
  and all `FAIL` tests are never deduplicated.
  Closes [#70](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/70).
- **Invariant-culture decimal formatting** — `AlCompat.Format()` for decimal values
  now always uses `.` as the decimal separator regardless of OS locale, matching
  real BC behavior.
- **Source files with compilation errors are no longer silently excluded** — Previously,
  when Roslyn compilation failed, al-runner would silently retry by dropping the
  offending files and compiling the remaining ones. This could produce a passing run
  that was missing whole codeunits. Now, any compilation error causes an immediate hard
  failure: all errors are printed to stderr and the runner exits. This ensures you always
  compile the full app or get a clear error — no silent partial results.
  Fixes [#66](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/66).
- **Server cache preserves compilation state across requests** — The `--server` JSON-RPC
  daemon now stores compilation errors alongside the compiled assembly in the cache.
  Cache hits return the same error state as the original compilation, preventing stale
  results on repeated identical requests.

### Fixed
- **`FieldRef.SetRange(Variant)` never matched records** — When AL code assigned a
  text literal to a `Variant` and then called `FieldRef.SetRange(v)`, the filter never
  matched because `MockVariant`'s implicit `NavValue?` operator returned `null` for
  non-NavValue content (raw CLR strings). The operator now converts primitive CLR values
  to their NavValue equivalents (`string→NavText`, `int→NavInteger`, `bool→NavBoolean`,
  `long→NavBigInteger`). Additionally, `NavValueToString` now trims trailing spaces from
  `NavCode` values (which BC pads to `maxLength`), fixing equality comparisons between
  `Code[N]` fields and `NavText` filter values.
  Tested by `tests/82-recref-fieldindex/` and `tests/87-fieldref-setrange-types/` (8 cases).
- **`GlobalLanguage()` NullReferenceException in standalone mode** — `ALSystemLanguage.get_ALGlobalLanguage` and `set_ALGlobalLanguage` crashed because there is no live BC session context in the runner. The rewriter now intercepts `ALSystemLanguage.ALGlobalLanguage` (both get and set) and routes them to `MockLanguage.ALGlobalLanguage`, a static int property backed by an in-memory field defaulting to 1033 (ENU). `MockLanguage.Reset()` is called between tests to restore the default. Fixes [#82](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/82). Tested by `tests/86-global-language/` (5 cases).
- **`FieldRef.SetRange` CS0121 ambiguity resolved** — `MockFieldRef.ALSetRange` had
  both `ALSetRange(NavValue)` and `ALSetRange(MockVariant)` overloads. Because
  `MockVariant` defines implicit conversions to and from `NavValue`, C# overload
  resolution could not pick one when the argument was a `NavValue` subtype (e.g.
  `NavInteger`, `NavOption`, `NavDecimal`), producing `CS0121`. The
  `ALSetRange(MockVariant)` overload has been removed; its logic is merged into the
  existing `ALSetRange(object)` catch-all, which already handles `MockVariant`
  correctly. Tested by `tests/87-fieldref-setrange-types/` (8 test cases).
  Fixes [#84](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/84).
- **`Insert()` now enforces primary-key uniqueness for more tables** — Previously,
  PK uniqueness was only checked when the table's key declaration had been parsed from
  AL source by `TableFieldRegistry`. Tables without an explicit `keys {}` block, or
  tables loaded from external `.app` symbol packages, skipped the check entirely,
  allowing silent duplicate inserts that would have errored in real BC. Now `ALInsert`
  falls back to field 1 as the implicit PK when no key is registered, restoring
  duplicate detection for tables without a declared key. Note: for symbol-only tables
  whose actual PK is composite or does not include field 1, behavior may still differ
  from real BC.
  Tested by `tests/86-pk-insert-fallback/`. Fixes [#78](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/78).
- **`Dialog` variable type now compiles and runs** — AL codeunits that declare a
  `Dialog` variable and call `Open`, `Update`, and `Close` on it previously failed
  with `CS1503: cannot convert from 'string' to 'NavText'` when the BC compiler
  emitted string literals for the dialog format string. `MockDialog.ALOpen` and
  `ALUpdate` now accept both `string` and `NavText`/`NavValue` overloads, matching
  all patterns emitted by the BC compiler. Tested by `tests/85-dialog/` (4 test cases).
  Fixes [#63](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/63).
- **XmlPort object classes no longer break compilation** — XmlPort schema classes
  generated by the BC compiler (`XmlPortNNNN : NavXmlPort`) contain complex
  constructor and schema-initialization code that cannot compile in standalone mode.
  The rewriter now replaces these entire class bodies with minimal stubs extending
  `MockXmlPortHandle`, so any test suite that includes an XmlPort definition compiles
  and runs correctly.

## [1.0.11] — 2026-04-13

### Added
- **XmlPort stub (`MockXmlPortHandle`)** — Codeunits that declare `XmlPort "X"` variables
  now compile and run in al-runner. `NavXmlPortHandle` is rewritten to `MockXmlPortHandle`,
  which exposes `Source`/`Destination` stream properties and satisfies `Import`/`Export`
  instance method calls from the BC compiler output. The static `XmlPort.Import(portId, stream)`
  / `XmlPort.Export(portId, stream)` forms (emitted as `NavXmlPort.Import/Export`) are
  redirected to `MockXmlPortHandle.StaticImport/StaticExport`. All import/export methods
  throw `NotSupportedException` at runtime with a clear message directing the developer to
  inject the XmlPort dependency via an AL interface. `Invoke()` returns null.
  Tested by `tests/84-xmlport/` (6 test cases).

## [1.0.10] — 2026-04-13

### Fixed
- **Variant-to-Record cast now works** — AL code that assigns a `Variant` to a
  `Record` variable (`MyRec := MyVariant;`) previously caused a Roslyn compile
  error `CS0030: Cannot convert type 'MockVariant' to 'MockRecordHandle'`. Fixed
  by adding an explicit cast operator `MockVariant → MockRecordHandle` that
  unwraps the inner value, matching BC runtime behavior. Tested by
  `tests/84-variant-to-record/` (5 test cases).
- **`Variant.IsRecord()` and other `Variant.IsXxx()` type-checks now unwrap
  the `MockVariant` wrapper and handle Nav runtime wrapper types** —
  `AlCompat.ALIsRecord()` (and all sibling `ALIs*` helpers) previously received
  the `MockVariant` object directly from the rewriter and checked its type name,
  which always failed for record-typed variants. All `ALIs*` methods now unwrap
  `MockVariant` before type-checking. They also handle Nav runtime wrapper types
  (`NavBoolean`, `NavInteger`, `NavBigInteger`, `NavDecimal`, `NavDate`,
  `NavDateTime`, `NavGuid`) that appear when values come from record fields
  rather than AL literals. Tested by `tests/84-variant-to-record/` (8 test cases).
- **`--output-json` now distinguishes compilation errors from test failures** —
  Tests that could not run due to a runner limitation now receive `"status": "error"`
  instead of `"status": "fail"` in the JSON output. The top-level `"errors"` field
  correctly counts these, while `"failed"` is reserved for genuine assertion failures.
  Resolves [#67](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/67).

## [1.0.9] — 2026-04-13

### Added
- **Opt-in crash telemetry** — When an unexpected .NET exception escapes the runner
  pipeline, al-runner now prompts the user to send an anonymous error report to
  Application Insights (Azure). The prompt only appears in interactive terminal
  sessions (never in CI, server mode, or when output is redirected). A 30-second
  timeout auto-answers "no" so no pipeline can ever hang. Only `AlRunner.*` stack
  frames are included — user AL source, file paths, and codeunit names are never
  transmitted. Use `--no-telemetry` to disable the prompt entirely.

### Fixed
- **Duplicate `.app` packages no longer cause AL0275 "ambiguous reference" errors**
  — When the packages directory contains multiple copies of the same extension
  (same publisher/name/version) with different GUIDs, al-runner now deduplicates
  them proactively at scan time via `PackageScanner`, keeping exactly one entry per
  identity (deterministic: lowest GUID wins). A reactive fallback also handles
  any residual self-duplicate AL0275 errors from explicitly specified dependency
  specs. Genuine cross-extension AL0275 conflicts (different publishers or names)
  are unaffected. Stubs-vs-package conflict resolution is unchanged.
  `DiagnosticClassifier.IsSelfDuplicateAmbiguity` correctly distinguishes the two
  cases by comparing both sides of the AL0275 message. This fixes the error
  pattern `'X' is an ambiguous reference between 'X' defined by the extension
  'App by Publisher (V)' and 'App by Publisher (V)'` when both sides are identical.

### Added
- **`DiagnosticClassifier`** — new public static class that parses AL compiler
  diagnostic messages. `IsSelfDuplicateAmbiguity(message)` returns `true` when
  both extension identity strings in an AL0275 message are identical (the
  self-duplicate case). `ExtractAmbiguityExtensionIds(message)` returns both
  extension identity strings or null if the message doesn't match.
- **`PackageScanner`** — new public static class that scans package directories
  for `.app` files and returns a deduplicated `IReadOnlyList<PackageSpec>`. Two-
  pass deduplication: (1) by GUID keeping highest version, (2) by
  publisher+name+version keeping lowest GUID. Replaces the inline scan loop that
  was previously embedded in `AlTranspiler`.

### Overloaded procedures with `var List of [T]` parameters
  `Invoke` now resolves the correct overload when the BC compiler emits suffixed
  C# method names (e.g., `ProcessJson_2101255952`) for overloaded AL procedures.
  Previously, `MockCodeunitHandle.Invoke` used only the base method name, picking
  the wrong overload and causing `Object of type 'ByRef<NavList<T>>' cannot be
  converted to type 'T'` reflection errors.
  Tested by `tests/83-list-byref/` (9 test cases).

### Added
- **`RecordRef.FieldIndex(n)`** returns a `MockFieldRef` for the nth registered
  field (sorted by field number). Out-of-range index returns a stub with field
  number 0.
- **`RecordRef.Caption`** returns the table caption (delegates to
  `MockRecordHandle.ALTableCaption`).
- **`TestPage field Visible`** — `ALVisible()` method on `MockTestPageField`
  returning `true` (stub).
- **`TestPage field Editable`** — `ALEditable()` method on `MockTestPageField`
  returning `true` (stub).
- **`TestPage field Lookup()`** — `ALLookup()` no-op method on `MockTestPageField`.
- **`TestPage field DrillDown()`** — `ALDrilldown()` no-op method on `MockTestPageField`.
- **`FieldRef.SetRange(MockVariant)`** — explicit overload preventing C# implicit
  conversion to `NavValue?` (which returned null for non-NavValue variant contents).
- **`FieldRef.SetRange(object)`** — overload for `NavComplexValue → object` rewritten
  parameters.
- **`FieldRef.ValidateSafe()`** — no-arg overload (re-validates current value).
- **`FieldRef.CalcField(DataError)`** — overload accepting DataError parameter (no-op).
- **`FieldRef.Clear()`** — resets field value to default.

  Tested by `tests/82-recref-fieldindex/` (10 test cases).

### Fixed
- **`IsInWriteTransaction()` no longer crashes with NullReferenceException.**
  `ALDatabase.ALIsInWriteTransaction()` calls into `NavSession` which is null in
  standalone mode. The rewriter now replaces the call with `false` (no DB
  transactions in the runner).
- **`GuiAllowed` now compiles in standalone mode.** Added `ALGuiAllowed` property
  to `MockSystemOperatingSystem` returning `false` (no UI in standalone mode).
  Previously caused `CS0117` compilation error.
  ([#54](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/54))
- **`FieldRef.Class = FieldClass::Normal` comparison now compiles.** Changed
  `MockFieldRef.ALClass` return type from `int` to `FieldClass` enum, fixing
  `CS0019` operator mismatch error.
  ([#54](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/54))
- **`NavComplexValue` type parameter mismatch resolved.** Added rewriter rule
  replacing `NavComplexValue` with `object` so `MockVariant` and `MockRecordRef`
  can be passed where BC expects `NavComplexValue`.
  ([#54](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/54))

  Tested by `tests/79-gui-fieldclass/` (6 test cases).

### Fixed
- **`exit(this)` in fluent-chaining codeunits now works.** The BC compiler emits
  `__ThisHandle` for codeunit methods that return `Codeunit "Self"` (fluent builder
  pattern). After the rewriter stripped the `NavCodeunit` base class, `__ThisHandle`
  was undefined, causing `CS1061` compilation errors. The rewriter now replaces
  `__ThisHandle` access with `MockCodeunitHandle.FromInstance()`, which wraps the
  live codeunit instance. Tested by `tests/79-exit-this/` (3 test cases).
  ([#45](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/45))

### Added
- **`MockFormHandle` page variable stubs.** `SetTableView(rec)`, `LookupMode`
  (bool property, default false), `Editable` (bool property, default true),
  `PageCaption` (string property, default empty), `Clear()`, and
  `GetRecord(rec)` (1-arg overload) are now available on Page variables.
  Previously caused CS1061 compilation errors when production code used these
  common page-level members. Tested by `tests/79-form-handle-stubs/` (8 test cases).
  ([#51](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/51))
- **`TestPage` custom action invoke (`GetAction`).** `MockTestPageHandle.GetAction(actionHash)`
  returns a no-op `MockTestPageAction` so `TestPage.MyAction.Invoke()` compiles and
  runs without crashing. Previously caused CS1061 because only `GetBuiltInAction`
  (for OK/Cancel) existed. Tested by `tests/79-form-handle-stubs/`.
  ([#52](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/52))
### Added
- **Session API support (`StartSession`, `StopSession`, `IsSessionActive`, `Sleep`).**
  `StartSession` dispatches the target codeunit synchronously via
  `MockCodeunitHandle` (same pattern as `Codeunit.Run`) and returns `true`.
  `StopSession` and `Sleep` are no-ops. `IsSessionActive` returns `false`
  (session already completed synchronously). All four session functions are
  intercepted by the rewriter and redirected to `MockSession`. Previously,
  `StartSession` with a record parameter caused a compilation failure because
  the rewriter stripped `.Target` from the record argument, leaving a
  `MockRecordHandle` where the BC runtime expected `NavRecord`.
  Tested by `tests/79-startsession/` (6 test cases).
  (fixes [#50](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/50))
### Added
- **`RecordRef.Duplicate()`** — `MockRecordRef.ALDuplicate()` returns a copy of
  the RecordRef pointing to the same table with copied field data and filters.
  ([#53](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/53))
- **`RecordRef.ReadIsolation` (no-op)** — `MockRecordRef.ALReadIsolation` setter
  accepts isolation level assignments without crashing. Getter returns default.
  ([#53](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/53))
- **`InStream` assignment (`InStr2 := InStr1`)** — `MockInStream.ALAssign()`
  copies the source stream's buffer and position into the target.
  ([#53](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/53))
- **`Record.ReadIsolation` already supported** — `MockRecordHandle.ALReadIsolation`
  was already implemented; confirmed working with test coverage.
  ([#49](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/49))

  Tested by `tests/80-recref-isolation/` (5 test cases).

## [1.0.8] — 2026-04-12

### Added
- **JSON types (`JsonObject`, `JsonArray`, `JsonToken`, `JsonValue`) now work.**
  The real BC JSON types (`NavJsonObject`, `NavJsonArray`, `NavJsonToken`,
  `NavJsonValue`) from `Microsoft.Dynamics.Nav.Ncl.dll` are used directly for
  most operations (Add, Get, Contains, Remove, Replace, AsValue, AsText,
  AsInteger, AsBoolean, Count, etc.). Only `WriteTo`, `ReadFrom`, `SelectToken`,
  and `SelectTokens` are intercepted by the rewriter and redirected to
  `MockJsonHelper`, which performs the same Newtonsoft.Json operations without
  going through BC's `TrappableOperationExecutor` / `NavEnvironment` (which
  crash in standalone mode). Tested by `tests/77-json-types/` (15 test cases).
  ([#47](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/47))
- **BLOB / InStream / OutStream support.** `MockBlob` replaces `NavBLOB` as an
  in-memory byte buffer. `MockInStream` and `MockOutStream` replace `NavInStream`
  and `NavOutStream` respectively. `MockStream` replaces the static `ALStream`
  helper class. Supports the common test pattern: write text to a BLOB field via
  `CreateOutStream` + `WriteText`, read it back via `CreateInStream` + `ReadText`,
  and check `HasValue`. BLOB fields on records auto-persist `MockBlob` instances
  so writes survive across reads. Tested by `tests/78-blob-stream/` (6 test cases).
  (fixes [#46](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/46))
- **`TestPage.Caption`, `.First()`, `.GoToKey()`, `.Filter.SetFilter()` stubs.**
  `MockTestPageHandle` now supports `ALCaption` (returns `"TestPage"`), `ALFirst()`
  (returns `true`), `ALGoToKey(DataError, params NavValue[])` (returns `true`), and
  `ALFilter` property returning `MockTestPageFilter` with `ALSetFilter(int, string)`
  no-op. These previously caused CS1061 compilation errors when test codeunits used
  TestPage navigation/filter members. Tested by `tests/74-testpage-navigation/`
  (6 test cases). ([#37](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/37))
- **`TestPage` field `Caption` property.** `MockTestPageField.ALCaption` returns
  a stub empty string, matching the BC compiler's `tP.GetField(hash).ALCaption`
  call pattern. Previously caused CS1061 compilation error.
  ([#38](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/38))
- **`RecordRef.SetLoadFields` no-op.** `MockRecordRef.ALSetLoadFields(DataError,
  params int[])` accepts the BC compiler's lowered call and does nothing — all
  fields are always in memory in standalone mode. Previously caused CS1061.
  ([#39](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/39))
- **`RecordRef.Name` stub.** `MockRecordRef.ALName` returns `"TableN"` (where N
  is the table ID) or empty string when no table is open. Previously caused
  CS1061 compilation error.
  ([#40](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/40))

  Tested by `tests/74-mock-stubs/` (8 test cases covering all 3 additions plus
  the existing `Page.Update()` no-op (#41)).
- **Built-in Library - Variable Storage stub** (codeunit 131004). An AL stub
  (`stubs/LibraryVariableStorage.al`) is auto-loaded alongside the Assert stub,
  and `MockVariableStorage` provides an in-memory FIFO queue at runtime.
  Supports `Enqueue`, `DequeueText`, `DequeueInteger`, `DequeueDecimal`,
  `DequeueBoolean`, `DequeueDate`, `DequeueVariant`, `AssertEmpty`, `Clear`,
  and `IsEmpty`. Tested by `tests/75-library-variable-storage/` (9 test cases).
  (fixes #43)

### Fixed
- **NavScope conversion gap in cross-codeunit dispatch.** When an AL method
  returns a Record or Interface, the BC compiler adds a hidden `NavScope`
  parameter for ownership tracking. Same-codeunit calls pass the calling
  scope object, but after rewriting scopes extend `AlScope` (not `NavScope`),
  causing Roslyn CS1503 error. The rewriter now replaces `NavScope` with
  `object` so any scope or null can be passed. Tested by
  `tests/76-navscope-dispatch/` (3 test cases).
  (fixes [#44](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/44))
- **`Codeunit.Run()` bool return value.** `MockCodeunitHandle.RunCodeunit` now
  accepts a `DataError` parameter and returns `bool`. When the BC compiler emits
  `NavCodeunit.RunCodeunit(DataError.TrapError, id, rec)` for
  `if Codeunit.Run(id) then`, the `&` operator no longer fails with CS0019
  (`Operator '&' cannot be applied to operands of type 'bool' and 'void'`).
  The rewriter now passes `DataError` through; `TrapError` catches exceptions
  and returns `false`, `ThrowError` propagates. Also keeps the outer `OnRun`
  wrapper method so `RunCodeunit` can dispatch to it via reflection.
  Tested by `tests/75-codeunit-run-bool/`.
  ([#42](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/42))
- **RecordRef assignment (`:=` operator).** `MockRecordRef` was missing the
  `ALAssign` method that the BC compiler emits for `RecRef2 := RecRef1`. This
  caused a CS1061 compilation error excluding any codeunit that assigns one
  RecordRef to another. Tested by `tests/72-recref-assign/`. (fixes #35, #36)

### Added
- **ModalPageHandler dispatch.** `[ModalPageHandler]` procedures now intercept
  `Page.RunModal()` calls. When production code calls `RunModal()` on a page
  variable, `MockFormHandle.RunModal()` looks up the registered handler via
  `HandlerRegistry`, creates a `MockTestPageHandle`, invokes the handler, and
  returns the `FormResult` set by the handler's OK/Cancel action invocation
  (OK maps to `LookupOK`, Cancel maps to `LookupCancel`). Missing handler
  throws a descriptive error. Tested by `tests/73-modal-handler/` (3 test cases).
- **TestPage support.** `NavTestPageHandle` is rewritten to `MockTestPageHandle`.
  Test codeunits can now use `TestPage "X"` variables with `OpenEdit()`,
  `OpenView()`, `OpenNew()`, `Close()`, and `Trap()` lifecycle methods.
  `GetField(hash)` returns `MockTestPageField` supporting `ALSetValue`/`ALValue`
  for field get/set. `GetBuiltInAction(FormResult)` returns `MockTestPageAction`
  with `ALInvoke()` for OK/Cancel actions. Tested by `tests/71-testpage/`.
- **ConfirmHandler / MessageHandler dispatch.** Test codeunits with
  `[HandlerFunctions('MyHandler')]` now dispatch `Confirm()` and `Message()`
  calls to the registered `[ConfirmHandler]` and `[MessageHandler]` procedures.
  The `HandlerRegistry` reads handler names from `[NavTest].Handlers`, finds
  matching `[NavHandler]` methods on the test codeunit, and wires them to
  `MockDialog.ALConfirm` and `AlDialog.Message`. `ByRef<bool>` parameters for
  confirm reply are initialized via delegate field wiring.

### Fixed
- **`CompanyName` / `UserId` crash** (#35). AL built-in functions `CompanyName`,
  `UserId`, `TenantId`, and `SerialNumber` caused `NullReferenceException` at
  `ALDatabase.get_ALCompanyName()` because the BC session is not initialized in
  standalone mode. The rewriter now replaces these `ALDatabase` property accesses
  with empty-string literals.

### Added
- **`--generate-stubs` source filtering.** When source directories are provided
  (`--generate-stubs <packages-dir> <output-dir> <src-dir> ...`), only codeunits
  actually referenced in the AL source are generated. Procedure-level filtering
  further limits each stub to only the methods called in source. Falls back to
  generating all codeunits when no source dirs are given (backward compatible).
- **`--generate-stubs` CLI command.** Scaffolds empty AL stub files from `.app`
  symbol packages. Reads `SymbolReference.json` from each `.app` file in the
  packages directory and emits one `.al` file per codeunit with correct procedure
  signatures, parameter types (including `var`, `Record "X"`, `Enum "X"`, etc.),
  and return types with default `exit(...)` values. Existing files are never
  overwritten, and natively mocked codeunits (e.g. codeunit 130) are skipped
  automatically. Non-codeunit objects (tables, pages, etc.) are counted and
  reported but not emitted.
- **RecordRef + FieldRef runtime support.** `MockRecordRef` now delegates all data
  operations (Insert, Modify, Delete, DeleteAll, FindSet, FindFirst, FindLast,
  Next, Count, IsEmpty, SetRange, SetFilter, Reset) to `MockRecordHandle`,
  sharing the same in-memory table store as typed Record variables.
  `MockFieldRef` provides `ALValue` get/set, `ALNumber`, `ALSetRange`,
  `ALSetFilter`, and `ALValidate`.
  Key operations:
  - `RecRef.Open(tableId)` / `RecRef.Close()`
  - `RecRef.Field(n)` returning a `MockFieldRef` with value read/write
  - `RecRef.FindSet()` + `RecRef.Next()` iteration
  - `RecRef.Insert()` / `Modify()` / `Delete()` / `DeleteAll()`
  - `RecRef.GetTable(Rec)` / `RecRef.SetTable(Rec)` for data copy
  - `FieldRef.SetRange()` / `FieldRef.SetFilter()` for filtering
  - `RecRef.Count()` / `RecRef.IsEmpty()` respecting active filters
- **Rewriter: `NavFieldRef` -> `MockFieldRef`.** The rewriter now replaces
  `NavFieldRef` with `MockFieldRef` (previously passed through to the real BC
  type with `null!` parent, which crashed on any property access).

### Fixed
- **`StrSubstNo` with `Integer` (and other `NavValue`) arguments no longer crashes.**
  `ALSystemString.ALStrSubstNo` is now intercepted by `RoslynRewriter` and
  redirected to `AlCompat.StrSubstNo`, which formats each `%1`/`%2`/… placeholder
  using the session-free `AlCompat.Format()`. Prevents the
  `NullReferenceException` in `NavIntegerFormatter.FormatWithFormatNumber` that
  occurred when `NavSession` is null in the runner context.
  ([#33](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/33))

### Added
- **Per-iteration tracking for loop debugging (`--iteration-tracking`).**
  A new CLI flag instruments `for`/`while`/`do` loops at the Roslyn AST level
  and captures, per loop and per iteration: variable values, console messages,
  and executed statement IDs mapped back to AL source lines. Output is appended
  to `--output-json` as an `iterations[]` array. Nested loops are tracked
  independently with parent/child relationships preserved.
  ([#34](https://github.com/StefanMaron/BusinessCentral.AL.Runner/pull/34))

### Fixed (coverage)
- Coverage line numbers corrected from 0-based to 1-based (off-by-one in
  `CoverageReport`).
- `OnRun_Scope` trigger names are now matched correctly by the scope regex.
- Coverage scope→file mapping no longer bleeds library scopes into user
  coverage output.

## [1.0.7] — 2026-04-11

### Added
- **`RecordRef` row-presence operations now read the in-memory store.**
  `Rec.Open(TableId[, Temporary[, CompanyName]])` followed by
  `IsEmpty` / `FindSet` / `Find` / `Next` / `Count` / `Close` consults
  the same shared table store typed `Record X` variables write to, so
  seeding a row via a typed variable is visible through a subsequent
  `RecRef.Open` on the same table id. Field-level access
  (`RecRef.Field(n).Value`) remains out of scope.
  ([#30](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/30))
- **Plain helper procedures on pages can now be called from tests.**
  `MockFormHandle` remembers the page id (the rewriter's constructor
  handling now keeps it instead of stripping) and exposes
  `Invoke(memberId, args)` that reflects over the generated `Page<N>`
  class using the same scope-name encoding MockCodeunitHandle uses.
  Page triggers, layout, actions, and factboxes remain skipped.
  ([#31](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/31))
- **`[EventSubscriber]` procedures fire when their
  `[IntegrationEvent]` / `[BusinessEvent]` is raised in the same
  compilation unit.** Rewriter replaces `βscope.RunEvent()` with
  `AlCompat.FireEvent(publisherCuId, eventName)` and strips the
  `if (γeventScope == null && …) return;` guard BC emits at the top
  of event methods. `EventSubscriberRegistry` scans the assembly for
  `NavEventSubscriberAttribute` via `CustomAttributeData` (reading
  `targetObjectNo` from the second int positional arg — the first
  is the `ObjectType` enum). Sender / Rec parameters pass `null`,
  matching BC's best-effort contract for standalone dispatch.
  ([#32](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/32))

## [1.0.6] — 2026-04-11

### Added
- **Table `trigger OnInsert()` bodies now run** on `Rec.Insert(true)`.
  Trigger firing uses reflection to instantiate the generated
  `Record<N>` class via `GetUninitializedObject` and overwrite its
  compiler-generated `<Rec>k__BackingField` (and xRec) so the
  trigger's `Rec.SetFieldValueSafe` calls mutate the caller's
  MockRecordHandle field bag in place. Falls back to a no-op for
  tables without a declared trigger. Only fires when `runTrigger=true`
  so `Insert(false)` still skips as before.
  ([#27](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/27))
- **`NumberSequence`** stub, **`NavApp.GetModuleInfo`** stub, and
  **`Hyperlink()`** stub — all no-op runtime calls that used to
  trap into BC's service-tier-dependent code paths and throw
  `NullReferenceException` or assembly-load errors.
  ([#14](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/14),
   [#22](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/22),
   [#24](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/24))
- **`Enum::X.Names()` via an enum instance** (`E := E::Draft; E.Names();`)
  now returns the declared member names. Tracked alongside the #17
  static `Enum::"X".Ordinals()` fix. NavOption instances are tagged
  at construction with their source enum id via a
  `ConditionalWeakTable`; rewriter rewrites `.ALNames` / `.ALOrdinals`
  getters to `AlCompat.GetNamesForOption` / `GetOrdinalsForOption`
  which look up the tag. Reassignments via
  `NavOption.Create(existing.NavOptionMetadata, V)` inherit the tag
  through `AlCompat.CloneTaggedOption`.
  ([#28](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/28))
- **Primary-key uniqueness on `Rec.Insert`** — duplicate inserts now
  throw a BC-style "already exists" error so `asserterror` catches
  them. Only enforced when the PK is registered, which is now
  automatic: `TableFieldRegistry` parses the first declared
  `key(...)` block and calls `MockRecordHandle.RegisterPrimaryKey`
  at pipeline start, so synthetic test fixtures don't need to wire
  the registration themselves.
  ([#29](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/29))

### Fixed
- **Multi-field FlowField `exist(...)`** — follow-up to #15 that the
  reporter hit on 1.0.3. Parser now paren-walks the `exist(...)` body
  manually and splits top-level commas in the `where(...)` list.
  Field IDs resolve through a new transpile-time
  `TableFieldRegistry` instead of depending on the runtime
  `RegisterFieldName` path.
  ([#15](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/15) follow-up)
- **`Rec.Validate("DateFormula field")` after `Evaluate`** —
  `DefaultForType` wasn't initialising DateFormula fields to
  `NavDateFormula.Default`, so the first read from a ByRef<T>
  inside `ALSystemVariable.ALEvaluate` hit a `NavText → NavDateFormula`
  cast error. Added the missing branch.
  ([#25](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/25))
- **`Rec.Get(textFromGuidField)`** — Guid values stored as
  `Text[100]` round-tripped to different string forms (braces,
  case, hyphens) than the raw Guid stored in the PK, so `Get` missed
  the match. Added `PkValuesEqual` helper in `ALGet` that tries
  Guid/decimal parse fallbacks before giving up.
  ([#26](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/26))

## [1.0.5] — 2026-04-11

### Fixed
- **`SetFilter` with Option member-name literals** (e.g.
  `SetFilter(Kind, '<>Red&<>Blue')`) now resolves the literal to its
  ordinal via `EnumRegistry.FindOrdinalByMemberName` before comparing,
  instead of producing a string-form mismatch against the stored
  NavOption ordinal. Also harvests inline `OptionMembers = A,B,C;`
  declarations from table fields so Option fields without a separate
  `enum` object resolve too.
  ([#19](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/19) follow-up)
- **~200 ms per-test spike on the first `SetFilter` over a NavOption
  field**. `MockRecordHandle.NavValueToString` used to fall back to
  `value.ToString()` for NavOption (and any other subtype without an
  explicit branch), which traps into BC's `NavFormatEvaluateHelper`
  → triggers `Microsoft.CodeAnalysis` reference resolution + Roslyn
  overload resolution on first use. The fallback is gone; NavOption,
  NavDate, NavDateTime now have explicit branches, NavDecimal uses a
  cached `PropertyInfo` instead of reflecting per call, and unknown
  types return empty string rather than reaching the slow path.
  ([#23](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/23))

### Internal
- New `AlRunner.Tests/NavValueToStringPerfTests.cs` holds the line:
  every test in `tests/52-setfilter-and/` must run in under 50 ms, so
  any regression back into BC's `NavValue.ToString()` fallback fails
  loudly.

## [1.0.4] — 2026-04-11

### Added
- `NavApp.GetModuleInfo` / `GetCurrentModuleInfo` / `GetCallerModuleInfo`
  routed through a new `MockNavApp` stub. The real `ALNavApp` loads
  `Microsoft.Dynamics.Nav.CodeAnalysis` (not shipped with al-runner),
  so any code path that reached NavApp metadata crashed with an
  assembly-load failure. The stub returns `false` for every lookup
  and leaves the ByRef `ModuleInfo` untouched, matching BC's
  "not found" contract. ([#22](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/22))

### Fixed
- **Multi-field FlowField `exist(...)`** now works. 1.0.3 covered the
  single-field case but the multi-condition variant
  (`exist(Child where(C1 = field(X), C2 = field(Y)))`) silently
  returned false: the `CalcFormulaRegistry` regex was non-greedy and
  stopped at the first `)` — the one closing `field(X)` — so the
  second clause was lost. Parser now paren-walks the `exist(...)`
  body manually, splits top-level commas in the `where(...)` body,
  and resolves child field IDs through a new transpile-time
  `TableFieldRegistry` (previously relied on runtime
  `RegisterFieldName`, which only fired when generated code referenced
  `ALFieldNo(name)` explicitly).
  ([#15](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/15) follow-up)

## [1.0.3] — 2026-04-11

### Added
- `CHANGELOG.md` shipped inside the NuGet package; `<PackageReleaseNotes>`
  points nuget.org at it.
- Publish workflow now creates a GitHub Release on tag push, seeded with
  the matching `CHANGELOG.md` section and the `.nupkg` attached.
- Missing-dependency diagnostic now enriches with a namespace-mismatch
  hint when a stub with the matching type+name was loaded under a
  different namespace. ([#9](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/9))
- Server mode: multi-slot LRU cache (8 slots) keyed by a per-file
  fingerprint, and the `runTests` response now includes a `changedFiles`
  array on cache miss so IDE integrations can show change-aware
  feedback. Bouncing between projects in one session no longer
  invalidates the previous entry.
  ([#10](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/10) — MVP; full dep-graph partial recompile still open)
- **Per-statement value capture**: `--capture-values` now emits a
  Quokka-style timeline of intermediate values, not just a
  final-state snapshot. A new `ValueCaptureInjector` pass injects
  `ValueCapture.Capture(...)` after each scope-field assignment,
  keyed by the neighboring `StmtHit(N)` so captures map back to AL
  source lines. Post-test reflection-based capture is kept as a
  fallback for variables the injector can't reach.
  ([#11](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/11))
- **Server `execute` command**: new JSON-RPC command that accepts
  either inline AL (`code`) or `sourcePaths` and runs the first
  codeunit's `OnRun` trigger in run-mode. Response mirrors
  `runTests` plus captured `messages` and optional `capturedValues`.
  ([#12](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/12))
- **Column precision in error mapping**: `TestResult` and
  `--output-json` now include `alSourceColumn` alongside
  `alSourceLine`. `FormatDiagnostic` emits `[AL line ~N col M in X]`.
  The existing `CoverageReport.ParseSourceSpans` encoding already
  carried columns; they were discarded.
  ([#13](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/13))
- **`Enum::X.Ordinals()` / `.Names()`** resolve against a transpile-time
  `EnumRegistry` built from the AL source. BC inlines enums so runtime
  reflection can't recover the member list. ([#17](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/17))
- **Enum-implements-interface dispatch** (`Flag := Strategy;`). BC stores
  the NavOption directly in the interface handle; `MockInterfaceHandle`
  now intercepts it, looks up the per-value
  `Implementation = "Iface" = "Codeunit"` mapping in `EnumRegistry`,
  and resolves the codeunit through the new `CodeunitNameRegistry`.
  ([#20](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/20))
- **Table `InitValue` defaults** applied by `Rec.Init()` via a new
  `TableInitValueRegistry` — supports Boolean, Integer, Decimal, Text
  and Enum member init values.
  ([#18](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/18))
- **FlowField `exist()` `CalcFields`** evaluated against in-memory
  tables via a new `CalcFormulaRegistry`. Supports
  `where(field = field(...))` and `where(field = const(...))`
  conditions; `count` / `sum` / `lookup` still return defaults.
  ([#15](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/15))
- **`NumberSequence`** replaced with a process-local
  `MockNumberSequence` keyed by name. `Exists` / `Insert` / `Next` /
  `Current` / `Restart` no longer throw `NullReferenceException` via
  `NavSession`. ([#14](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/14))
- **`Page "X"` local variables** transpile to a `MockFormHandle`
  stub (like the existing `MockInterfaceHandle` / `MockRecordRef`).
  ([#21](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/21))

### Fixed
- **`SetFilter` AND operator (`&`)** — AL filter expressions with AND
  chains were silently OR-ed, matching too many rows.
  `MatchesFilterExpression` now splits on `|` (OR) first, then on
  `&` (AND) inside each alternative, matching BC's precedence.
  Wildcards, `..` ranges, `@` case-insensitive, and per-field
  AND-across-fields all still work. `%1..%n` placeholder substitution
  covered for integer and text values, including inside mixed AND/OR
  precedence expressions.
  ([#19](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/19))
- **`Page.Run(Page::X, Rec)` / `Page.RunModal`** with fully-qualified
  `NavForm` method access, and `Page "X"` local variable initialisation
  via `NavFormHandle` — both no longer cascade-exclude the containing
  codeunit. (Follow-up to #6, with a real repro via #21.)
- **`RecordRef` 3-arg `Open(tableId, temporary, company)`** now has
  matching `ALOpen(CompilationTarget, int, bool, string)` overloads,
  and `ALIsEmpty` is exposed as a property to match BC's lowering of
  `!recRef.IsEmpty`.
  ([#16](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/16))
- `AL0791 namespace unknown` on an unused `using` directive no longer
  blocks compilation; added to the ignored-error set alongside
  `AL0432` / `AL0433`. Genuine unresolved uses still surface as
  separate errors. ([#8](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/8))
- Regression test for single-arg `Record.Validate("Field")` covering
  Decimal, DateFormula, and error propagation paths. The underlying
  2-arg `ALValidateSafe` overload was added before the report was
  filed; this commit just locks the behavior in.
  ([#7](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/7))

### Internal
- `Pipeline.Run` now redirects both `Console.Out` and `Console.Error`
  into the captured `StringWriter` instances for the duration of the
  run, so `AlDialog.Message` and `PrintResults` no longer corrupt
  the server's stdin/stdout JSON protocol.

## [1.0.2] — 2026-04-11

### Fixed
- `Page.RunModal(PageId, Rec)` as a bare statement no longer emits
  invalid C# (`default(FormResult);`). Strips `NavForm.Run/RunModal/SetRecord`
  at statement level. ([#6](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/6))
- `[TryFunction]`-attributed procedures now compile and run: `AlScope`
  gains `TryInvoke(Action)` / `TryInvoke<T>(Func<T>)` overloads that
  execute the delegate, catch any exception, and return true/false.
  ([#4](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/4))
- `List of [Interface X]` no longer cascades-excludes the containing
  object. New `MockObjectList<T>` replaces BC's `NavObjectList<T>`
  (which requires `T : ITreeObject` and a non-null Tree handler),
  and `ALCompiler.ToInterface(this, x)` is rewritten to
  `MockInterfaceHandle.Wrap(x)`.
  ([#3](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/3))
- Declaring `var RecRef: RecordRef` no longer cascades-excludes the
  containing codeunit. `NavRecordRef` is rewritten to a new
  parameterless `MockRecordRef` stub with no-op Open/Close/IsEmpty/
  Find/Next/Count. Consistent with the documented policy that
  RecordRef/FieldRef compile but do not function at runtime.
  ([#5](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/5))
- `AL0791 namespace unknown` on an unused `using` directive no longer
  blocks compilation; added to the ignored-error set alongside
  `AL0432` / `AL0433`. Genuine unresolved uses still surface as
  separate errors. ([#8](https://github.com/StefanMaron/BusinessCentral.AL.Runner/issues/8))

### CI
- Publish workflow now mirrors the test matrix: runs the C# test
  project and excludes `tests/39-stubs/` from the bulk run, invoking
  it separately with `--stubs`. Builds `AlRunner.slnx` so the test
  DLL exists by the time `dotnet test --no-build` runs.

## [1.0.1] — 2026-04-10

### Changed
- Per-suite test invocation restored (single-invocation run had ID
  conflicts); test timings back to ~75 s total but reliable.

## [1.0.0] — 2026-04-10

### Added
- `--output-json` machine-readable test output.
- `--server` long-running JSON-RPC daemon over stdin/stdout.
- `--capture-values` variable-value capture for Quokka-style inline
  display.
- `--run <ProcedureName>` single-procedure execution.
- Error line mapping via last-statement tracking.
- C# test infrastructure (`AlRunner.Tests/`) covering pipeline,
  server, capture-values, single-procedure, error mapping and
  incremental server-mode caching.

### Changed
- All BC versions 26.0 → 27.5 now run on every push via the test
  matrix workflow.

## [0.2.0] — 2026-04-10

### Added
- `--coverage` Cobertura XML output wired into CI job summaries.
- NuGet package ID standardized to `MSDyn365BC.AL.Runner`.

## [0.1.0] — 2026-04-10

Initial release — AL transpile + Roslyn rewriter + in-memory execution
for pure-logic codeunits. No BC service tier, no Docker, no SQL, no
license. Test runner with `Subtype = Test` discovery and `Assert`
codeunit mock.

[1.0.7]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/releases/tag/v1.0.7
[1.0.6]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/releases/tag/v1.0.6
[1.0.5]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/releases/tag/v1.0.5
[1.0.4]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/releases/tag/v1.0.4
[1.0.3]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/releases/tag/v1.0.3
[1.0.2]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/releases/tag/v1.0.2
[1.0.1]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/releases/tag/v1.0.1
[1.0.0]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/releases/tag/v1.0.0
[0.2.0]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/releases/tag/v0.2.0
[0.1.0]: https://github.com/StefanMaron/BusinessCentral.AL.Runner/releases/tag/v0.1.0
