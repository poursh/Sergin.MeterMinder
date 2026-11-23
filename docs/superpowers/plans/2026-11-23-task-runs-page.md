# Task Runs Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `/sys/tasks/runs` page, under Administration › Tasks in the drawer, listing every run of one module's tasks (all four triggers) for holders of `permission.sys.tasks.manage`.

**Architecture:** SharedKernel gains shell nav entries (the catalog merges them like modules), one shared run-status rule, a paged list query over one module's `task_runs` with the existing reach rule, and a Blazor page built on `SerginAggregateListView`. The host bootstrap adds the Tasks entry only when a module opts into tasks.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.9, MediatR, Dapper over PostgreSQL, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-23-task-runs-page-design.md`

## Global Constraints

- Work in the worktree `.claude/worktrees/task-runs-page`, branch `task-runs-page`. SharedKernel code changes are commits **inside `src/SharedKernel`** (its own repo, branch `task-runs-page` there too); host-repo changes (tests, docs, submodule pointer) are commits in the worktree root.
- Build: `dotnet build Sergin.MeterMinder.slnx` from the worktree root. Warnings are errors (`TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer). No `@code` blocks: every `.razor` has a `.razor.cs` partial.
- Tests need Docker running: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter <name>`.
- Every visible text is a declared `TextDefinition`/`FieldText`; components take `TextDefinition`, never `string` labels. Every new key goes into `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/Types/catalog-keys.txt` in the task that declares it (`MigrationPinTests` fails otherwise).
- Route `/sys/tasks/runs`. Nav: group `SerginNavGroups.Administration`, caption "Tasks", item "Task runs", `Order` 400, icon `Icons.Material.Filled.WorkHistory`, `RequiredPermission = TaskPermissions.Manage`.
- Filters are single-valued (`FilteringType.equals`, plus `between` on the start time). No `in` mode.
- Status values, in this order: `Running`, `Succeeded`, `PartlyFailed`, `Failed`, `Cancelled`, `Skipped`; texts "Running", "Succeeded", "Partly failed", "Failed", "Cancelled", "Skipped".
- Reach: a schedule's run through its live schedule (`deleted_at_utc IS NULL AND ScopeColumns.ReachSql` on `task_schedules`), any other run through its own scope (`ScopeColumns.ReachSql` on `task_runs`) — exactly `RecentRunsAsync` in `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Tasks/TaskQueries.cs`.
- Commit under the user's git identity; no `Co-Authored-By` trailer.

## Review Focus

- **A run whose completion was lost** (status Running, no unfinished item, counts 0) must not read as "Running" forever; by the shared rule it reads Succeeded until recovery completes it. Pinned in Task 2 (`Of_StaleRunningWithNothingUnfinished_IsSucceeded`).
- **Status filter on a derived value**: filtering `status = Running` must match exactly the rows whose shown status is Running, including bulk runs whose counts are still 0. Pinned in Task 3 (`StatusFilter_MatchesTheShownStatus_ForEveryOutcome`).
- **A typed `?module=` that names no opted-in schema** (or a stale bookmark) must fall back to the default module, not send `Tasks.UnknownSchema`. Pinned in Task 5 (`UnknownModuleParameter_FallsBackToTheDefault`).
- **The total must exclude rows out of reach** — a regional manager sees neither the South run nor a count of it. Pinned in Task 3 (`OutOfReachRun_NeitherListedNorCounted`).
- **A cron run of a since-deleted schedule** stays hidden (the history goes with its schedule), even though its run row still exists. Pinned in Task 3 (`DeletedSchedulesRuns_AreHidden`).

---

### Task 1: Shell nav entries in the catalog

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Navigation/SerginShellNavEntry.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Modules/SerginUiModuleCatalog.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Shell/NavSectionCatalogTests.cs`

**Interfaces:**
- Produces: `public sealed record SerginShellNavEntry(SerginNavGroup Group, TextDefinition? Caption, IReadOnlyCollection<SerginNavItem> Items);` and `SerginUiModuleCatalog(IReadOnlyCollection<ISerginWebUiModule> modules, IReadOnlyCollection<SerginShellNavEntry>? shellEntries = null)` plus `public IReadOnlyCollection<SerginShellNavEntry> ShellEntries { get; }`.

- [ ] **Step 1: Write the failing tests** (in `NavSectionCatalogTests`, reusing its `NavModule` and `TestText` helpers)

```csharp
[Fact]
public void ShellEntry_JoinsItsGroupsSection_AsOneMoreSubsection_OrderedByLowestEntry()
{
    SerginUiModuleCatalog catalog = new(
        [new NavModule("cc", SharedGroup, [new(TestText.Of("B"), "/cc/b", string.Empty, Order: 210)], TestText.Of("Caption C"))],
        [new SerginShellNavEntry(SharedGroup, TestText.Of("Tasks"), [new(TestText.Of("Runs"), "/sys/runs", string.Empty, Order: 400)])]);

    SerginNavSection shared = Assert.Single(catalog.NavSections);
    Assert.Equal(["Caption C", "Tasks"], shared.Subsections.Select(s => s.Caption?.Default));
    Assert.Equal(["/cc/b", "/sys/runs"], shared.Items.Select(i => i.Href));
    Assert.True(shared.ShowsCaptions);
    Assert.Equal(SharedGroup, catalog.GroupOf("/sys/runs"));
}

[Fact]
public void ShellEntry_InAGroupNoModuleReturns_FormsItsOwnSection() // one section, captions hidden (one subsection)

[Fact]
public void ShellEntryWithNoItems_GetsNoSubsection()
```

- [ ] **Step 2: Run to verify they fail** — `--filter NavSectionCatalogTests` — FAIL: constructor takes one argument.

- [ ] **Step 3: Implement.** `NavSections` groups modules and shell entries together by `SerginNavGroup` (value equality); a shell entry becomes `new SerginNavSubsection(entry.Caption, items in drawer order)` and sorts among the module subsections by its first item's `Order`, then caption — the existing `InDrawerOrder` rule. `NavItems` and `GroupOf` need no change beyond reading the merged sections. `RoutableAssemblies` and `Modules` are unchanged (the shell assembly is already routable).

- [ ] **Step 4: Run** `--filter NavSectionCatalogTests` — PASS; then `--filter "NavVisibilityTests|BreadcrumbRenderingTests"` — PASS (no regression).

- [ ] **Step 5: Commit** in `src/SharedKernel` (`Shell nav entries in the UI catalog`), then in the worktree root (`Tests: shell nav entries`).

---

### Task 2: One run-status rule

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/RunOutcome.cs` (enum + `RunOutcomes`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Tasks/ActivityRows.cs` (delete `ActivityState`; `ActivityRow.State` becomes `RunOutcome`; `StateOf` calls `RunOutcomes.Of`) and every user of `ActivityState` (`grep -rn ActivityState src tests`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/TaskTexts.cs` (add `RunList`, below)
- Test: create `tests/Sergin.MeterMinder.IntegrationTests.All/Tasks/RunOutcomeTests.cs`; update `Tasks/ActivityRowsTests.cs`

**Interfaces:**
- Produces: `public enum RunOutcome { Running, Succeeded, PartlyFailed, Failed, Cancelled, Skipped }`; `public static class RunOutcomes { public static RunOutcome Of(TaskRunStatus status, int unfinished, int succeeded, int failed, int cancelled); }`; `TaskTexts.RunList` (nested `public static class`): `Category = TaskTexts.Category.Child("RunList")` (keys `common.tasks.run-list.*`), `FieldText`s `StartedAt` "Started", `Task` "Task", `Trigger` "Trigger", `Status` "Status", `StartedBy` "Started by", and `public static readonly EnumTexts<RunOutcome> Outcomes = Category.Values(...)` with the six texts of Global Constraints (shape: Settings' `LanguageTexts.StatusValues`).

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData(TaskRunStatus.Skipped, 0, 0, 0, 0, RunOutcome.Skipped)]
[InlineData(TaskRunStatus.Running, 3, 0, 0, 0, RunOutcome.Running)]
[InlineData(TaskRunStatus.Completed, 0, 12, 0, 0, RunOutcome.Succeeded)]
[InlineData(TaskRunStatus.Completed, 0, 11, 1, 0, RunOutcome.PartlyFailed)]
[InlineData(TaskRunStatus.Completed, 0, 0, 2, 0, RunOutcome.Failed)]
[InlineData(TaskRunStatus.Completed, 0, 25, 0, 15, RunOutcome.Cancelled)]
[InlineData(TaskRunStatus.Completed, 0, 3, 1, 5, RunOutcome.PartlyFailed)]
public void Of_FollowsTheRule(TaskRunStatus status, int unfinished, int succeeded, int failed, int cancelled, RunOutcome expected) =>
    Assert.Equal(expected, RunOutcomes.Of(status, unfinished, succeeded, failed, cancelled));

[Fact]
public void Of_StaleRunningWithNothingUnfinished_IsSucceeded() =>
    Assert.Equal(RunOutcome.Succeeded, RunOutcomes.Of(TaskRunStatus.Running, 0, 0, 0, 0));
```

- [ ] **Step 2: Run** `--filter RunOutcomeTests` — FAIL: `RunOutcome` not defined.

- [ ] **Step 3: Implement** `RunOutcomes.Of`, in order: `Skipped` when `status == Skipped`; `Running` when `unfinished > 0`; with `failed == 0`: `Cancelled` if `cancelled > 0` else `Succeeded`; with failures: `Failed` if `succeeded == 0` else `PartlyFailed`. Switch `ActivityRows` to it; the activity panel's texts and behaviour do not change. Add `TaskTexts.RunList` and its keys to `catalog-keys.txt`.

- [ ] **Step 4: Run** `--filter "RunOutcomeTests|ActivityRowsTests|ActivityIndicatorRenderingTests|MigrationPinTests"` — PASS.

- [ ] **Step 5: Commit** (SharedKernel: `One run-status rule shared by activity and run lists`; root: tests + keys).

---

### Task 3: The run list query

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/Queries/TaskRunList.cs` — `GetTaskRunsQuery`, `TaskRunListFields`, its handler and `GetTaskRunsQueryValidator`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/Queries/TaskResponses.cs` (add `TaskRunsListItem`), `ITaskQueries.cs`, `TaskQueryConfigurations.cs`, `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/TaskConfigurationTypes.cs` (append the configuration)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Tasks/TaskQueries.cs` (`RunsListAsync`), `TaskServiceExtensions.cs` (handler + validator registrations, beside the other task reads)
- Test: create `tests/Sergin.MeterMinder.IntegrationTests.All/Tasks/TaskRunsQueryTests.cs` (host, tree and caller setup copied from `TaskQueryTests`; seed runs through the store and `TasksDbContext` the way `TaskQueryTests.AddOneOffRunAsync`/`CreateTaskAsync`/`FireAsync` do, setting status and counts directly on the rows for the outcome cases)

**Interfaces:**
- Consumes: `RunOutcome`, `RunOutcomes`, `TaskTexts.RunList` (Task 2).
- Produces:
  - `public sealed record GetTaskRunsQuery : ListQuery<TaskRunsListItem>` with constructor `(string Schema, Paggination paggination, Term? term = default, Filtering? filtering = default, Sorting? sorting = default) : base(paggination, term, filtering, sorting)` and `public string Schema { get; }`.
  - `public static class TaskRunListFields` with consts `StartedAt = "startedAt"`, `Task = "task"`, `Trigger = "trigger"`, `Status = "status"`, `StartedBy = "startedBy"` and `public static ListFieldSet Set` = `startedAt` (Instant, sortable, `between`), `task` (Text, sortable, `equals`), `trigger` (Text, `equals`), `status` (Text, `equals`), `startedBy` (Identifier, `equals`) — `new ListField(id, kind, Sortable, modes)` for the two that are both.
  - `public sealed record TaskRunsListItem(Guid Id, string CommandName, TaskRunTrigger Trigger, Guid ActingUserId, Guid? ScheduleId, string? ScheduleCron, string? ScheduleTimeZoneId, RunOutcome Outcome, DateTime StartedAtUtc, DateTime? CompletedAtUtc, int Total, int Succeeded, int Failed, int Cancelled, int Unfinished, Guid? TargetId, string? TargetLabel, int? Progress, bool Cancelable, Guid? ScopeId);`
  - `ITaskQueries.RunsListAsync(string schema, ListQuery query, CancellationToken ct) : Task<ErrorOr<ListQueryResponse<TaskRunsListItem>>>`.
  - Configuration `GetTaskRunsQueryConfiguration`: `RequirePermissions(TaskPermissions.Manage).Texts(TaskTexts.RunList.Category).NotShown(x => x.Schema)`.

- [ ] **Step 1: Write the failing tests** — each sends through `ISerginDispatcher` from a caller scope, as `TaskQueryTests.SendAsync` does:
  - `ListsEveryTrigger_NewestFirst` — runs started in the order Manual, Once, ScheduleNow, Schedule; `Items.Select(i => i.Trigger)` equals `[Schedule, ScheduleNow, Once, Manual]`; `TotalCount == 4`.
  - `Paged_TotalCountsEveryMatch` — 5 runs, page size 2, page 2 returns runs 3–4, total 5.
  - `TaskFilter`, `TriggerFilter`, `StartedByFilter`, `StartedBetweenFilter` — each `equals`/`between` narrows rows and total.
  - `StatusFilter_MatchesTheShownStatus_ForEveryOutcome` — one run per outcome; for each `RunOutcome` value, filtering `status = <name>` returns exactly the rows whose `Outcome` is that value, and each row's `Outcome == RunOutcomes.Of(row's status, Unfinished, Succeeded, Failed, Cancelled)`.
  - `OneItemRun_CarriesTargetLabelAndProgress_BulkRunDoesNot`.
  - `CronRun_CarriesItsSchedulesCron_OneOffRunNone`.
  - `OutOfReachRun_NeitherListedNorCounted` — caller granted Manage at North; a South run is absent and `TotalCount` excludes it.
  - `DeletedSchedulesRuns_AreHidden`.
  - `OneOffRun_ReachedByItsOwnScope`.
  - `WithoutManage_IsForbidden` — `ErrorType.Forbidden`.
  - `UnknownSchema_IsRefused` — `Tasks.UnknownSchema`.
  - `UndeclaredSort_IsAValidationError` — sorting by `trigger` → `Error.Validation` coded `Sorting`.
  - `EveryListField_HasAFieldText` — for each id in `TaskRunListFields.Set`, `TextCatalog` has `common.tasks.run-list.fields.<kebab-id>.label` (pins what `FieldTextGuard` does not see).

- [ ] **Step 2: Run** `--filter TaskRunsQueryTests` — FAIL: `GetTaskRunsQuery` not defined.

- [ ] **Step 3: Implement.** `TaskQueries.RunsListAsync`: `TryQuote` the schema; the WHERE is `RecentRunsAsync`'s reach predicate plus `ListQuerySql.Apply(query, RunListColumns, parameters).Where`; count and page through one `QueryMultipleAsync` (`SELECT count(*) …; SELECT … ORDER BY {OrderBy} LIMIT @PageSize OFFSET @Offset;`). `RunListColumns` is a static `ListQueryColumns(TaskRunListFields.Set, { startedAt → r.started_at_utc, task → r.command_name, trigger → r."trigger", status → OutcomeSql, startedBy → r.acting_user_id }, searchColumns: [], tieBreaker: "r.id")`; default order when no sorting is sent is `startedAt` descending. `OutcomeSql` is one private const mirroring `RunOutcomes.Of` over `r.status`, `r.succeeded`, `r.failed`, `r.cancelled` and the unfinished sub-count `(SELECT count(*) FROM {schema}.task_run_items u WHERE u.run_id = r.id AND u.status IN ('Pending','Running','Retrying'))`, answering the enum member names — it contains the schema, so compose it per call like the other SQL here. Select the activity read's `LEFT JOIN LATERAL` one-item columns and a `LEFT JOIN {schema}.task_schedules s ON s.id = r.schedule_id AND s.deleted_at_utc IS NULL` for `ScheduleCron`/`ScheduleTimeZoneId` (keep Reach unambiguous: the reach predicate stays in its own `EXISTS`, as in `RecentRunsAsync`). `Cancelable` from `CancelableOf`. The one composed send carries the existing scoped `#pragma warning disable S2077`. Register the handler (`IRequestHandler<GetTaskRunsQuery, ErrorOr<ListQueryResponse<TaskRunsListItem>>>`) and `IValidator<GetTaskRunsQuery>` (scoped) in `AddSerginTasks`.

- [ ] **Step 4: Run** `--filter "TaskRunsQueryTests|TaskQueryTests|MigrationPinTests"` — PASS.

- [ ] **Step 5: Commit** (SharedKernel: `Task runs list query`; root: tests + keys).

---

### Task 4: Run actors and run-item labels

**Files:**
- Modify: `TaskRunList.cs` (add `GetTaskRunActorsQuery` + handler), `TaskQueryConfigurations.cs`, `TaskConfigurationTypes.cs`, `ITaskQueries.cs`, EFCore `TaskQueries.cs`, `TaskServiceExtensions.cs`
- Modify: `TaskResponses.cs` (`TaskRunItemListItem` gains a trailing `string? TargetLabel = null`), EFCore `TaskQueries.RunItemsAsync` (select `i.target_label AS TargetLabel`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Tasks/SerginTaskRunsDialog.razor.cs` (`TargetText` prefers `TargetLabel`, then the id, then the scope)
- Test: `TaskRunsQueryTests.cs`, `TaskQueryTests.cs`

**Interfaces:**
- Produces: `public sealed record GetTaskRunActorsQuery(string Schema) : IQuery<IReadOnlyCollection<Guid>>;` (config: Manage, `NotShown(Schema)`); `ITaskQueries.RunActorsAsync(string schema, CancellationToken ct) : Task<ErrorOr<IReadOnlyCollection<Guid>>>` — distinct `acting_user_id` of reachable runs, ordered.

- [ ] **Step 1: Write the failing tests** — `Actors_AreTheDistinctActingUsersInReach` (two users, three runs, one South run's user absent); `RunItems_CarryTheStoredTargetLabel` (in `TaskQueryTests`).
- [ ] **Step 2: Run** `--filter "TaskRunsQueryTests|TaskQueryTests"` — FAIL.
- [ ] **Step 3: Implement** with the same reach predicate as Task 3.
- [ ] **Step 4: Run** the same filter plus `TaskUiTests` — PASS.
- [ ] **Step 5: Commit.**

---

### Task 5: The Task runs page

**Files (all under `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Tasks/Runs/`):**
- Create: `SerginTaskRunsPage.razor` (`@page "/sys/tasks/runs"`, `@layout SerginPageLayout`, `<SerginTaskRunsView Module="Module" />`) + `.razor.cs` (`[SupplyParameterFromQuery(Name = "module")] public string? Module { get; set; }`)
- Create: `SerginTaskRunsView.razor` + `.razor.cs` — `SerginAggregateListView` around `SerginAggregateList<TaskRunsListItem>` (`Searchable="false"`, filter panel, `RowCommands`)
- Create: `TaskRunRows.cs` — pure, `internal static`: `StartedByLine`, `RecordsText`, module ordering
- Create: `SerginRunItemsTable.razor` + `.razor.cs` (extracted from `SerginTaskRunsDialog`'s item table; `[Parameter] IReadOnlyCollection<TaskRunItemListItem> Items`, `Guid? RunScope`), and `SerginRunItemsDialog.razor` + `.razor.cs` (`[Parameter] string Schema`, `TaskRunsListItem Run`; loads `GetTaskRunItemsQuery`)
- Modify: `SerginTaskRunsDialog.razor` to render `SerginRunItemsTable`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Tasks/TaskUiTexts.cs` — nested `RunList` on `category.Child("RunList")`: `Title` "Task runs", `Module` "Module", `Records` "Records", `NoModule` "No module runs tasks in this application.", `NoRuns` "No runs match.", `TriggerManual` "Started", `TriggerOnce` "Run once", `TriggerScheduleNow` "Run now", `TriggerSchedule` "Schedule", `TriggerWithSchedule` "{Trigger} · {Schedule}", `AnyValue` "Any"
- Test: create `tests/Sergin.MeterMinder.IntegrationTests.All/Tasks/TaskRunsPageTests.cs` (bUnit-free, like `TaskUiTests`: prerendered HTML through the real host, plus `TaskRunRows` unit tests)

**Interfaces:**
- Consumes: `GetTaskRunsQuery`, `TaskRunListFields`, `TaskRunsListItem` (Task 3); `GetTaskRunActorsQuery`, `TaskRunItemListItem.TargetLabel` (Task 4); `RunOutcome`, `TaskTexts.RunList.Outcomes` (Task 2); existing `ActivityRows.CommandLabel`, `ActivityRows.ModuleCaption`, `ActivityRows.RecordsText`, `TaskUiFormat.Time`, `TaskUiFormat.ScopeName`, `TaskUiFormat.RunSummary` (adapt it to take counts if it only takes `TaskRunListItem`), `SerginActivityRowAction`, `SerginAggregateList.ReloadAsync()`.
- Produces: `TaskRunRows.Modules(TaskCatalog tasks, SerginUiModuleCatalog ui) : IReadOnlyList<string>` (distinct task schemas, in `ui.Modules` order, then any other schema by ordinal); `TaskRunRows.StartedByLine(TaskRunsListItem run) : (TextDefinition Trigger, string? Schedule)`; the route constant `SerginTaskRunsPage.Route = "/sys/tasks/runs"` used by Task 6.

- [ ] **Step 1: Write the failing tests**
  - `Modules_FollowTheUiModulesOrder_ThenOrdinal` (unit, over `TaskCatalog.FromTypes` + a test catalog).
  - `StartedByLine_PerTrigger` — Manual → (`TriggerManual`, null); Once → (`TriggerOnce`, null); ScheduleNow with cron `0 9 * * *` → (`TriggerScheduleNow`, `"0 9 * * *"`); Schedule → (`TriggerSchedule`, `"0 9 * * *"`); a cron run whose schedule is gone (cron null) → (`TriggerSchedule`, null).
  - `Page_Prerenders_UnderTheAdministrationTrail` — GET `/sys/tasks/runs` as the dev user: 200, breadcrumb contains "Administration" and "Task runs", `<title>` "Task runs".
  - `ModuleSelector_HiddenWithOneModule` — no element `id="sergin-task-runs-module"` in the real host (DeviceManagement only).
  - `UnknownModuleParameter_FallsBackToTheDefault` — GET `/sys/tasks/runs?module=zz` renders like the default (no problem panel, no error).
  - `NoOptedInModule_ShowsTheEmptyStateAndSendsNothing` — `TaskRunRows.Modules` over an empty `TaskCatalog` is empty, and the view's branch renders `NoModule` (render the view with an empty catalog through the same in-process render helper `ActivityIndicatorRenderingTests` uses).
  - `Rows_RenderTheirCells` — seed a one-record Manual run (label `MTR-0042`), a 12-record Schedule run with 1 failure, a Running run; the rendered rows (rendered in process and started by hand, as `ActivityIndicatorRenderingTests` does, since `MudTable` loads after first render) show the task label, `MTR-0042` linked to the device page, "12 records", the person over "Schedule · 0 9 * * *", "Partly failed", and Cancel only on the Running row.
  - `TaskLink_OpensTheRecordsDialog_WithTheStoredLabels`.
- [ ] **Step 2: Run** `--filter TaskRunsPageTests` — FAIL.
- [ ] **Step 3: Implement.**
  - The view computes `modules = TaskRunRows.Modules(...)`; empty → `NoModule` text, no `Load`; else selected = `Module` if listed, else `modules[0]`. The selector (`MudSelect`, `id="sergin-task-runs-module"`) renders only when `modules.Count > 1`; a change navigates to `?module=<schema>` (`NavigationManager.GetUriWithQueryParameter`).
  - `Load` builds `new GetTaskRunsQuery(schema, request.Paggination, request.Term, request.Filtering, request.Sorting)` from the `SerginListRequest` the list passes, and remembers whether any loaded row's `Outcome == Running`.
  - Filter panel fields: Task (`MudSelect` over the schema's `TaskCatalog` entries, labelled by `ActivityRows.CommandLabel` else name), Status (`TaskTexts.RunList.Outcomes`), Started by (`GetTaskRunActorsQuery`, loaded on `OnFilterPanelOpened`, named through `IUserDirectory`), Trigger (the four trigger texts), Started between (two date pickers → one `between` filter); `BuildFilters` turns the set fields into `SerginAppliedFilter`s; `OnFilterRemoved`/`OnFiltersReset` clear them.
  - Columns per the spec §4: Task (a `MudLink` opening `SerginRunItemsDialog` through `IDialogService`), Records (`TargetLabel` linked to the root's detail href the way `ActivityRows` builds `Href`; `"{Count} records"`; scope name for a collection run), Started by (two lines: `IUserDirectory` name; `TriggerWithSchedule` when a schedule cron is present, else the trigger text), Started (`TaskUiFormat.Time`, `MudTableSortLabel SortLabel="startedAt"`), Status (`MudProgressLinear` as the activity row draws it while Running; else the outcome text and the run summary), Row command `SerginActivityRowAction` while Running (on cancel success, `ReloadAsync()`).
  - Poll: a `PeriodicTimer` of 5 s started after first render, ticking `ReloadAsync()` through `InvokeAsync` only while the last load held a Running row; disposed with the view (`IAsyncDisposable`).
  - Texts to `catalog-keys.txt`.
- [ ] **Step 4: Run** `--filter "TaskRunsPageTests|TaskUiTests|ScheduleDialogArgumentsTests|MigrationPinTests"` — PASS.
- [ ] **Step 5: Commit.**

---

### Task 6: Host wiring — the Tasks entry and the shell route guard

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Tasks/Runs/SerginTaskNavigation.cs` — `public static class SerginTaskNavigation { public static SerginNavItem Runs { get; } ; public static SerginShellNavEntry Entry { get; } }` (values from Global Constraints; caption `TaskUiTexts.Navigation.Caption` "Tasks", label `TaskUiTexts.Navigation.Runs` "Task runs" — add a nested `Navigation` class to `TaskUiTexts`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs` — `CreateUiModuleCatalog(modules, services)` passes `ShellEntriesFor(services)`; `ValidateRoutePrefixes` and `ValidateNavPermissions` cover the shell; both extracted as `internal static` (`ShellEntriesFor(IServiceCollection)`, `RouteViolations(SerginUiModuleCatalog catalog, Assembly shellAssembly) : IReadOnlyList<string>`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/Sergin.SharedKernel.Hosts.WebUi.csproj` — `<InternalsVisibleTo Include="Sergin.MeterMinder.IntegrationTests.All" />` (the shape `Presentation.Blazor.csproj` uses)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` — the schema guard refuses a local or remote module whose `Schema` is `sys`, naming the module type
- Test: create `tests/Sergin.MeterMinder.IntegrationTests.All/Shell/ShellNavEntryTests.cs`; extend `Localization/PseudoLocaleSweepTests.cs` (add `"/sys/tasks/runs"` to the route list)

**Interfaces:**
- Consumes: `SerginShellNavEntry` and the catalog constructor (Task 1); `SerginTaskRunsPage.Route` (Task 5); `TasksRegistration` (`Sergin.SharedKernel.Infrastructure.Data.EFCore.Tasks`, reachable through `Hosts`).

- [ ] **Step 1: Write the failing tests**
  - `RealHost_ShowsTaskRunsUnderAdministration_WithItsCaption` — the host's `SerginUiModuleCatalog`: the Administration section's subsections' captions are `["User Access", "Settings", "Tasks"]`, and `GroupOf("/sys/tasks/runs") == SerginNavGroups.Administration`.
  - `ShellEntriesFor_IsEmpty_WithoutATasksRegistration` and `…_HoldsTheTasksEntry_WithOne` (a bare `ServiceCollection`).
  - `ShellRoute_OutsideSys_IsAViolation` — `RouteViolations(catalog, typeof(ScheduleProbePage).Assembly)` names `ScheduleProbePage` (its route is not `/` or `/sys/`); `RouteViolations(realCatalog, typeof(SerginUiModuleCatalog).Assembly)` is empty.
  - `ModuleWithSchemaSys_IsRefused` — `AddSerginCore` with a test module whose `Schema` is `"sys"` throws `InvalidOperationException` naming it (shape: the existing duplicate-schema test; `grep -rn "same schema" tests`).
  - `UnregisteredShellPermission_IsRefused` — `ValidateNavPermissions` over a catalog with a shell entry requiring an unregistered definition throws naming the entry's label.
  - `PseudoLocaleSweepTests` gains the route and passes.
- [ ] **Step 2: Run** `--filter "ShellNavEntryTests|PseudoLocaleSweepTests"` — FAIL.
- [ ] **Step 3: Implement.** `ShellEntriesFor` answers `[SerginTaskNavigation.Entry]` when `services.Any(d => d.ServiceType == typeof(TasksRegistration))`, else `[]`; it runs after `AddSerginCore` in `AddSerginBlazorApp` (where `CreateUiModuleCatalog` already is). The shell route rule: a template is `/` or starts with `/sys/`. Keys to `catalog-keys.txt`.
- [ ] **Step 4: Run** the full suite: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` — PASS (TickerQ-gated tests skip).
- [ ] **Step 5: Commit.**

---

### Task 7: Docs, submodule pointer, browser check

**Files:**
- Modify: `src/SharedKernel/.claude/CLAUDE.md` — "Tasks" gains a bullet for the run list (query, reach, the status rule, the page, the shell entry); "Presentation.Blazor" gains shell nav entries and the `/sys/` rule.
- Modify: `.claude/CLAUDE.md` (root) — the Blazor UI host bullets: shell nav entries and the `/sys/` prefix; the tests list gains `TaskRunsQueryTests`, `TaskRunsPageTests`, `RunOutcomeTests`, `ShellNavEntryTests`.
- Modify: the submodule pointer `src/SharedKernel` in the worktree root.

- [ ] **Step 1:** Push the SharedKernel branch `task-runs-page` and commit the pointer bump in the root.
- [ ] **Step 2:** Run the host (`dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.All`) and walk the page in the headless browser (Playwright, as in the memory note): drawer shows Administration › Tasks › Task runs; start Read profile on a device; the row shows Running with a Cancel; it turns Succeeded within the poll; the task link opens the records dialog naming the device. Report what was checked.
- [ ] **Step 3: Commit** docs.
