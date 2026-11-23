# Task commands: one concept for scheduled and background work

Date: 2026-11-20. Status: draft for approval.
Replaces the authoring surface of `2026-11-10-scheduled-commands-design.md` and `2026-11-18-background-commands-design.md`; their run mechanics stay.

## Why

Today, work that runs outside a request is two separate opt-ins with rules between them. `Schedulable(name)` can go on any eligible command. `Background(timeout)` needs `Schedulable(name)` too, and it only works on an `ITransitionCommand`. A version-guarded command needs `WithoutExpectedVersion()` before it can be scheduled. The schedule name is a literal the author has to invent. Because of these rules, `ReadDeviceProfileCommand` claims to be a transition "though it changes no field". **Writing a slice against this split is confusing.** That is the whole reason for this change. Nothing here is in production, so the surface can change freely.

The target: an author writes one command type and one configuration block, and the framework derives the rest.

## Goals

- One concept, the **task**. A task command is its own command type and always runs as a tracked run, never inline.
- Task traits are declared in the command's existing `ICommandConfiguration`, in one `Task(...)` block: inactivity timeout, optional hard cap, cancelable (on by default), schedulable.
- Commands that are not tasks can never run in the background or on a schedule. The type system enforces this where it can, and host start and the pipeline enforce the rest.
- A handler may report progress. Every report resets the inactivity timeout.
- The vocabulary is clean: "task" means the command, "schedule" means when it runs, "run" means one execution.

## Non-goals

- Criteria tasks, where a run picks its own records from a filter at run time. A collection task can read a filter argument itself later; no framework shape is added for it now.
- New engines. `IScheduledTaskEngine` (renamed, below), the TickerQ adapter and the in-process stand-in stay as they are.
- Changes to the run mechanics already proven by tests: claim-before-send, the overlap index, retries, heartbeat, cancel, the recovery sweep, purge, trigger rebuild, the activity indicator and its notifier.
- Per-task retry policy. The global `MaxAttempts`/`RetryIntervalsSeconds` rule stays.

## Anti-goals

- A façade that maps the new surface onto `Schedulable`/`Background`. The confusing split would survive inside.
- Two meanings of "task". The current `ScheduledTask` entity (which is a schedule) is renamed.
- Re-earning fixed edge cases by rewriting the executor. The executor is reshaped, not rewritten.

## Approaches considered

- **A. Reshape the surface, keep the run mechanics.** New types, configuration, names, namespace and tables; the executor, store, engines and hosted services are kept and renamed. **Chosen**: the confusion is in the authoring surface and the vocabulary, not the run mechanics.
- **B. Clean rewrite.** Delete the `ScheduledTasks` tree and build it again. Cleanest code, but it re-earns about 40 test files of edge cases (overlap, stale sweep, cancel race, heartbeat, culture on notify). Rejected as large and risky.
- **C. Façade over the current code.** Small, but the split survives inside and a reader still meets it. Rejected because it works against the reason for the change.

## Decisions (made with the user)

1. **Two task shapes** (option 2 of three; entity-only was too narrow, criteria tasks are a non-goal):
   - `ITaskCommand<TRoot, TId>`: targets one entity and carries `Id`. With `ISelectable` it may run on several picked records, one run item each. This replaces `ITransitionCommand` on `ReadDeviceProfileCommand`.
   - `ITaskCommand<TRoot>`: no record target, one run item per run, for whole-collection jobs (for example a nightly fleet job).
2. **A task always runs in the background.** It is never handled inline.
3. **Task traits are declared in `ICommandConfiguration` through one `Task(...)` block.**
4. **Timeout = inactivity + optional hard cap.** The inactivity timeout is reset by every progress report. `MaxDuration` is optional, off by default, and nothing resets it.
5. **The task name is derived by convention**, pinned by a test, with `Name(...)` as an override that keeps an old name after a rename.
6. **Cancelable is on by default**; `.NotCancelable()` opts out.
7. **Approach A.**
8. **Child-entity tasks are dropped.** A task targets a root entity or the root's collection, never a child. The `RootId` path (`BackgroundTarget.RootId`, the targets' and run items' `root_id`, the child branch of the command factory) is deleted.
9. **A task may carry arguments, the same way any other command does.** The arguments are the command's own properties beyond its target id. The view configuration decides how its form is rendered: a button and a modal form, a button and a page, or a form section inside the host page.
10. **A collection task has at most one running run per task name and scope.** A second start is refused. Entity tasks keep today's rule (one running run per saved schedule).

## Design

### Command types (`Sergin.SharedKernel.Application.Commands.Types`)

```csharp
// One record per run item; the run substitutes Id per picked record.
public interface ITaskCommand<TRoot, TId> : ICommand, IEntityTarget<TRoot, TId>, ITaskCommand
    where TRoot : IAggregateRoot;

// No record: one item per run. The root gives the texts, the permissions' scope rule and the UI placement.
public interface ITaskCommand<TRoot> : ICommand, ICollectionTarget<TRoot>, ITaskCommand
    where TRoot : IAggregateRoot;

// Non-generic marker, for the guards and generic constraints. Not implemented directly.
public interface ITaskCommand : IBaseCommand;
```

- A task command answers `ErrorOr<Success>` (`ICommand`, no response). Its outcome is the run item's status; there is no response to return to a caller.
- `CommandTypeGuard` refuses at host start: a task that also carries another effect interface, a type that implements the non-generic `ITaskCommand` without one of the two generic shapes, `ISelectable` on `ITaskCommand<TRoot>`, and a task that also implements `IChildEntityTarget` or `IRootTarget` (child tasks are dropped).
- **Arguments**: every property beyond the target id is an argument. Arguments follow the rules of any other command: each shown one needs a `FieldText` in the root's category (`FieldTextGuard`), `NotShown` hides one, and the task's validator covers them. A run stores the arguments once (the existing jsonb `arguments` on the run and on the schedule) and substitutes only the target id per item.
- **Expected version: a task never takes one.** A run executes later than the page read the record, so a version would be stale. `CommandTypes` derives "no version required" for every task type. This removes the `WithoutExpectedVersion()` rule for scheduling. The task's handler still saves under EF's concurrency token as today.
- **Permission scope** follows the existing typed-record rule: a `Scoped()` root is checked on the run's scope; an unscoped root passes a holder on any scope. For `ITaskCommand<TRoot>` on a `Scoped()` root, the check is on the run's scope (the caller's current scope at start), and the handler's reads see that subtree through the scope filter.

`ReadDeviceProfileCommand` becomes:

```csharp
public sealed record ReadDeviceProfileCommand(DeviceIntenralId Id)
    : ITaskCommand<Device, DeviceIntenralId>, ISelectable;
```

### Configuration (`CommandConfigurationBuilder<TCommand>`)

```csharp
internal sealed class ReadDeviceProfileCommandConfiguration : ICommandConfiguration<ReadDeviceProfileCommand>
{
    public void Configure(CommandConfigurationBuilder<ReadDeviceProfileCommand> builder) =>
        builder.RequirePermissions(DevicePermissions.ReadProfile)
            .Task(task => task
                .InactivityTimeout(TimeSpan.FromMinutes(2))
                .Schedulable());
}
```

`Task(Action<TaskBuilder>)` exists only where `TCommand : ITaskCommand` (an extension method constrained on the command type, so calling it on a non-task does not compile). `TaskBuilder` members:

| Member | Default | Meaning |
|---|---|---|
| `InactivityTimeout(TimeSpan)` | 5 minutes | Longest silence allowed: from the item's start to its first report, and between two reports. Must be in (0, 1 hour]. |
| `MaxDuration(TimeSpan)` | none | Hard cap on one attempt's wall time. Nothing resets it. Must be above zero and at least the inactivity timeout. |
| `NotCancelable()` | cancelable | The run cannot be cancelled by a person. Use it where a half-done run would harm the data. |
| `Schedulable()` | not schedulable | The task may be saved on a cron or as a run-once at a time. Without it, a task can only be started now. |
| `Name(string)` | derived | Overrides the derived name. Use it only to keep an old name after a rename. |

A task with no `Task(...)` call, and a task with no configuration at all, gets every default. The old `Schedulable(name)` and `Background(timeout)` builder methods are deleted, and so is `BackgroundSettings`. `CommandSettings` carries one `TaskSettings?` (null for every non-task request): `Name`, `InactivityTimeout`, `MaxDuration`, `Cancelable`, `Schedulable`.

### Task names

- Derived as `<schema>.<root type name, kebab>.<command type name minus "Command", kebab>`, for example `dm.device.read-device-profile`. The root type name is used as is, not pluralized, so no pluralization rule can drift.
- The name is stored on every schedule and every run. It is how a run rebuilds its command, and how the activity panel labels a run when no view names the command.
- A pin test lists the exact set of task names in `tests/Sergin.MeterMinder.IntegrationTests.All/Tasks/task-names.txt`, like `MigrationPinTests` and `catalog-keys.txt`. A renamed type changes its derived name and fails the test. The author then accepts the new name (when no saved schedule uses the old one) or adds `Name("<old>")`.
- Host start refuses a malformed name (`TaskNames.IsValid`, the current regex), a name outside the module's schema, and two tasks with one name.

### Running

- **Starting now**: `StartTaskCommand(ITaskCommand Template, IReadOnlyCollection<TaskTarget> Targets)`; the Blazor entry is `Dispatcher.StartTaskAsync<TTask>(TTask template, targets) where TTask : ITaskCommand`. A non-task cannot be passed: the constraint refuses it at compile time. For `ITaskCommand<TRoot>`, the targets are empty and the run gets one item. Everything else is as today's `StartBackgroundCommand`: the template's own permissions, its validator run per target before anything is written, at most `MaxTargets` targets.
- **The guard**: `TaskCommandGuardBehavior` (first pipeline behavior, replaces `BackgroundCommandGuardBehavior`) refuses any `ITaskCommand` sent outside a run item with `TaskErrors.MustRunAsTask`. It reads the request type, not configuration.
- **Scheduling**: the create/update/pause/resume/delete/run-now and run-once commands stay, renamed (below). They refuse a task that is not `Schedulable()` with `TaskErrors.NotSchedulable`. A non-task cannot reach them: their template is typed `ITaskCommand`.
- **Overlap**: a collection task's start (now, run-once or a cron fire) is refused with `TaskErrors.AlreadyRunning` (`Tasks.AlreadyRunning`, Conflict) while a run of the same task name is Running in the same scope. A cron fire that loses is recorded as a Skipped run, as an overlapping schedule is today. The guarantee is a partial unique index (below); the start's check before the insert is advisory.
- **Cancel**: `CancelTaskRunCommand` refuses a run whose task is `NotCancelable()` with `TaskErrors.NotCancelable`. The activity row and the schedule section hide the Cancel button for such a task. Host shutdown still propagates as today.

### Progress and timeouts

- The handler injects **`ITaskProgress`** (replaces `IRunProgress`; scoped, registered in every host, a no-op outside a run item):
  - `ReportAsync(int percent, CancellationToken)`: as today (clamped 0–100, first write at once, then at most one write per 2 s, last value kept on the finished item). **Every call resets the inactivity timer**, including the calls whose write the throttle holds back.
  - `KeepAliveAsync(CancellationToken)`: resets the inactivity timer without a percent, for a handler that cannot measure its progress. No write. (Builder's call how it is exposed; this is a proposal.)
- **Mechanism**: the executor owns one `CancellationTokenSource` for the inactivity timeout and calls `CancelAfter(InactivityTimeout)` at the start of the send; `ITaskProgress` reaches it through the scoped run-item context and calls `CancelAfter(InactivityTimeout)` again on each report, which re-arms the timer. `MaxDuration`, when set, is a second source armed once. The send's token links both with the cancel source and the engine's token.
- **Outcomes**: inactivity expiry fails the item as `Tasks.TimedOut` (the message names the inactivity timeout); a `MaxDuration` expiry as `Tasks.MaxDurationExceeded`. Both are final and never retried, as `TimedOut` is today. Cancel still beats both; a host shutdown is still not a timeout.
- **The executor heartbeat is unchanged** and separate: it proves the worker is alive to the recovery sweep and reads cancel requests. It never resets the inactivity timer, because a stuck handler's worker is still alive.

### Renames (vocabulary)

| Today | New |
|---|---|
| namespace `…ScheduledTasks` (all layers) | `…Tasks` |
| `ScheduledTask` (the schedule entity) | `TaskSchedule` |
| `ScheduledTaskRun`, `ScheduledTaskRunItem` | `TaskRun`, `TaskRunItem` |
| `ScheduledRunTrigger` `Scheduled`/`RunNow`/`OneOff`/`Background` | `TaskRunTrigger` `Schedule`/`ScheduleNow`/`Once`/`Manual` |
| `ScheduledCommandCatalog`, `ScheduledCommand` | `TaskCatalog`, `TaskDescriptor` |
| `StartBackgroundCommand`, `CancelBackgroundRunCommand` | `StartTaskCommand`, `CancelTaskRunCommand` |
| `Create/Update/Delete/Pause/Resume/RunNow ScheduledTaskCommand`, `RunCommandOnceCommand` | `…TaskScheduleCommand`, `RunTaskOnceCommand` |
| `IScheduledTaskEngine`, `IScheduledTaskStore`, `IScheduledTasksDbContext`, `ApplyScheduledTasks()` | `ITaskEngine`, `ITaskStore`, `ITasksDbContext`, `ApplyTasks()` |
| `ScheduledRunItemExecutor`, `ScheduledRunStarter`, `ScheduledTaskRunner` | `TaskRunItemExecutor`, `TaskRunStarter`, `TaskScheduleRunner` |
| `IRunProgress` | `ITaskProgress` |
| `ScheduledTaskErrors` (`ScheduledTasks.*` codes) | `TaskErrors` (`Tasks.*` codes) |
| `ScheduledTaskPermissions.Manage`, `permission.sys.scheduled-tasks.manage` | `TaskPermissions.Manage`, `permission.sys.tasks.manage` |
| `ScheduledTaskTexts`/`ScheduledTaskUiTexts`, `common.scheduled-tasks.*` | `TaskTexts`/`TaskUiTexts`, `common.tasks.*` |
| options `Sergin:ScheduledTasks` (+ `:TickerQ:LicensePath`), `MaxBackgroundTargets` | `Sergin:Tasks` (+ `:TickerQ:LicensePath`), `MaxTargets` |
| Blazor `SerginScheduledTasksSection`, `SerginScheduledRunsDialog` | `SerginTaskSchedulesSection`, `SerginTaskRunsDialog` |
| hosted services `ScheduledTask…Service`, `ScheduledRunItemRecoveryService` | `Task…Service`, `TaskRunItemRecoveryService` |
| project `Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ` | `Sergin.SharedKernel.Infrastructure.Tasks.TickerQ` |

The manage permission gates the schedule UI and cancelling another user's run, as today (the run's own user may always cancel it; a `Manage` holder on the run's scope may cancel anyone's). `NotCancelable()` refuses both, with no override (builder check; my call). Starting a task now needs only the task's own permissions, as today.

### Storage

- Tables: `task_schedules`, `task_schedule_targets`, `task_runs`, `task_run_items`, with the same columns, partial indexes and FKs as today, renamed (`ix_task_runs_schedule_id_running`, `ix_task_run_items_unfinished_last_activity`, …). `task_run_items.target_id` becomes nullable, for `ITaskCommand<TRoot>` items. The `root_id` columns are dropped (decision 8). The run's `command_name` column holds the task name. The runs table gains a `target_kind` column (`Entity`/`Collection`) and a second partial unique index, `ix_task_runs_collection_running` on `(command_name, scope_id) WHERE status = 'Running' AND target_kind = 'Collection'`, for decision 10.
- DeviceManagement adds one migration, `ReplaceScheduledTasksWithTasks`: it drops the four `scheduled_task*` tables and creates the four `task_*` tables. The four existing migrations stay in history, so a dev database needs no reset. Saved dev schedules and runs are lost, which is acceptable (nothing is in production). TickerQ's old triggers are removed by the trigger rebuild as unlinked after `Grace`.
- `ScopePathRewriteHandler` re-paths `task_runs` as it re-paths `scheduled_task_runs` today.

### UI

- Generated views place tasks by type, not by a `Background` flag:
  - `ITaskCommand<TRoot, TId>`: a detail and row action that starts the task (bulk when `ISelectable`), as Read profile does today. When `Schedulable()`, it also gets the Schedule action and the "View schedules" item, and the detail view gets the schedules section for that record.
  - `ITaskCommand<TRoot>`: a list-view header action that starts the task. When `Schedulable()`, the list view also gets a Schedule action and a "Schedules" header item that opens the schedules section in a dialog with no record (my call: the list page has no natural section slot, and a dialog keeps the list on top).
- **Argument forms** (decision 9). A task with no shown argument starts on click, as Read profile does today. A task with arguments renders its form in the mode its view configuration names, through the same `SerginEditMode` and the same generated form machinery as an edit command:
  - `view.Command<T>().Edit(SerginEditMode.Dialog)` (the default): a button opens a modal form; submit starts the task.
  - `SerginEditMode.Page`: a button navigates to a form page, routed and hosted the way a Page-mode edit is today; submit starts the task and leaves to the host page.
  - `SerginEditMode.Inline`: a form section inside the host page (the detail view for an entity task, the list view for a collection task), with a Start submit.
  The form is a `MudForm` validated field by field through `ISerginFormValidator.RulesFor`, like every generated form; the template is built from the form with the target id filled per item. Bulk start shows the form once and applies the same values to every selected record.
- **Schedule dialog**: when a schedulable task has arguments, the schedule dialog shows the same argument fields above the cron picker, whatever the start mode (my call: a schedule is saved in one step, so its arguments belong in its dialog). Edit of a saved schedule may change its arguments as well as time and zone (my call; today it keeps them).
- `AggregateViewGuard.EnsureSchedulableFromId` is removed. A replacement check refuses a task whose arguments have no shown field and no way to be built (for example a property marked `NotShown` with no default), naming the task.
- `CommandView.Background`/`ScheduleName` become one `CommandView.Task` (`TaskSettings?`). The busy spinner reads it.
- The activity panel and indicator are unchanged apart from renames. A collection-task row has no record link and names the run's scope instead of a record. A `NotCancelable()` task's Running row shows "Can't be cancelled" instead of a Cancel button. Two new failure texts: inactivity ("Timed out after {Timeout} without progress.") and the cap ("Stopped after the {MaxDuration} limit.").
- Wireframe details (approved 2026-11-20, https://claude.ai/artifact/Rufn1czFweezBeg89UiRhg):
  - The Schedule… menu on a task's split button appears only when the task is `Schedulable()`.
  - Page mode: the module adds a three-line routable page hosting the generated task form, as every generated page does. Its breadcrumb is the record (a link) then the task's label. Start leaves to the record and Cancel leaves without starting.
  - Inline mode: an outlined, collapsible section titled with the task's label, with no action-bar button. After Start it keeps the entered values, and it shows the spinner while this record's run is going.
  - Collection task: a split button in the list header, with a spinner while its run in the current scope is going. The `AlreadyRunning` refusal names the task and the scope.
  - The schedule dialog's header names the records count for an entity task and the scope for a collection task.

### Module changes

- DeviceManagement: `ReadDeviceProfileCommand` as above; its configuration as above (inactivity timeout 2 minutes replaces the 10-minute `Background` timeout, since it reports every second); `IRunProgress` becomes `ITaskProgress`; the `DbContext` implements `ITasksDbContext` and calls `ApplyTasks()`; the migration above; `DeviceViewConfiguration` updated. The derived name `dm.device.read-device-profile` replaces `dm.devices.read-profile`; since saved dev schedules are dropped by the migration, no `Name(...)` override is needed.
- Host: `Program.cs` calls the renamed `AddSerginTickerQ`/`UseSerginTickerQ`/`EnsureSerginTickerQStoreAsync` (names unchanged; they now live in the renamed project). `appsettings*.json`, user secrets and `docker-compose.yml` move to `Sergin:Tasks`. The README's "scheduled work" section is updated.
- Seeds: `dev-seed-users.sql` grants `permission.sys.tasks.manage`; `dev-seed.sql`'s `fa` rows move from `common.scheduled-tasks.*` to `common.tasks.*`.
- Docs: this repo's CLAUDE.md "Scheduled tasks" bullet, the pipeline-behavior list, and SharedKernel's CLAUDE.md "Scheduled tasks" section are rewritten for tasks. `/add-feature` gains a "task command" variant.

### Tests

- The `ScheduledTasks/` folder becomes `Tasks/`, renamed throughout; behavior tests keep their assertions.
- New tests:
  - The type guard's refusals (two effects, bare marker, `ISelectable` on a collection task).
  - `Task(...)` defaults and each builder refusal (timeout out of range, `MaxDuration` below the inactivity timeout, `Name` malformed or outside the schema, duplicate names).
  - The pin test over `task-names.txt`.
  - Inactivity: a handler that reports keeps running past one inactivity timeout; a handler silent for longer than it times out as `Tasks.TimedOut`; `KeepAliveAsync` resets it; `MaxDuration` stops a handler that keeps reporting, as `Tasks.MaxDurationExceeded`.
  - Throttled reports still reset the timer.
  - Cancel refused for `NotCancelable()`, and its Cancel button hidden.
  - Scheduling refused for a non-`Schedulable()` task.
  - A collection task started from a list view: one item, no target, permission on the run's scope.
  - Version: a task on a `Versioned()` root starts with no version and is not refused with 428.
  - A task that targets a child entity is refused at host start; the child-task probes (`tst.probes.touch-child`) are deleted.
  - Arguments: a task with arguments in each of the three modes (dialog, page, inline) renders its form, validates field by field, and starts with the entered values; bulk start applies one form to every item; the schedule dialog stores the arguments and a cron run uses them.
  - Overlap: a second start of a running collection task in the same scope is refused with `Tasks.AlreadyRunning`; the same task in another scope starts; a losing cron fire is a Skipped run; the index refuses a duplicate when the check is bypassed.
- `MigrationPinTests`' set of version-guarded commands loses nothing (Read profile was already waived).
- The headless-browser walk of the Device pages is repeated: start, bulk start, cancel, schedule, run now.

## Left to the builder

- Exact file split inside the renamed namespaces, and whether `TaskBuilder` is a nested class or its own file.
- Whether `KeepAliveAsync` is a separate method or `ReportAsync(int? percent)`.
- Whether the rename lands as one SharedKernel PR plus one host PR, or is staged across several commits on one branch (SharedKernel is a submodule, so its PR merges first and the host bumps the pointer).
- Message wording for the new errors and texts, as declared `TextDefinition`s with English defaults.
- Order of work, so the build stays green at each commit (`TreatWarningsAsErrors`).
