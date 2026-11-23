# Task runs page — design

Date: 2026-11-23. Status: approved approaches; design for review.
Mockup: https://claude.ai/artifact/3H7MhkW7GMzWHyPHH9fZbn (the page and the run records dialog).

## Intent and why

Today a module's task runs are visible only per record, per schedule or per person:

- a record's detail page lists the latest 5 runs over that record (every trigger),
- a schedule's runs dialog lists that one schedule's runs,
- a collection task's Schedules dialog lists that task's runs in one scope,
- the activity panel lists the viewer's own runs of the last 24 hours, never a cron run.

Nobody can answer "what ran in this module today, and what failed?" without opening record after record. The
**Task runs** page lists every run of a module's tasks in one place, whatever started it, for the people who
manage tasks.

Vocabulary as fixed by the task-commands design (`2026-11-20-task-commands-design.md`): a *task* is the command,
a *schedule* is when it runs, a *run* is one execution, a *run item* is one record inside a run.

## Goals

- One page, `/sys/tasks/runs`, listing runs of all four triggers (Manual, Once, ScheduleNow, Schedule) for one
  module at a time, newest first, server-paged.
- Shown in the drawer under **Administration**, in its own **Tasks** caption, for holders of
  `permission.sys.tasks.manage`.
- Filters, one value each: task, status, started by, trigger, plus a started-between range.
- The task name opens the run's records in a dialog.
- A Running row can be cancelled, under the existing cancel rules.
- A run reads the same here as in the activity panel (one status rule).

## Non-goals

- Several modules' runs in one table (chosen against, see Approaches).
- Multi-value filters. The shared list criteria have no `in` mode; adding one is its own piece of work.
- Listing or editing schedules on this page. Schedules stay on the record's detail page and the collection task's
  Schedules dialog: a manual run has no stored definition, so the two lists do not merge.
- Run again, retry a failed item, export, bulk cancel, quick search.
- A page for people without `permission.sys.tasks.manage`. Their own runs stay in the activity panel.

## Anti-goals

- A run the caller's grants do not reach must never appear, nor be counted in the total.
- A deleted schedule's history must stay hidden, as it is in the runs dialog today. The page must not become a side
  door around that rule.
- No nav entry in a host where no module opts into tasks: such a host has no `permission.sys.tasks.manage` in its
  permission catalogue, and a nav entry naming an unregistered permission refuses host start.

## Constraints

- SharedKernel is not a module. Only `ISerginWebUiModule`s contribute drawer entries today
  (`SerginUiModuleCatalog`); the home entry is wired separately.
- Every module page route must start with `/{schema}/`; the guard walks `catalog.Modules` only, so a shell page is
  outside it today.
- Task tables live in each opted-in module's own schema (`task_runs`, `task_run_items`); `ITaskQueries` names the
  schema on each call and refuses one no module registered (`Tasks.UnknownSchema`).
- Raw-SQL reads apply the reach rule themselves: a schedule's run is reached through its live schedule
  (`deleted_at_utc IS NULL AND ScopeColumns.ReachSql` on `task_schedules`), any other run through its own
  `scope_path` (`ScopeColumns.ReachSql` on `task_runs`). `RecentRunsAsync` in
  `Infrastructure.Data.EFCore/Tasks/TaskQueries.cs` is the reference.
- The task handlers and the task configuration source are registered only by `AddSerginTasks`, which runs only when a
  module opts in. In a host with none, sending a task query has no handler: MediatR throws and the dispatcher does not
  catch it.
- List criteria support one value per filter (`FilteringType.equals` and the range modes in
  `ListFieldSet.SupportedModes`), over `ListFieldKind` Text, Identifier and Instant.
- `FieldTextGuard` scans module Contracts assemblies only; it never sees SharedKernel's own requests.
- Build rules: analyzers as errors, markup-only `.razor`, every visible text declared, `PseudoLocaleSweepTests` over
  every parameterless route.

## Approaches

### Where the nav entry comes from

| | Approach | Trade-off |
|---|---|---|
| **A (chosen)** | **Shell nav entries.** `SerginUiModuleCatalog` takes shell entries beside modules; each is a group, an optional caption and items, merged into sections exactly as a module is. | Smallest change; SharedKernel keeps owning its own page. Needs one guard rule for shell routes. |
| B | A SharedKernel UI-only pseudo-module implementing `ISerginWebUiModule` with schema `sys`. | Must fake `ApplicationAssembly`, `AddServices`, `MigrateAsync`; MediatR would scan SharedKernel's Application assembly a second time and register the task handlers twice. |
| C | Host the page in the Settings module (`/st/tasks/runs`). | Gets the Administration group for free, but Settings would own a page about something it does not own, a host without Settings loses the page, and it is a change in another repo. |

The user picked A.

### How one list reads several modules' runs

| | Approach | Trade-off |
|---|---|---|
| 1 | Ask every module's source for `offset + pageSize` rows and its count, merge, cut the page. | All modules in one table; deep pages read more rows; a failing module is left out with a warning. |
| 2 | One `UNION ALL` statement over every opted-in schema. | Exact paging, but one broken schema fails the page, and it assumes one database (no Remote module). |
| **3 (chosen)** | **Module selector.** The page lists one module at a time. | Exact paging over one schema, reusing the existing per-schema query shape. Two modules' runs are never side by side. |

The user picked 3. Only DeviceManagement opts in today, so in practice the page lists every run.

## Design

### 1. Shell nav entries (SharedKernel `Presentation.Blazor`, `Hosts.WebUi`)

- New record `SerginShellNavEntry(SerginNavGroup Group, TextDefinition? Caption, IReadOnlyCollection<SerginNavItem> Items)`
  in `Navigation/`.
- `SerginUiModuleCatalog` gains a second constructor argument, `IReadOnlyCollection<SerginShellNavEntry> shellEntries`
  (default empty). A shell entry becomes one `SerginNavSubsection` in the section of its group, ordered with the
  modules' subsections by its lowest item `Order` then caption, as a module's is. `NavItems`, `GroupOf` and the drawer
  see it with no further change; `ShowsCaptions` stays "two or more subsections".
- `ValidateNavPermissions` checks shell entries' `RequiredPermission`s too (it walks `catalog.Modules` today).
- Route guard: every routable component in the shell's own assembly (`typeof(SerginUiModuleCatalog).Assembly`) must
  have the template `/` or one starting with `/sys/`. `/sys/` is the platform's reserved prefix, matching the reserved
  `sys` permission schema; a module whose `Schema` is `sys` is refused at start.
- The Tasks entry is added by the Blazor bootstrap only when a module opted into tasks (a `TasksRegistration` is in the
  service collection once `AddSerginCore` has run its module loop; `CreateUiModuleCatalog` already runs after it).
  Group `SerginNavGroups.Administration`, caption `TaskUiTexts.Navigation.Caption` ("Tasks"), one item
  `TaskUiTexts.Navigation.Runs` ("Task runs"), href `/sys/tasks/runs`, icon `Icons.Material.Filled.WorkHistory` (the
  activity indicator's), `Order` 400, `RequiredPermission = TaskPermissions.Manage`.
- Drawer result (captions show because Administration now holds three subsections):

  ```
  Administration
    USER ACCESS   Users · Scopes
    ───────
    SETTINGS      Languages
    ───────
    TASKS         Task runs
  ```

- Breadcrumb: `Home > Administration > Task runs`.

### 2. One status rule (SharedKernel `Application`)

- `RunOutcome` enum moves from the Blazor-internal `ActivityState` to `Application/Tasks/` with one more member:
  `Running`, `Succeeded`, `PartlyFailed`, `Failed`, `Cancelled`, `Skipped`.
- The rule, in order: Skipped when the run's status is Skipped; Running while any item is unfinished; then, with no
  failed item, Cancelled when any item was cancelled, else Succeeded; with failed items, Failed when none succeeded,
  else PartlyFailed. This is `ActivityRows.StateOf` today plus Skipped (the activity read never returns a Skipped run,
  so the panel is unchanged).
- `RunOutcomes.Of(...)` holds it in C#; `ActivityRows.StateOf` calls it. The SQL `CASE` for the list (below) mirrors
  it, and a test runs one run of each outcome through both and requires the same answer.
- Value texts: `TaskTexts.RunList.Outcomes = Category.Values(...)`; the activity panel keeps its own existing
  wording, and the page uses these.

### 3. The read (SharedKernel `Application` + `Infrastructure.Data.EFCore`)

- `GetTaskRunsQuery(string Schema, Paggination, Term?, Filtering?, Sorting?)`, a `sealed record` deriving from
  `ListQuery<TaskRunsListItem>` (explicit constructor calling `base(...)`), handled through `IListQueryHandler`.
  Configuration: `RequirePermissions(TaskPermissions.Manage)`, `NotShown(x => x.Schema)`,
  `Texts(TaskTexts.RunList.Category)`. Untyped, so checked on the current scope, and the `ScopeFilter` is filled from the
  Manage permission's reach, like the other task reads. Registered by `AddSerginTasks` with the other task handlers
  and configurations.
- `TaskRunListFields : ListFieldSet`, every filter single-valued:

  | Field | Kind | Sort | Filter | SQL expression |
  |---|---|---|---|---|
  | `startedAt` | Instant | yes (default, newest first) | between | `r.started_at_utc` |
  | `task` | Text | yes | equals | `r.command_name` |
  | `trigger` | Text | no | equals | `r."trigger"` |
  | `status` | Text | no | equals | the outcome `CASE` (§2), declared once as a constant beside the columns and used by filter and select alike |
  | `startedBy` | Identifier | no | equals | `r.acting_user_id` |

- `GetTaskRunsQueryValidator : ListQueryValidator<GetTaskRunsQuery>`.
- `ITaskQueries.RunsListAsync(schema, ListQuery, ct)` in `TaskQueries`: the reach predicate of `RecentRunsAsync`,
  then `ListQuerySql.Apply` for filter and order, `r.id` as tie-breaker; count and page in one `QueryMultipleAsync`,
  as every list read does.
- Row: `TaskRunsListItem(Guid Id, string CommandName, TaskRunTrigger Trigger, Guid ActingUserId,
  Guid? ScheduleId, string? ScheduleCron, string? ScheduleTimeZoneId, RunOutcome Outcome, DateTime StartedAtUtc,
  DateTime? CompletedAtUtc, int Total, int Succeeded, int Failed, int Cancelled, int Unfinished, Guid? TargetId,
  string? TargetLabel, int? Progress, bool Cancelable, Guid? ScopeId)`. The one-item fields come from the same
  `LEFT JOIN LATERAL` the activity read uses, `Unfinished` from the same sub-count, and the schedule's cron and zone
  from a join on its live schedule (null for a one-off run).
- `GetTaskRunActorsQuery(string Schema)` answers the distinct acting user ids of the module's reachable runs (same
  reach, same permission), for the Started by filter; loaded when the filter panel opens.
- `TaskRunItemListItem` gains `string? TargetLabel` (the stored `task_run_items.target_label`), so the records dialog
  names each record as the person saw it. The schedule runs dialog benefits from the same field.

### 4. The page (SharedKernel `Presentation.Blazor/Tasks/`)

- `SerginTaskRunsPage` (`@page "/sys/tasks/runs"`, `@layout SerginPageLayout`) hosting `SerginTaskRunsView`, which
  renders through `SerginAggregateListView` + `SerginAggregateList<TaskRunsListItem>` with `Searchable="false"`.
- **No opted-in module**: the modules are the distinct schemas of `TaskCatalog.Commands`. When there are none the view
  sends nothing and shows an empty state ("No module runs tasks in this application."); sending would hit a missing
  handler.
- **Module selector**: each schema named by its UI module's `NavCaption`, else its `NavGroup.Title`
  (`ActivityRows.ModuleCaption`). Hidden while only one module is listed. Defaults to the first in drawer order. Kept
  in the query string, `?module=dm`, so reload and Copy link keep it; an unknown value falls back to the default.
- **Header**: Reload, Copy link.
- **Filter panel**: Task (the module's tasks by view label), Status (§2's six), Started by (from
  `GetTaskRunActorsQuery`, named through `IUserDirectory`), Trigger (Started, Run once, Run now, Schedule), Started
  between. Each applied filter is one removable chip.
- **Columns**:
  - **Task**: the task's view label (`ActivityRows.CommandLabel`), else its stored name. It is a link that opens the
    records dialog. Rows are not click targets, per the list rule.
  - **Records**: a one-record run shows its stored `TargetLabel`, linked to the record's detail page (as the activity
    panel links it); a bulk run shows "{Count} records"; a collection task's run names its scope
    (`TaskUiFormat.ScopeName`).
  - **Started by**, two lines: the person (`IUserDirectory`; for a cron run, the schedule's delegating user, which is
    the run's `ActingUserId`), and under it the trigger: "Started", "Run once", "Run now · {schedule}",
    "Schedule · {schedule}", where `{schedule}` is the schedule's cron described the way the schedule list shows it.
    Schedules have no name, so none is invented.
  - **Started**: local time through `TaskUiFormat.Time` (the viewer's zone), sortable.
  - **Status**: a Running row shows the progress bar exactly as the activity panel draws it (the item's percent for a
    one-record run that reported, `Total - Unfinished` of `Total` for a bulk run, else indeterminate); a finished row
    shows its outcome text and, under it, the run summary (`TaskUiFormat.RunSummary`).
  - **Row command**: `SerginActivityRowAction` on a Running row (Cancel, or "Can't be cancelled").
- **Records dialog**: the run's records, reusing the item table of `SerginTaskRunsDialog` (extracted into a component
  both dialogs render): record (target label, else id; a collection run's scope), status, attempts, error, finished
  at. The heading names the task, the start time, the trigger line and the person.
- **Refresh**: `IRunNotifier` cannot drive it, since it publishes per acting user and this page lists everyone's runs.
  The view polls every 5 s while a listed row is Running (the activity indicator's rule) and stops on dispose.

### 5. Texts

- `TaskTexts.RunList` (Application, under `common.tasks.run-list.*`, apart from the existing `TaskUiTexts.Runs` keys): the category, a `FieldText` per list field, the
  `RunOutcome` value texts.
- `TaskUiTexts` (Presentation.Blazor): `Navigation.Caption` "Tasks", `Navigation.Runs` "Task runs", the page title, the
  column headers, "Module", the trigger lines, the empty state. Reuse `CommonTexts` and existing `TaskUiTexts` texts
  wherever one already says the same thing.
- `catalog-keys.txt` gains the new keys. Because `FieldTextGuard` does not see SharedKernel's requests, a test pins
  that every `TaskRunListFields` field has a `FieldText` in the query's category.

### 6. Tests (host repo, `tests/Sergin.MeterMinder.IntegrationTests.All`)

- `Shell/`: the catalog merges a shell entry into its group's section and orders it; captions show with three
  subsections; the breadcrumb names Administration; the `/sys/` guard (a shell route outside `/` and `/sys/` refused, a
  module with schema `sys` refused); an unregistered shell `RequiredPermission` refused; no Tasks entry in a host where
  no module opts in.
- `Tasks/RunOutcomeTests`: the C# rule and the SQL `CASE` agree on one run of each outcome; the activity panel's states
  are unchanged.
- `Tasks/TaskRunsQueryTests`: all four triggers listed; newest first; each filter; paging and total; a run outside the
  caller's reach neither listed nor counted; a deleted schedule's runs hidden; a one-off run reached by its own scope;
  refused without Manage; unknown schema refused; the field texts present.
- `Tasks/TaskRunsPageTests`: the page prerenders under the Administration trail; selector hidden with one module;
  `?module=` honoured; empty state with no opted-in module; row cells (label, records, the two-line Started by,
  status); Cancel on a Running row; the task link opens the records dialog.
- `Localization/PseudoLocaleSweepTests`: add `/sys/tasks/runs`.

## Calls made without the user (check while reading)

- Route `/sys/tasks/runs`, nav Order 400, WorkHistory icon, caption "Tasks".
- `/sys/` reserved for shell routes; a module schema `sys` refused.
- No quick search; the Task filter instead.
- No default time window; retention (30 days) bounds the list.
- The task name is the link to the records dialog (no row click).
- Two-line Started by, with the schedule's cron as its name (shown in the mockup; not yet confirmed).
- The empty state when no module opts in, rather than sending.
- A test pins the list field texts instead of widening `FieldTextGuard`.
- Poll every 5 s while a row is Running.
- `TaskRunItemListItem` gains `TargetLabel`.
- Module selector hidden with one module, default first in drawer order, kept as `?module=`.

## Left to the builder

- Exact names of the new types and texts, within the conventions above.
- Whether the item table is extracted as a component or the dialog takes a run id.
- Where in `Hosts.WebUi` the Tasks shell entry is added (after `AddSerginCore`'s module loop).
