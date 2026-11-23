# Task Runs: Open Filter Panel, Module and Aggregate Columns — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Task runs page opens with its filter panel shown and filtered to running runs, and lists each run's module and aggregate, filterable by aggregate.

**Architecture:** A new opt-in parameter on the shared list; a new `aggregate` list field that the query handler resolves to task names through `TaskCatalog`, passed to the repository as an extra `ANY` predicate; two new columns in `SerginTaskRunsView`.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.9, Dapper on PostgreSQL, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-23-task-runs-page-design.md`, section "Addendum (2026-11-23)".

## Global Constraints

- SharedKernel code is committed inside `src/SharedKernel` on its branch `task-runs-page`; tests, docs and the pointer bump in the host repo, same branch name.
- Warnings are errors (Sonar, `AnalysisMode=All`); `.razor` files are markup-only, C# in `.razor.cs`.
- Every new text key goes into `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/Types/catalog-keys.txt`.
- No `Co-Authored-By` trailer; commit under the configured git identity.

## Review Focus

- An aggregate filter value naming no aggregate of the module: a validation error, not an empty list.
- A renamed task (`Name("tst.probes.touch")` on root `ScheduleProbe`) matches its root's filter.
- A run whose task the catalogue no longer knows: excluded by any aggregate filter, shown with "—".
- Aggregate filter combined with the Task filter: both apply (AND).
- The count equals the filtered rows (the `ANY` predicate in both statements).

---

### Task 1: Aggregate filter in the run list query

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/Queries/TaskRunList.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/Queries/TaskQueries.cs` (`ITaskQueries.RunsListAsync`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/TaskTexts.cs` (`RunList.Aggregate`, `RunList.Module`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/TaskNames.cs` (expose `RootKey(Type root)`: the kebab root name `Derive` already computes)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Tasks/TaskQueries.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Tasks/TaskRunsQueryTests.cs`, `catalog-keys.txt`

**Interfaces:**
- Produces: `TaskRunListFields.Aggregate = "aggregate"` (`ListField.FilterOnly(Aggregate, ListFieldKind.Text, FilteringType.equals)`); `TaskNames.RootKey(Type) : string`; `ITaskQueries.RunsListAsync(string schema, ListQuery query, IReadOnlyCollection<string>? taskNames, CancellationToken ct)`; `TaskTexts.RunList.Aggregate` and `TaskTexts.RunList.Module` (`FieldText`, labels "Aggregate" and "Module").

- [ ] **Step 1: Write the failing tests**
  - `AggregateFilter_ListsTheRootsTasks_ByCatalogNotByName`: seed a `tst.probes.touch` run and a `tst.probes.other` run (no catalogue entry); filter `aggregate equals "schedule-probe"` → only the touch run, `TotalCount == 1`.
  - `AggregateFilter_AndTaskFilter_BothApply`: aggregate `schedule-probe` + task `tst.probes.other` → no rows, `TotalCount == 0`.
  - `AggregateFilter_UnknownAggregate_IsAValidationError`: value `nope` → `ErrorType.Validation`, code `Filtering`.
- [ ] **Step 2: Run them; they fail.** `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter FullyQualifiedName~TaskRunsQueryTests`
- [ ] **Step 3: Implement.** The handler takes `TaskCatalog`. When the request filters on `aggregate`, it rebuilds the query without that filter (`Filtering.Create` over the remaining `FilterData`, or null when none remain) and collects `Catalog.Commands` where `Schema == request.Schema && TaskNames.RootKey(Root) == value`. No match → `Error.Validation("Filtering", …)`; otherwise it passes the names. The repository adds `AND r.command_name = ANY(@AggregateTasks)` to both statements when `taskNames` is not null. Add the two field texts (`FieldTextGuard` needs `Aggregate`) and their keys to `catalog-keys.txt`.
- [ ] **Step 4: Run the tests; they pass.**
- [ ] **Step 5: Commit** in `src/SharedKernel`, then in the host repo.

### Task 2: Filter panel open at start, Module and Aggregate columns

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Lists/SerginAggregateList.razor.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Tasks/Runs/SerginTaskRunsView.razor`, `SerginTaskRunsView.razor.cs`, `TaskRunRows.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Tasks/TaskRunsPageTests.cs`; `Localization/PseudoLocaleSweepTests.cs` must still pass

**Interfaces:**
- Consumes: Task 1's field id, `TaskNames.RootKey` and texts.
- Produces: `[Parameter] bool FilterPanelInitiallyOpen` on `SerginAggregateList<TItem>`; `SerginTaskRunsView.AggregateFilter : string?` (internal); `TaskRunRows.AggregateText(IStringLocalizer, string commandName, TaskCatalog, AggregateFeatureRegistry) : string`.

- [ ] **Step 1: Write the failing tests**
  - `FilterPanel_IsOpenOnFirstRender`: the rendered page holds `.sergin-filter-panel` with no click.
  - `Rows_ShowModuleAndAggregate`: a seeded `dm.device.read-device-profile` run's row shows the Device Management caption and the Device name.
  - `AppliedFilters_CarryTheAggregate`: `AggregateFilter = "device"` → `BuildFilters()` holds `aggregate equals "device"` with a chip.
  - `DefaultFilter_IsRunning`: the first load (recorded dispatcher) sends `status equals "Running"`, the Running chip is rendered, and a finished seeded run is not listed while a running one is.
  - `DefaultFilter_ReturnsOnModuleChange_NotOnReset`: after Reset the next load sends no filter; a module change sets `StatusFilter` back to `Running`.
- [ ] **Step 2: Run them; they fail.**
- [ ] **Step 3: Implement.** `SerginAggregateList`: `filterPanelOpen = FilterPanelInitiallyOpen` on init; after the first render, invoke `OnFilterPanelOpened` once when it starts open. New `[Parameter] bool ApplyFiltersAtStart`: when set, the applied filters are seeded from `BuildFilters()` before the first load. The view sets `StatusFilter = RunOutcome.Running` wherever it starts a module's list (`OnParametersSet` on a module change, first one included), and passes both parameters. The view: columns Module, then Aggregate, before Task; a filter select of the module's roots, cleared by chip removal, reset and module change; with an aggregate chosen, the Task filter lists only tasks on that root.
- [ ] **Step 4: Run the Tasks and Localization tests; they pass.**
- [ ] **Step 5: Commit** in `src/SharedKernel`, then in the host repo.

### Task 3: Docs, pointer, full suite, browser check

- [ ] Both CLAUDE.md files: the run list text names the open panel, the two columns and the aggregate filter.
- [ ] Bump the SharedKernel pointer; run the full integration suite (Docker running).
- [ ] Headless-browser walk of `/sys/tasks/runs`: panel open at load, both columns, filter by Device.
