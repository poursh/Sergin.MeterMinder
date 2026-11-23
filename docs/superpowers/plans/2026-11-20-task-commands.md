# Task Commands Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace scheduled commands and background commands with one task command type (`ITaskCommand`), configured in one `Task(...)` block, while keeping today's run mechanics.

**Architecture:** First a mechanical rename to the new vocabulary (`…Tasks`, `TaskSchedule`, `TaskRun`, `task_*` tables) with no behavior change. Then the new surface goes in on top: command types, `TaskSettings` and derived names, the type-based guard, inactivity timeouts, cancelable, collection tasks with overlap, and argument forms in the generated views. The executor, store, engines and hosted services are reshaped, not rewritten.

**Tech Stack:** .NET 10, MediatR, EF Core + Npgsql, Dapper, FluentValidation, Blazor Server + MudBlazor 9.9.0, TickerQ 10.5.1, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-20-task-commands-design.md` (read it first; this plan does not repeat its reasoning). Wireframes: https://claude.ai/artifact/Rufn1czFweezBeg89UiRhg

## Global Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: every commit must build clean with `dotnet build Sergin.MeterMinder.slnx`.
- Two repos: SharedKernel code lives in the `src/SharedKernel` submodule (branch `task-commands` there); everything else in the host repo (branch `task-commands-design`). Commit in the submodule first, then in the host (including the submodule pointer).
- Commit under the user's git identity only. **No `Co-Authored-By` trailer.**
- Worktree: `c:/@factory/Sergin/Sergin.MeterMinder-tasks`. Run `git submodule update --init --recursive` there before the first build, then `git -C src/SharedKernel switch -c task-commands`.
- Integration tests need Docker running (launch Docker Desktop and poll `docker info` first).
- `.razor` files carry no `@code`; all C# in `.razor.cs`. Form models carry no DataAnnotations.
- Every visible text is a declared `TextDefinition` with an English default; new shared keys under `common.tasks.*`.
- A scaffolded migration must be converted to a file-scoped namespace by hand before building.
- Defaults pinned by the spec: `InactivityTimeout` 5 minutes, allowed range (0, 1 hour]; `MaxDuration` none, must be ≥ the inactivity timeout; cancelable on; schedulable off; Read profile `InactivityTimeout(TimeSpan.FromMinutes(2))`.
- Error codes: `Tasks.MustRunAsTask`, `Tasks.NotSchedulable`, `Tasks.NotCancelable`, `Tasks.AlreadyRunning` (Conflict), `Tasks.TimedOut`, `Tasks.MaxDurationExceeded`; every other `ScheduledTasks.*` code keeps its suffix under `Tasks.*`.
- Failure texts: "Timed out after {Timeout} without progress." and "Stopped after the {MaxDuration} limit."
- Task name: `<schema>.<root type name, kebab>.<command type name minus "Command", kebab>`; Read profile's is `dm.device.read-device-profile`.

## Review Focus

1. **A report that arrives after the inactivity timer already fired** must not revive the item: the outcome stays `Tasks.TimedOut`. Test in Task 5.
2. **A handler that ignores its `CancellationToken`** after an inactivity expiry: the item is still recorded `Tasks.TimedOut` when the send finally returns, never Succeeded. Test in Task 5.
3. **Two collection-task starts racing** (two tabs clicking at once): exactly one run, the other `Tasks.AlreadyRunning`, not a `DbUpdateException` surfacing as 500. Test in Task 8.
4. **A saved schedule whose task later loses `Schedulable()`** (configuration edited after the schedule was saved): the cron fire fails the run with `Tasks.NotSchedulable` rather than throwing, and the schedules section still lists it so it can be deleted. Test in Task 4.
5. **A bulk start with arguments where one selected record fails validation** (for example a record out of reach): nothing is started and the error names that record, as today's start does for id-only tasks. Test in Task 10.

---

### Task 1: Rename to the task vocabulary (no behavior change)

**Files:**
- Rename: every file and folder under `src/SharedKernel/**/ScheduledTasks/` to `…/Tasks/`, and the project `src/SharedKernel/Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ` to `Sergin.SharedKernel.Infrastructure.Tasks.TickerQ` (update `Sergin.MeterMinder.slnx`, `Sergin.SharedKernel.slnx` and every `ProjectReference`).
- Rename: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/` to `Tasks/` and every type in it.
- Modify: `src/Modules/DeviceManagement/**` usages, `DeviceManagementDbContext`, `src/Hosts/Sergin.MeterMinder.Hosts.All/{Program.cs,appsettings*.json}`, `docker-compose/docker-compose.yml`, `docker-compose/.files/seed/dev-seed.sql`, `dev-seed-users.sql`.
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Migrations/<ts>_ReplaceScheduledTasksWithTasks.cs` (scaffolded).

**Interfaces:**
- Produces: every name in the spec's "Renames" table, plus `BackgroundRunRef` → `TaskRunRef`, `IBackgroundRunSource` → `ITaskRunSource`, `IBackgroundRunDirectory` → `ITaskRunDirectory`, `BackgroundTarget` → `TaskTarget`, `BackgroundRunStartedResponse` → `TaskRunStartedResponse`, `SerginDispatcherBackgroundExtensions.StartAsync` → `SerginDispatcherTaskExtensions.StartTaskAsync` (same parameters for now), `ScheduledTaskTarget` → `TaskScheduleTarget`, `ScheduledRunItemStatus` → `TaskRunItemStatus`. `Schedulable(name)` and `Background(timeout)` keep their names and behavior in this task.

- [ ] **Step 1: Rename types, namespaces, folders and the TickerQ project** with IDE/`git mv`. Keep every behavior and assertion.
- [ ] **Step 2: Rename tables, indexes and columns** in `TasksModelBuilderExtensions.ApplyTasks()`: `task_schedules`, `task_schedule_targets`, `task_runs`, `task_run_items`; `scheduled_task_id`/`task_id` FK columns become `schedule_id`; indexes `ix_task_runs_schedule_id_running`, `ix_task_runs_schedule_id_started_at_utc`, `ix_task_run_items_unfinished_last_activity`, `ix_task_run_items_target_id_run_id`, `ix_task_runs_acting_user_id_started_at_utc`. Update every raw-SQL string (`TaskQueries`, `EfTaskStore`, `LocalTaskRunSource`, `ScopePathRewriteHandler`).
- [ ] **Step 3: Rename the options section, permission and text keys**: `Sergin:Tasks` (`MaxBackgroundTargets` → `MaxTargets`), `permission.sys.tasks.manage` (`TaskPermissions.Manage`), `common.tasks.*` (`TaskTexts`, `TaskUiTexts`). Update `appsettings*.json`, `docker-compose.yml`, both seed files (`fa` rows' keys only), `Localization/catalog-keys.txt`, and the README's user-secret key.
- [ ] **Step 4: Scaffold the migration**

Run: `dotnet ef migrations add ReplaceScheduledTasksWithTasks --project src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All`
Then hand-edit it: `Down`-safe drop of the four `scheduled_task*` tables in `Up` (drop runs-items, runs, targets, tasks in FK order) followed by the creates the tool wrote; convert to file-scoped namespace. This migration is re-scaffolded in Task 8 (nothing is merged yet, so replacing it is safe).

- [ ] **Step 5: Build and run the renamed suite**

Run: `dotnet build Sergin.MeterMinder.slnx` → 0 warnings, 0 errors.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter "FullyQualifiedName~Tasks|FullyQualifiedName~Devices|FullyQualifiedName~Shell|FullyQualifiedName~Aggregates|FullyQualifiedName~Localization|FullyQualifiedName~Permissions"` → all pass (the TickerQ-licence tests skipped as before).

- [ ] **Step 6: Commit**

```bash
git -C src/SharedKernel add -A && git -C src/SharedKernel commit -m "Rename scheduled tasks to tasks (no behavior change)"
git add -A && git commit -m "Rename scheduled tasks to tasks; migration ReplaceScheduledTasksWithTasks"
```

---

### Task 2: Task command types and the type guard

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Types/TaskEffects.cs`
- Modify: `…/Commands/Types/CommandTarget.cs` (`CommandTypes`), `…/Commands/Configuration/CommandConfigurationRegistry.cs` (version derivation), `src/SharedKernel/Sergin.SharedKernel.Hosts/Commands/CommandTypeGuard.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Devices/Commands/ReadProfile/ReadDeviceProfileCommand.cs`
- Modify: `tests/.../Tasks/TaskTestTypes.cs` (probe records become `ITaskCommand<ScheduleProbe, ScheduleProbeId>`; remove `TouchProbeChildCommand` and its configuration and tests here, since child tasks are dropped)
- Test: `tests/.../Commands/Types/TaskCommandTypeTests.cs`, `tests/.../Commands/Types/TaskCommandTypeTestTypes.cs`

**Interfaces:**
- Produces:
  - `public interface ITaskCommand : IBaseCommand;`
  - `public interface ITaskCommand<TRoot, TId> : ICommand, IEntityTarget<TRoot, TId>, ITaskCommand where TRoot : IAggregateRoot;`
  - `public interface ITaskCommand<TRoot> : ICommand, ICollectionTarget<TRoot>, ITaskCommand where TRoot : IAggregateRoot;`
  - `CommandTypes.IsTask(Type requestType) -> bool`
  - `ITaskCommand<,>` and `ITaskCommand<>` count as effect interfaces in `CommandTypes.EffectsOf`.

- [ ] **Step 1: Write the failing tests** in `TaskCommandTypeTests`:
  - `EntityTask_TargetsItsRootEntity`: `CommandTypes.TargetOf(typeof(EntityTaskProbe))` is `(ScheduleProbe, Entity)`; `IsTask` true.
  - `CollectionTask_TargetsTheCollection`: kind `Collection`, `IsTask` true.
  - `Task_OnVersionedRoot_RequiresNoExpectedVersion`: registry settings for a task on `VersionedProbe` have `RequiresExpectedVersion == false` with no `WithoutExpectedVersion()`.
  - `Guard_Refuses_TaskWithSecondEffect` (`ITaskCommand<…>` + `IUpdateCommand<…>`), `Guard_Refuses_BareMarker` (implements only `ITaskCommand`), `Guard_Refuses_SelectableCollectionTask`, `Guard_Refuses_ChildTargetTask` (`ITaskCommand<…>` + `IChildEntityTarget<…>`): each `CommandTypeGuard` run throws `InvalidOperationException` whose message contains the offending type's full name.
- [ ] **Step 2: Run** `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter TaskCommandTypeTests` → FAIL (types not defined).
- [ ] **Step 3: Implement** the interfaces in `TaskEffects.cs`, `IsTask`, the effect registration, the version rule (a task never requires an expected version; `WithoutExpectedVersion()` on a task is refused as "waives nothing", like any other dead opt-out), and the four guard refusals.
- [ ] **Step 4: Change `ReadDeviceProfileCommand`** to `: ITaskCommand<Device, DeviceIntenralId>, ISelectable` and drop the transition comment; drop `.WithoutExpectedVersion()` from its configuration; convert test probes the same way.
- [ ] **Step 5: Run** the new tests plus `--filter "FullyQualifiedName~Tasks|FullyQualifiedName~Commands"` → PASS. `MigrationPinTests` still pins twenty guarded commands.
- [ ] **Step 6: Commit** (submodule, then host): `Add ITaskCommand types and type-guard refusals`.

---

### Task 3: `TaskSettings`, the `Task(...)` builder and derived names

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/TaskSettings.cs`, `TaskBuilder.cs`, `TaskConfigurationExtensions.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/TaskNames.cs` (moves the regex from `ScheduledCommandNames`, adds `Derive`)
- Delete: `BackgroundSettings.cs`; `Schedulable(string)` and `Background(TimeSpan?)` on `CommandConfigurationBuilder<TCommand>`
- Modify: `CommandSettings.cs`, `CommandConfigurationRegistry.cs`, `Tasks/TaskCatalog.cs`, `Tasks/TaskDescriptor.cs`, every reader of `ScheduleName`/`Background` (`CommandView`, `GeneratedViewSupport`, activity code)
- Modify: `ReadDeviceProfileCommandConfiguration.cs`, test probe configurations
- Test: `tests/.../Tasks/TaskDeclarationTests.cs` (replaces `BackgroundDeclarationTests`), `tests/.../Tasks/TaskCatalogTests.cs` (replaces `ScheduledCommandCatalogTests`), `tests/.../Tasks/TaskNamePinTests.cs`, `tests/.../Tasks/task-names.txt`

**Interfaces:**
- Consumes: `CommandTypes.IsTask` (Task 2).
- Produces:
  - `public sealed record TaskSettings(string Name, TimeSpan InactivityTimeout, TimeSpan? MaxDuration, bool Cancelable, bool Schedulable)` with `public static TimeSpan DefaultInactivityTimeout` (5 min) and `MaxInactivityTimeout` (1 h).
  - `CommandSettings.Task : TaskSettings?` (replaces `ScheduleName` and `Background`); non-null exactly for task requests, configured or not.
  - `public static CommandConfigurationBuilder<TCommand> Task<TCommand>(this CommandConfigurationBuilder<TCommand> builder, Action<TaskBuilder> configure) where TCommand : ITaskCommand`
  - `TaskBuilder`: `InactivityTimeout(TimeSpan)`, `MaxDuration(TimeSpan)`, `NotCancelable()`, `Schedulable()`, `Name(string)`, each returning `TaskBuilder`.
  - `TaskNames.Derive(string? schema, Type root, Type request) -> string`; `TaskNames.IsValid(string) -> bool`.
  - `TaskDescriptor(string Name, string? Schema, Type RequestType, Type Root, CommandTargetKind TargetKind, Type? IdType, bool Selectable, TaskSettings Settings)` (no `RootIdType`).
  - `TaskCatalog.ByName(string)`, `TaskCatalog.ByRequest(Type)` as today's catalogue.

- [ ] **Step 1: Write the failing tests**
  - `TaskDeclarationTests.UnconfiguredTask_GetsEveryDefault`: settings `Task` = (derived name, 5 min, null, true, false).
  - `…Task_DeclaresEachTrait`: `.InactivityTimeout(2 min).MaxDuration(10 min).NotCancelable().Schedulable().Name("tst.probes.old")` reads back exactly.
  - `…Refuses_InactivityTimeoutOutOfRange` (0 and 61 min), `…Refuses_MaxDurationBelowInactivity`: host start throws naming the configuration and request types; the 61-minute message names "1 hour".
  - `…NonTask_HasNoTaskSettings`: `CommandSettings.Task` is null for `UpdateDeviceCommand`.
  - `TaskCatalogTests.DerivesName`: `ReadDeviceProfileCommand` → `dm.device.read-device-profile`; a collection probe `SyncProbesCommand` on `ScheduleProbe` in test schema `tst` → `tst.schedule-probe.sync-probes`.
  - `…Refuses_MalformedName`, `…Refuses_NameOutsideSchema`, `…Refuses_DuplicateNames` (one derived, one `Name(...)` colliding): each message names every offending request.
  - `TaskNamePinTests.TaskNames_MatchThePinnedList`: the real host's `TaskCatalog` names, sorted, equal the lines of `task-names.txt`, which holds exactly `dm.device.read-device-profile`.
- [ ] **Step 2: Run** `--filter "TaskDeclarationTests|TaskCatalogTests|TaskNamePinTests"` → FAIL.
- [ ] **Step 3: Implement** the records, builder, extension and registry validation; `TaskNames.Derive` kebab-cases PascalCase (`ReadDeviceProfile` → `read-device-profile`) and strips a trailing `Command`. Delete `Schedulable(name)`/`Background(timeout)`/`BackgroundSettings` and fix every reader. Executor and starter read `TaskSettings.InactivityTimeout` where they read `Background.Timeout` (still a fixed timeout until Task 5).
- [ ] **Step 4: Update Read profile's configuration** to the spec's shape: `RequirePermissions(DevicePermissions.ReadProfile).Task(task => task.InactivityTimeout(TimeSpan.FromMinutes(2)).Schedulable())`. Update probe configurations; schedulable probes call `.Schedulable()`.
- [ ] **Step 5: Run** the new tests and `--filter "FullyQualifiedName~Tasks|FullyQualifiedName~Devices|FullyQualifiedName~Aggregates"` → PASS.
- [ ] **Step 6: Commit**: `Declare tasks with Task(...) and derive task names`.

---

### Task 4: The type-based guard, typed start, and the schedulable refusal

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/TaskCommandGuardBehavior.cs` (replaces `BackgroundCommandGuardBehavior`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` (behavior registration, first), `Tasks/Commands/StartTaskCommand.cs`, `Tasks/Commands/TaskScheduleRules.cs`, `Tasks/TaskScheduleRunner.cs`, `Tasks/TaskErrors.cs`, `Presentation.Blazor/Dispatching/SerginDispatcherTaskExtensions.cs`
- Test: `tests/.../Tasks/TaskGuardTests.cs` (replaces `BackgroundGuardTests`), `tests/.../Tasks/TaskStartTests.cs` (replaces `BackgroundStartTests`), additions to `TaskScheduleCommandTests`

**Interfaces:**
- Consumes: `CommandTypes.IsTask`, `TaskCatalog`, `TaskSettings.Schedulable`.
- Produces:
  - `StartTaskCommand(ITaskCommand Template, IReadOnlyCollection<TaskTarget> Targets) : ICommand<TaskRunStartedResponse>`
  - `SerginDispatcherTaskExtensions.StartTaskAsync<TTask>(this ISerginDispatcher dispatcher, TTask template, IReadOnlyCollection<TaskTarget> targets, CancellationToken ct = default) where TTask : ITaskCommand -> Task<ErrorOr<TaskRunStartedResponse>>`
  - `TaskTarget(Guid? TargetId, string? Label = null)` (`RootId` removed)
  - Schedule commands' `Template` typed `ITaskCommand`.
  - `TaskErrors.MustRunAsTask`, `TaskErrors.NotSchedulable` (code constants `MustRunAsTaskCode`, `NotSchedulableCode`).

- [ ] **Step 1: Write the failing tests**
  - `TaskGuardTests.TaskSentInline_IsRefused`: `ISender.Send(new ReadDeviceProfileCommand(id))` outside a run item → one error, code `Tasks.MustRunAsTask`, type Validation; handler not called.
  - `…UnconfiguredTask_SentInline_IsRefused` (proves it reads the type, not configuration).
  - `…NonTask_SentInline_Passes`.
  - `TaskScheduleCommandTests.CreateSchedule_ForNonSchedulableTask_IsRefused` → `Tasks.NotSchedulable`; same for `RunTaskOnceCommand`.
  - Review Focus 4, `…CronFire_OfTaskThatLostSchedulable_FailsTheRun`: insert a schedule row for a task whose catalogue entry has `Schedulable = false`, fire `TaskScheduleRunner` → run completed Failed with `Tasks.NotSchedulable`, no exception; `GetTaskSchedules` still lists the schedule.
  - `TaskStartTests.Start_WithNoTargets_ForEntityTask_IsRefused` (validation, `Targets`).
- [ ] **Step 2: Run** `--filter "TaskGuardTests|TaskStartTests|TaskScheduleCommandTests"` → FAIL.
- [ ] **Step 3: Implement** the behavior (checks `request is ITaskCommand` and `IRunItemContext.RunItemId is null`), register it first, retype `StartTaskCommand`, `TaskTarget` and the schedule templates, add the schedulable rule to the schedule validators and to `TaskScheduleRunner` before it starts a run. Remove `BackgroundCommandGuardBehavior`.
- [ ] **Step 4: Run** the same filter plus `FullyQualifiedName~Tasks` → PASS.
- [ ] **Step 5: Commit**: `Guard tasks by type; typed StartTaskAsync; refuse non-schedulable tasks`.

---

### Task 5: Inactivity timeout, `MaxDuration` and `ITaskProgress`

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Tasks/ITaskProgress.cs` (replaces `IRunProgress.cs`), `Tasks/TaskTimeouts.cs`
- Modify: `Tasks/IRunItemContext.cs` (`RunItemContext` gains the timeout handle), `Tasks/TaskRunItemExecutor.cs`, `Tasks/TaskErrors.cs`, `Tasks/TaskTexts.cs`, `ReadDeviceProfileCommandHandler.cs`
- Test: `tests/.../Tasks/TaskTimeoutTests.cs`, `tests/.../Tasks/TaskProgressTests.cs` (replaces `RunProgressTests`)

**Interfaces:**
- Produces:
  - `public interface ITaskProgress { ValueTask ReportAsync(int percent, CancellationToken ct = default); ValueTask KeepAliveAsync(CancellationToken ct = default); }`
  - `internal sealed class TaskTimeouts : IDisposable` — `Token`, `ResetInactivity()`, `InactivityExpired`, `MaxDurationExpired`; built from `TaskSettings` and `IDateTimeProvider`-independent `CancellationTokenSource.CancelAfter`.
  - `RunItemContext.Timeouts : TaskTimeouts?` set by the executor for the send.
  - `TaskErrors.TimedOut(TimeSpan inactivity)` (`Tasks.TimedOut`), `TaskErrors.MaxDurationExceeded(TimeSpan cap)` (`Tasks.MaxDurationExceeded`); texts per Global Constraints.

- [ ] **Step 1: Write the failing tests** (probes with `InactivityTimeout(1 s)` unless named):
  - `ReportingTask_RunsPastOneInactivityTimeout`: reports every 400 ms for 3 s → Succeeded.
  - `SilentTask_TimesOut`: waits 3 s without reporting → Failed `Tasks.TimedOut`, description contains "without progress".
  - `KeepAlive_ResetsTheTimer`: `KeepAliveAsync` every 400 ms for 3 s → Succeeded, `progress` column stays null.
  - `ThrottledReports_StillResetTheTimer`: reports every 300 ms (below `TaskProgress.WriteInterval`, 2 s) for 3 s → Succeeded.
  - `MaxDuration_StopsATaskThatKeepsReporting`: `MaxDuration(2 s)`, reports every 300 ms for 5 s → Failed `Tasks.MaxDurationExceeded`.
  - Review Focus 1, `ReportAfterExpiry_DoesNotRevive`: silent 1.5 s then reports and returns → `Tasks.TimedOut`.
  - Review Focus 2, `TaskIgnoringToken_IsStillTimedOut`: handler sleeps 3 s with `CancellationToken.None` → `Tasks.TimedOut`, not Succeeded.
  - `Cancel_BeatsInactivityTimeout` and `HostShutdown_IsNotATimeout` carried over from today's tests against the new sources.
- [ ] **Step 2: Run** `--filter "TaskTimeoutTests|TaskProgressTests"` → FAIL.
- [ ] **Step 3: Implement**: the executor creates `TaskTimeouts` before the send, links its token with the cancel source and the engine token, stores it on `RunItemContext`; `TaskProgress.ReportAsync` and `KeepAliveAsync` call `ResetInactivity()` before the throttle check; the outcome reads `InactivityExpired`/`MaxDurationExpired` after the send (timeout wins over a late success, cancel wins over both, shutdown propagates). The heartbeat never touches `TaskTimeouts`.
- [ ] **Step 4: Switch** `ReadDeviceProfileCommandHandler` to `ITaskProgress`.
- [ ] **Step 5: Run** the filter plus `FullyQualifiedName~Tasks|FullyQualifiedName~Devices` → PASS.
- [ ] **Step 6: Commit**: `Inactivity timeout reset by progress; MaxDuration cap`.

---

### Task 6: Cancelable

**Files:**
- Modify: `Tasks/Commands/CancelTaskRunCommand.cs`, `Tasks/TaskErrors.cs`, `Tasks/Activity/ActivityItem.cs` (add `Cancelable`), `Tasks/Queries/TaskResponses.cs` (`TaskRunListItem.Cancelable`), `Presentation.Blazor/Tasks/ActivityRows.cs`, `SerginActivityIndicator.razor{,.cs}`, `SerginTaskSchedulesSection.razor{,.cs}`, `TaskUiTexts.cs`
- Test: `tests/.../Tasks/CancelRunTests.cs`, `tests/.../Shell/ActivityIndicatorRenderingTests.cs`, `tests/.../Tasks/TaskUiTests.cs`

**Interfaces:**
- Produces: `TaskErrors.NotCancelable` (`Tasks.NotCancelable`, Conflict); `ActivityItem.Cancelable : bool`; `TaskUiTexts.Activity.NotCancelable` ("Can't be cancelled").

- [ ] **Step 1: Write the failing tests**
  - `CancelRunTests.NotCancelableTask_RefusesItsOwnUser` and `…RefusesManageHolder` → `Tasks.NotCancelable`, the run keeps running.
  - `ActivityIndicatorRenderingTests.NotCancelableRunningRow_ShowsNoCancelButton`: the row renders "Can't be cancelled" and no Cancel button; a cancelable row still has one.
  - `TaskUiTests.SchedulesSection_NotCancelableOneOffRun_HasNoCancel`.
- [ ] **Step 2: Run** the filter → FAIL.
- [ ] **Step 3: Implement**: the cancel handler reads `TaskCatalog.ByName(run.CommandName).Settings.Cancelable` before any write; the reads carry `Cancelable` from the catalogue; the UI hides the button.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit**: `NotCancelable tasks refuse cancel and hide the button`.

---

### Task 7: Drop the child-task path

**Files:**
- Modify: `Tasks/TaskCommandFactory.cs` (`Build(TaskDescriptor, string arguments, Guid? targetId)`), `Tasks/TaskCatalog.cs` (refuse `ChildEntity`), `src/SharedKernel/Sergin.SharedKernel.Domain/Tasks/{TaskSchedule,TaskRun,TaskRunItem}.cs` (remove `RootId`), `TasksModelBuilderExtensions.cs`, `TaskRunStarter.cs`, `EfTaskStore.cs`, `TaskQueries.cs`, `ActivityRows.cs` (`ActivityItem.RootId` removed), `GeneratedViewSupport.cs`
- Test: delete child probes' tests; `TaskCatalogTests.Refuses_ChildEntityTask`

**Interfaces:**
- Produces: `TaskCommandFactory.Build(TaskDescriptor task, string arguments, Guid? targetId) -> ErrorOr<IBaseCommand>`; no `RootId` anywhere in `…Tasks`.

- [ ] **Step 1: Write the failing test** `TaskCatalogTests.Refuses_ChildEntityTask` (a probe implementing `ITaskCommand<…>` and `IChildEntityTarget<…>` is already refused by Task 2's guard; this test pins the catalogue's own message for a hand-built source).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Remove** `RootId` from domain, model, store, queries, activity and the factory; `rg RootId src/SharedKernel/*/Tasks` returns nothing.
- [ ] **Step 4: Run** `--filter FullyQualifiedName~Tasks|FullyQualifiedName~Shell` → PASS.
- [ ] **Step 5: Commit**: `Drop child-entity tasks`.

---

### Task 8: Collection tasks and the overlap rule

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Domain/Tasks/TaskRun.cs` (`TargetKind`), `TaskRunItem.cs` (`TargetId : Guid?`), `TasksModelBuilderExtensions.cs` (index), `TaskRunStarter.cs`, `TaskScheduleRunner.cs`, `StartTaskCommand.cs` validator, `TaskErrors.cs`, `ActivityRows.cs` + `ActivityItem.cs` (`ScopeLabel` for a collection run), `TaskQueries.cs`
- Delete + re-scaffold: `…/Migrations/<ts>_ReplaceScheduledTasksWithTasks.cs` (and Designer, snapshot) with the final model
- Test: `tests/.../Tasks/CollectionTaskTests.cs`, `tests/.../Tasks/TaskModelTests.cs`, `tests/.../Tasks/DeviceManagementTasksModelTests.cs`

**Interfaces:**
- Produces: `TaskRunTargetKind { Entity, Collection }` stored as name in `task_runs.target_kind`; index `ix_task_runs_collection_running` on `(command_name, scope_id) WHERE status = 'Running' AND target_kind = 'Collection'`; `TaskErrors.AlreadyRunning(string taskLabel, string scopeName)` (`Tasks.AlreadyRunning`, Conflict); `ActivityItem.ScopeId : Guid?` for collection runs.

- [ ] **Step 1: Write the failing tests** (probe `SyncProbesCommand : ITaskCommand<ScheduleProbe>`, which `ScheduleProbe` is `Scoped()` in the test context):
  - `CollectionTask_StartsOneItemWithNoTarget`: `StartTaskAsync(new SyncProbesCommand(), [])` → one run, one item, `target_id IS NULL`, `target_kind = 'Collection'`, Succeeded.
  - `CollectionTask_PermissionCheckedOnRunScope`: a caller holding the permission only on North starts in South → Forbidden, nothing written.
  - `SecondStart_SameScope_IsRefused` → `Tasks.AlreadyRunning`; `SameTask_OtherScope_Starts`.
  - Review Focus 3, `RacingStarts_OneWinsOneAlreadyRunning`: two `StartTaskAsync` calls through `Task.WhenAll` from two scopes → exactly one run row, the other result `Tasks.AlreadyRunning` (the starter maps Postgres 23505 on `ix_task_runs_collection_running` to that error).
  - `CronFire_WhileRunning_IsSkipped` → a Skipped run with `Tasks.AlreadyRunning`.
  - `Index_RefusesDuplicateWhenCheckBypassed`: raw insert of a second Running collection run → 23505.
  - `TaskModelTests`/`DeviceManagementTasksModelTests`: the `dm` model carries the new index, nullable `target_id`, no `root_id`, `target_kind` NOT NULL.
- [ ] **Step 2: Run** `--filter "CollectionTaskTests|TaskModelTests|DeviceManagementTasksModelTests"` → FAIL.
- [ ] **Step 3: Implement** the model, starter (advisory check, then insert; 23505 on that index → `AlreadyRunning`), runner skip, validator (entity task needs ≥1 target, collection task exactly 0), activity scope label.
- [ ] **Step 4: Re-scaffold the migration**: remove `ReplaceScheduledTasksWithTasks` (`dotnet ef migrations remove …`), add it again, re-add the hand-written drops of the four `scheduled_task*` tables at the top of `Up`, convert to file-scoped namespace.
- [ ] **Step 5: Run** the filter, then the full suite `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All` → PASS.
- [ ] **Step 6: Commit**: `Collection tasks with one running run per name and scope`.

---

### Task 9: Generated views place tasks by type

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Model/CommandView.cs` (`Task : TaskSettings?` replaces `ScheduleName`/`Background`), `AggregateViewModelJoin.cs`, `Views/Parts/GeneratedCommandActions*.cs`, `Views/Parts/GeneratedViewSupport.cs`, `Views/Parts/GeneratedListBody.razor.cs`, `Views/SerginGeneratedListView.razor{,.cs}`, `Views/SerginGeneratedDetailView.razor.cs`, `Tasks/ActivityPanelState.cs` (`IsRunning(name, Guid? targetId, Guid? scopeId)`), `Tasks/SerginTaskSchedulesSection.razor.cs` (`TargetId : Guid?`), new `Tasks/SerginTaskSchedulesDialog.razor{,.cs}`
- Test: `tests/.../Aggregates/GeneratedTaskViewTests.cs` (replaces `GeneratedBackgroundCommandTests`), `SplitButtonRenderingTests.cs`, `GeneratedDeviceViewTests.cs`

**Interfaces:**
- Consumes: `CommandTypes.IsTask`, `TaskSettings`, `StartTaskAsync`.
- Produces: `CommandView.Task`; list-header task actions; `SerginTaskSchedulesDialog` (`Schema`, `TaskName`, `ScopeId`) opened by the "Schedules" menu item; `ActivityPanelState.IsRunning` overload for collection runs.

- [ ] **Step 1: Write the failing tests** (through `ViewProbe*Page`/`ViewProbeTestHost` and the real Device pages):
  - `EntityTask_NotSchedulable_HasNoScheduleMenu`; `EntityTask_Schedulable_HasScheduleAndViewSchedules`.
  - `CollectionTask_RendersInListHeader` with Schedule… and Schedules when schedulable; clicking Start creates one collection run.
  - `CollectionTask_Running_ShowsSpinner`; `CollectionTask_SecondClick_NotifiesAlreadyRunning` (one snackbar naming the task and scope).
  - `SchedulesMenu_OpensDialogListingCollectionSchedules`.
  - `GeneratedDeviceViewTests.ReadProfile_StillStartsOnClick` (no form, run started).
- [ ] **Step 2: Run** `--filter "GeneratedTaskViewTests|SplitButtonRenderingTests|GeneratedDeviceViewTests"` → FAIL.
- [ ] **Step 3: Implement** placement by `CommandView.Task` and target kind; non-task commands never get Schedule… or the spinner.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit**: `Generated views place entity and collection tasks`.

---

### Task 10: Argument forms (dialog, page, inline) and bulk

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Views/SerginGeneratedTaskFormView.razor{,.cs}` (renders a task's arguments as a `MudForm` via the existing generated-form field machinery; parameters `Id : Guid?`, `Ids : IReadOnlyCollection<Guid>?`, `Mode : SerginEditMode`), `Views/Parts/TaskFormDialog.razor{,.cs}`
- Modify: `Configuration/CommandViewBuilder.cs` (add `PageRoute(Func<Guid?, string> route)` for `Edit(SerginEditMode.Page)` on a task), `AggregateViewGuard.cs` (remove `EnsureSchedulableFromId`; add `EnsureTaskArgumentsBuildable` and "Page mode needs `PageRoute`"), `GeneratedCommandActions*.cs`, `GeneratedViewSupport.cs` (template built from the form, id filled per item), `SerginGeneratedDetailView.razor` + `SerginGeneratedListView.razor` (inline section slot)
- Modify (tests only): `tests/.../Aggregates/ViewProbe*` gains `ProbeWindowTaskCommand(ScheduleProbeId Id, ProbeNote Note, int Hours) : ITaskCommand<…>, ISelectable` with a validator (`Hours` 1–24) and `FieldText`s, configured in three probe views (dialog, page with a `ViewProbeTaskPage.razor` at `/tst/probes/{Id:guid}/window`, inline)
- Test: `tests/.../Aggregates/TaskArgumentFormTests.cs`, `AggregateViewGuardTests.cs`

**Interfaces:**
- Consumes: `CommandView.Task`, `ISerginFormValidator.RulesFor`, `StartTaskAsync`.
- Produces: `CommandViewBuilder<TCommand>.PageRoute(Func<Guid?, string>)`; `SerginGeneratedTaskFormView<TRoot, TTask>`.

- [ ] **Step 1: Write the failing tests**
  - `Dialog_OpensForm_ValidatesAndStarts`: click → dialog with Note and Hours; Hours 0 shows the validator's message and Start is disabled; valid values → run started with those arguments stored.
  - `Page_NavigatesToFormPage_StartsAndReturns`: breadcrumb ends `… › <record> › <task label>`; Start leaves to the detail route.
  - `Inline_RendersSection_KeepsValuesAfterStart`.
  - `NoShownArguments_StartsOnClick` (Read profile shape).
  - `Bulk_OneFormForAllSelected`: three selected rows → one dialog; one run with three items, each with the same Note/Hours and its own target.
  - Review Focus 5, `Bulk_OneRecordFailsValidation_StartsNothing`: one selected record out of the caller's reach → no run row, the notified error names that record's label.
  - `AggregateViewGuardTests.Refuses_TaskArgumentNoFormCanBuild` (a `NotShown` argument with no default) and `Refuses_PageModeTaskWithoutPageRoute`, each naming the task.
- [ ] **Step 2: Run** `--filter "TaskArgumentFormTests|AggregateViewGuardTests"` → FAIL.
- [ ] **Step 3: Implement** the form view, dialog, page/inline wiring and the guard changes. The form follows the `SerginFormView` shape (MudForm, `onsubmit` EventCallback, `SuppressImplicitSubmission="false"`, `ValidateAsync` before submit).
- [ ] **Step 4: Run** → PASS; `PseudoLocaleSweepTests` passes with the probe routes added there.
- [ ] **Step 5: Commit**: `Task argument forms in dialog, page and inline modes`.

---

### Task 11: Schedule dialog with arguments

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Tasks/SerginScheduleDialog.razor{,.cs}`, `SerginScheduleRequest.cs` (argument form from the task's generated fields; header names "N records" or the scope), `SerginTaskSchedulesSection.razor.cs` (Edit reopens the dialog with stored arguments and sends them as `UpdateTaskScheduleCommand.Template`)
- Test: `tests/.../Tasks/TaskUiTests.cs`, `tests/.../Tasks/TaskSchedulesSectionRefreshTests.cs`

**Interfaces:**
- Consumes: Task 10's generated argument fields; `UpdateTaskScheduleCommand(string Schema, TaskScheduleId Id, ITaskCommand Template, IReadOnlyCollection<TaskScheduleTarget> Targets, string Cron, string TimeZoneId)`.

- [ ] **Step 1: Write the failing tests**
  - `ScheduleDialog_ShowsArgumentsAboveCron_AndStoresThem`: save → `task_schedules.arguments` holds Note/Hours; a cron fire (fake engine) runs items with them.
  - `ScheduleDialog_Header_NamesRecordsOrScope`: "· 3 records" for an entity task, "· North Region" for a collection task.
  - `EditSchedule_ChangesArguments`: edit Hours 4 → 8; the stored arguments change and the row version moves.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `--filter FullyQualifiedName~Tasks` → PASS.
- [ ] **Step 5: Commit**: `Schedule dialog edits task arguments`.

---

### Task 12: Docs, seeds and the end-to-end walk

**Files:**
- Modify: `.claude/CLAUDE.md` (Scheduled tasks bullet → Tasks; pipeline-behavior list item 1; test-folder paragraph), `src/SharedKernel/.claude/CLAUDE.md` ("Scheduled tasks" section rewritten as "Tasks"), `src/Modules/DeviceManagement/CLAUDE.md`, `README.md`, `.claude/skills/add-feature/SKILL.md` (a "task command" variant: type, configuration, `ITaskProgress`, view placement and modes)
- Verify: `docker-compose/.files/seed/*.sql` keys (from Task 1)

- [ ] **Step 1: Rewrite the docs** to the final names and rules. Remove every mention of `Schedulable(name)`, `Background(timeout)`, `BackgroundCommandGuardBehavior`, `IRunProgress`.
- [ ] **Step 2: Full verification**

Run: `dotnet build Sergin.MeterMinder.slnx` → 0 warnings.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All` and `dotnet test tests/Sergin.Administration.UserAccess.UnitTests` → all pass (licence-gated TickerQ tests skipped).
Run: `rg -n "ScheduledTask|Schedulable\(\"|Background\(|IRunProgress|scheduled-tasks" src tests docker-compose .claude --glob '!**/Migrations/**'` → no hits outside migration history and the old specs.

- [ ] **Step 3: Headless-browser walk** (Playwright-Python against the host on :5555, per the memory note): on the Device pages, start Read profile, bulk start on three rows, cancel a running one, schedule daily, Run now, edit the schedule, delete it. Each step's visible result matches the wireframes.
- [ ] **Step 4: Commit** (submodule, then host with the pointer bump): `Docs and walk-through for task commands`.
