# Scheduled commands — design

Date: 2026-11-10. Status: approved.

## Intent

DeviceManagement is a Head-End System. An HES has to read meters without a person present: read a device's
load profile every night, read one parameter (a register) from a set of devices every hour, or run the same
read right now from a button. Meter communication does not exist yet, but the scheduling and execution
plumbing it needs is platform work, and it should exist before the first read command is written.

This design adds **scheduled commands** to the platform. An operator takes a command that a module has
marked as schedulable, picks its targets (one device or a fixed set of picked devices), and either runs it now
or gives it a schedule. At each run the platform sends that command once per target through the normal MediatR
pipeline. Permission, version and validation behaviors apply exactly as they do for a button click.

There is no new "job" concept for module authors to learn. A job is a command that someone is allowed to run
later. A module gets a new background capability by writing an ordinary command slice and opting it in.

The engine that fires the schedules and retries is third-party (**TickerQ**, the user's choice). SharedKernel
defines the abstractions. A separate adapter assembly holds the TickerQ implementation, so no module and no
SharedKernel contract project references TickerQ.

## Goals

- A command or query becomes schedulable only by opting in through its `ICommandConfiguration`
  (`builder.Schedulable()`). This works for aggregate commands and for child-entity commands.
- An operator creates a **scheduled task**: a schedulable command, its arguments, one or more targets, and
  either a cron schedule or nothing (a run-now-only task). They can run it now, pause it, edit it and delete it.
- Each run sends the command **once per target**. Each target succeeds, fails and retries on its own; one dead
  meter does not fail the other 99.
- **Delegation.** A run executes with no session, on behalf of the user who created the task, in the scope the
  task was created in. That user's **current** grants are checked on **every run**. If the user is revoked or
  deactivated, their task's runs fail with Forbidden.
- Run history per task and per target: when, outcome, error.
- **Per-module opt-in**, the same way the outbox works. A module that does not opt in gets no tables, no
  pages and no cost.
- SharedKernel holds the contracts; the TickerQ adapter lives in its own assembly; the host chooses it in
  `Program.cs`.
- Generic UI parts in SharedKernel's Blazor library: a "Schedule…" action and dialog, and a section that lists
  a record's tasks and recent runs.
- Proven end to end by **test-only** schedulable commands. No real module command is made schedulable in this
  work.

## Non-goals

- Meter communication, and any real `ReadDeviceProfile` or `ReadDeviceParameter` command. Those come later as
  ordinary DeviceManagement slices that call `Schedulable()`.
- Dynamic targets: a scope subtree, a saved list filter, or a named device group. Targets are a **fixed set of
  picked records** for now. A record joins a task only when someone edits the task.
- A central "all scheduled tasks" administration page. Operators start from the record (see "UI").
- Multi-instance deployment. One host runs today. The design must not make it impossible (see "Engine"), but
  this work does not prove it.
- Schedulable version-guarded commands (see "Rules").
- Exposing scheduling over WebApi or gRPC. Those endpoints can follow later.

## Anti-goals

These would make the work a failure even if it ran:

- **A run that is more powerful than its creator.** Delegation must never turn into "the system can do
  anything". The run identity must not be an `ISystemUserContext`, because that type passes every permission
  check without a lookup (see `ISystemUserContext`, `RelayUserContext`).
- **A second pipeline.** A scheduled send must not skip `PermissionCheckPipelineBehavior`,
  `ExpectedVersionPipelineBehavior` or `ValidationPipelineBehavior`, and must not call a handler directly.
- **TickerQ leaking.** No TickerQ type in any module project, in `Sergin.SharedKernel.Application`, or in a
  `.Presentation.Blazor` project.
- **Scheduling anything.** A command that did not opt in can never be scheduled, by any path.
- **One target's failure failing the run**, or one run blocking the next forever.

## Decisions made with the user

1. **Operators define schedules at runtime** in the UI. Schedules are stored in the database and can be edited
   without a deploy.
2. **The unit of scheduling is a command** that is configured as schedulable. This covers aggregate and
   child-entity commands.
3. **Targets are one record or a fixed set of picked records.** A group is often more than 100 devices.
4. **Engine: TickerQ**, behind SharedKernel abstractions, in a separate implementation assembly.
5. **Identity: a system identity acting on behalf of the creating user (delegation).**
6. **The creator's grants are checked on every run**, not only when the task is created.
7. **Operators start from the record**: the device detail page, or rows selected on the device list.
8. **First piece ships the plumbing and the generic UI parts, proven by test-only commands.**
9. **Every run acts as a person who chose it.** A scheduled run acts on behalf of the user who last saved the
   task (creator at first, then each editor in turn). A "Run now" acts on behalf of the user who clicked it.
10. **"Run now" without "Save" is a one-off run.** It leaves no stored task behind; it appears only in run
    history.

## Concepts

- **Schedulable command**: a request record whose `ICommandConfiguration` calls `builder.Schedulable("<stable name>")`.
- **Scheduled task**: the stored definition. It holds the command type, the argument template, the targets,
  the trigger (a cron and time zone), the creator, the **delegating user** (whoever last saved it), the scope,
  and a state (active or paused). It is owned by the module the command belongs to. A stored task always has a
  schedule; there is no stored run-now-only task.
- **Run**: one send of a command to its targets. Three kinds:
  - *scheduled*: fired by a task's cron, acting on behalf of the task's delegating user;
  - *run now of a task*: fired from a task's row command, acting on behalf of the user who clicked;
  - *one-off*: fired from the dialog's "Run now" with no save. It has **no task**. The run itself stores the
    command type, arguments, targets, scope and the user who clicked.

  Every run records who it acts for and what triggered it. A run has one **run item** per target.
- **Run item**: one send of the command for one target, with attempts, outcome and error.

## Rules

### Which commands may be schedulable

- Only a configured request can call `Schedulable()`. An unconfigured request is never schedulable.
- The request must have a type from the entity command types work (`Sergin.SharedKernel.Application.Commands.Types`)
  that names one target per send: an `IEntityTarget<TRoot, TId>` (`Id`) or an `IChildEntityTarget`
  (`RootId`, `Id`). Targets are substituted into exactly those properties.
- A multi-target task requires the command to be `ISelectable`, which already means "may run once per
  selected row". A non-selectable schedulable command can be scheduled for one target only.
- **A command whose type requires an expected version is refused at host start** if it calls `Schedulable()`,
  unless its configuration also calls `WithoutExpectedVersion()`. A version stored when the task is created is
  stale by the next run, so the run would always be refused with 412. Read-style commands are the target use.
  The refusal names the type, the same way a dead `WithoutExpectedVersion()` is refused today.
- `Schedulable()` on a creation command (`ICreateCommand`), a collection target, or a `ISystemCommand` is
  refused at host start. They have no per-target `Id` to substitute.
- Queries can be schedulable too (a read whose result is only stored in run history). Whether a query's
  response is kept is left to the builder (see "Left to the builder").

### Delegation and authorization

- Each run item executes in a fresh DI scope, seeded the way `ScopedSerginDispatcher` seeds a Blazor send:
  - `UserContextAccessor.Current` is set to a **delegated user context**. It carries the id and names of the
    user the run acts for: the task's delegating user for a scheduled run, the clicking user for any "Run now". It is **not** an `ISystemUserContext`. The permission checker therefore looks up that user's stored
    grants exactly as it does for a signed-in request.
  - `IAuthorizationScopeContext.Set(task.ScopeId)` is called with the scope the task was created in.
- The delegated context also exposes that it is a scheduled run (a marker interface, for example
  `IDelegatedUserContext : IUserContext` with the task id). Run history and logs use it. Audit stamps keep the
  user's id in `created_by`/`modified_by` (that is the delegated authority); the run history is where the
  scheduler shows up.
- **Saving a task makes the saver its delegating user.** Editing re-checks the editor's permissions, not the
  previous delegating user's. So a second operator can never keep work running under the authority of someone
  who has lost it: the moment they save, the task runs under their own grants.
- Because grants are checked live, a revoked grant, a deactivated user or an expired assignment fails the run
  item with Forbidden on the next run. The task is not paused automatically: it keeps firing and keeps
  recording Forbidden, so an operator sees why. *(My call; see "Calls I made".)*
- **Creating, editing or running a task now** requires the caller to hold, on the current scope:
  - the command's own required permissions (checked at creation as an early refusal; checked again at every run
    as the real guarantee), and
  - a scheduling permission that the module registers, for example `permission.dm.scheduled-tasks.manage`.
    *(My call.)*
- A task is visible only in its own scope's reach. The task table is scoped like `Device` (the `Scoped()`
  feature), so the scope picker and query filter apply.

### Run behavior

- **Overlap.** If a task's previous run still has unfinished run items when the next trigger fires, the new run
  is recorded as **skipped** and sends nothing. *(My call.)*
- **Retry per run item.** A run item that fails with an unexpected exception or a transient error is retried
  with backoff, up to a configured maximum, then marked failed. A run item that fails with an `ErrorOr` error
  of type Validation, NotFound or Forbidden is **not** retried, because retrying cannot change it. *(My call.)*
- **A target that disappears.** A picked record that was deleted, or that moved out of the creator's reach,
  fails as NotFound in that run. The other targets still run.
- **Run now of a task** creates a run immediately, outside the schedule, and follows the same overlap and retry
  rules. A **one-off run** has no task, so the overlap rule does not apply to it; retry does.
- **Time zone.** A cron is interpreted in a time zone stored on the task. It defaults to the operator's browser
  zone (`IUiTimeZoneStore`) when the task is created, and falls back to `Sergin:TimeZone`. *(My call.)*
- **Retention.** Runs and run items older than a configured period are purged, the same way outbox rows are.

## Architecture

### Where things live

| Piece | Project |
|---|---|
| `Schedulable()` on `CommandConfigurationBuilder<TCommand>`, the registry flag, the host-start refusals | `Sergin.SharedKernel.Application` (`Commands/Configuration`) and `Sergin.SharedKernel.Hosts` (guard) |
| Scheduled task / run / run item model, the commands that manage tasks, the per-target runner, the delegated user context | SharedKernel, split across `.Domain` / `.Application` / `.Application.Contracts`-equivalent folders the way the outbox is split today |
| Engine abstraction (`IJobScheduler`, or a better name): register, update, remove and trigger a task's trigger; enqueue a run item | `Sergin.SharedKernel.Application` |
| EF mapping: `modelBuilder.ApplyScheduledTasks()` and the `IScheduledTasksDbContext` opt-in | `Sergin.SharedKernel.Infrastructure.Data.EFCore` |
| TickerQ adapter: the `IJobScheduler` implementation, TickerQ's own store, the TickerQ function(s) that call back into the runner | **new** `Sergin.SharedKernel.Infrastructure.Jobs.TickerQ` (name left to the builder) |
| "Schedule…" action and dialog, task/run section | `Sergin.SharedKernel.Presentation.Blazor` |
| Test-only schedulable commands and their aggregate | `tests/Sergin.MeterMinder.IntegrationTests.All` |

TickerQ package versions go into `Directory.Packages.props` (Central Package Management), referenced
version-less, alphabetical.

### Per-module opt-in

This follows the outbox exactly. A module's `DbContext` implements `IScheduledTasksDbContext` and calls
`modelBuilder.ApplyScheduledTasks()`. That puts the tables in the module's own schema (for example
`dm.scheduled_tasks`, `dm.scheduled_task_targets`, `dm.scheduled_task_runs`, `dm.scheduled_task_run_items`)
and `AddModuleDbContext` then registers that module's runner and store. The tables arrive in the module's
migration, written in the same PR as the opt-in.

**In this work, no real module opts in, and the real host is not wired.** The test project opts in a test-only
context and registers the TickerQ adapter in its own fixture, the same way the outbox tests prove the relay with
`OutboxTestHost`. `Sergin.MeterMinder.Hosts.All`'s `Program.cs`, the Compose database and the migration story
for TickerQ's own tables (today only `ISerginModule.MigrateAsync` runs, and only in Development) are left
untouched. All three come with DeviceManagement's first real schedulable command. *(My call.)*

### Storage (agreed with the user)

Four tables in the opting-in module's schema (`dm` shown). Column names are the agreed shape; the builder may
adjust names, not meaning.

**`scheduled_tasks`**: one row per saved task. An aggregate root configured `Audited().SoftDeletable().Versioned().Scoped()`,
so edit, pause and delete are version-checked like any other aggregate, and a deleted task keeps its run history.

| column | type | note |
|---|---|---|
| `id` | uuid | `Guid.CreateVersion7()` |
| `command_type` | text | the command's **stable declared name** (see below), never a CLR type name |
| `arguments` | jsonb | the non-target arguments, e.g. `{"ProfileKind":{"Value":"daily"}}` |
| `cron` | text | e.g. `0 2 * * *` |
| `time_zone` | text | IANA id, e.g. `Asia/Tehran` |
| `state` | text | `active` / `paused` |
| `delegating_user_id` | uuid | the last saver; scheduled runs act for this user |
| `engine_trigger_id` | uuid | the TickerQ cron ticker id; the only link to TickerQ |
| `scope_id`, `scope_path` | uuid, ltree | from `Scoped()` |
| `created_*`, `modified_*`, `deleted_*` | | from `Audited()` and `SoftDeletable()` |
| `row_version` | uuid | from `Versioned()` |

**`scheduled_task_targets`**: one row per picked record. `task_id` (FK, cascade), `target_id` uuid, `root_id` uuid
null (child-entity commands only). Primary key `(task_id, target_id)`. Index on `target_id`, so a record's page
finds the tasks that target it.

**`scheduled_task_runs`**: one row per run. `id`; `task_id` nullable FK (null for a one-off run); `trigger`
(`scheduled` / `run_now` / `one_off`); `acting_user_id`; `scope_id`, `scope_path`; **`command_type` and
`arguments` copied onto every run**, so a one-off run needs no task and history stays true after a task is
edited; `status` (`running` / `completed` / `skipped`); `started_at_utc`, `completed_at_utc`; `total`,
`succeeded`, `failed`. Index on `(task_id, started_at_utc desc)`.

**`scheduled_task_run_items`**: one row per target per run. `id`, `run_id` (FK, cascade), `target_id`,
`root_id`, `status` (`pending` / `running` / `retrying` / `succeeded` / `failed`), `attempts`, `error_code`,
`error_description`, `started_at_utc`, `completed_at_utc`, `engine_job_id`.

Runs and run items older than `Sergin:ScheduledTasks:Retention` are purged, like outbox rows.

**Stable command name.** `Schedulable` takes a name: `builder.Schedulable("dm.devices.read-profile")`, shaped like
an `[IntegrationEventName]` wire name, so renaming or moving the class does not break stored tasks. Host start
refuses a duplicate name, a malformed one, or one outside the module's schema. A stored name no command declares
any more fails that task's run items with a clear error.

**Target ids are `uuid`**, because every typed id today wraps a `Guid`. A future text-keyed root would need a
change here.

**TickerQ's tables** hold only a cron ticker per active task (payload `{schema, taskId}`) and a one-off ticker per
run item. They never hold arguments, users or targets, and can be rebuilt from `scheduled_tasks`.

### Sergin owns the definition; TickerQ owns the firing

The scheduled task row in the module's schema is the source of truth: command, arguments, targets, creator,
scope, cron, state. TickerQ holds only what it needs to fire: a cron ticker per active task, whose payload is
`(module schema, task id)`, and a one-off ticker per run item for retries.

When a task is created, edited, paused or deleted, the handler writes the Sergin row and calls the engine
abstraction in the same request. If the engine call fails, the request fails and nothing is saved. *(My call.
The builder may instead reconcile triggers from the Sergin rows at start, which also heals drift. Either is
acceptable if a task can never fire with no row, or have a row and silently never fire.)*

### One run, step by step

1. TickerQ fires the task's cron ticker. Its function resolves the runner for the module schema in the payload.
2. The runner loads the task. If the task is paused or deleted, nothing happens. If a previous run is unfinished,
   it records a skipped run.
3. It records a run with one run item per target, then asks the engine to execute each run item (one engine job
   per run item, so retries and parallelism are the engine's).
4. For each run item, in a fresh scope: seed the delegated user and the scope, build the command from the
   argument template with the target's id put into `Id` (and `RootId` for a child), then `ISender.Send` it.
5. Record the outcome: succeeded, failed (with the error code and description), or retrying.
6. When the last run item finishes, the run is complete with counts.

### Argument template

The task stores the command's non-target arguments as JSON, the same way the outbox stores integration events.
Commands carry domain value objects (`DeviceId(string)` and so on), so the serializer must round-trip them, and
a test must prove that it does for every shape the test-only commands use. When a command's shape changes after
tasks were stored, a task whose arguments no longer deserialize fails its run items with a clear error. It is
not silently dropped.

### Engine

TickerQ is source-generated, persists through EF Core on PostgreSQL, supports cron tickers created at runtime,
keeps run history, and ships a dashboard. The adapter registers TickerQ's own tables in a schema of its own,
separate from every module. The TickerQ dashboard is **not** exposed by default. If it is turned on, it is
Development-only behind the host's authentication. The Sergin UI is the operator's view.

**Version and licence (decided with the user, 2026-11-10).** TickerQ **10.5.1** (the .NET 10 line), on the free
Community licence. The 5.x line is source-available, not MIT, and a missing or invalid licence certificate blocks
execution. So the licence path is configuration (`Sergin:ScheduledTasks:TickerQ:LicensePath`); the file is never
committed; host start fails naming that key when the file is missing; and the tests that run real TickerQ skip
with a reason when it is absent. Eligibility under the Community terms is to be checked before the first customer
deployment. Because TickerQ holds only triggers, which are rebuilt from `scheduled_tasks` at start, a later move to
another engine (Quartz.NET was the alternative considered) is a new adapter with no data migration.

**TickerQ is the user's choice and is fixed.** If its current documentation shows a blocker (no .NET 10 or
EF Core 10 support, no separate schema, no runtime cron tickers, no usable retry), the builder stops and asks
the user. The builder does not swap engines silently. *(My call.)*

Multi-instance: TickerQ's locking behaviour on PostgreSQL with more than one host process was not confirmed
during design. The builder must check the TickerQ documentation for the version chosen and record the answer in
the plan. This work does not need it, but the design must not depend on a single process in a way that would
double-fire later.

Tuning lives in `Sergin:ScheduledTasks` (max attempts, backoff, run-item parallelism, retention), validated at
start with an `IValidateOptions<T>` that names the bad key, like `Sergin:Outbox`.

## UI

The generic parts live in `Sergin.SharedKernel.Presentation.Blazor` and follow the existing view and page-type
conventions: `TextDefinition` labels only, `.razor` markup with all C# in `.razor.cs`, `ISerginDispatcher` for
every send, MudBlazor, `IUiErrorPresenter` for errors.

- **"Schedule…" action.** A view adds it next to a schedulable command, the same way it adds a
  `SerginPageAction` today. On a list it is a bulk action over the selected rows (`SerginBulkAction<TItem>`)
  for an `ISelectable` command. The action is hidden when the user lacks the scheduling permission or the
  command's permissions, using the existing permission hiding.
- **Schedule dialog.** It shows the command's name, its non-target arguments (the form for those is passed in
  by the module, because generating forms from command types is a separate design), the targets (read-only, from
  the record or the selection), and a trigger: "Run now only", or a schedule. The schedule is entered as a small
  set of common choices (daily at a time, hourly, weekly on a day at a time) with a cron field for anything else,
  shown with the time zone and the next few fire times. Buttons are "Run now" (a one-off run, nothing saved)
  and "Save" (stores the task with its schedule). *(Layout is my call; a
  mockup comes at plan time.)*
- **Scheduled tasks section.** An embedded section for a record's detail view (a `SerginEntityListView`-shaped
  outlined section) listing tasks that target this record: command, schedule, next run, last outcome, state.
  Row commands: Run now, Pause/Resume, Edit, Delete. Opening a task shows its recent runs and, per run, its run
  items with their errors. Below the tasks, a short list of recent **one-off runs** that targeted this record,
  with who ran them and the outcome.
- Texts are declared in the SharedKernel catalog under `common.scheduled-tasks.*`, and the pseudo-locale sweep
  covers the new parts.

No real module page uses these parts in this work. A test-only Blazor page or a rendering test (`bunit`-style
or the existing prerender tests) proves them. *(The method is left to the builder, within the existing test
project.)*

## Proof

Integration tests in `tests/Sergin.MeterMinder.IntegrationTests.All`, under a new `ScheduledTasks/` folder,
against a test-only aggregate and test-only schedulable commands, with Testcontainers PostgreSQL:

- `Schedulable()` and the host-start refusals: a version-guarded command, a creation command, a non-selectable
  command with several targets, an unconfigured command.
- A task created, run now, and its run items succeeded per target. The command went through the pipeline,
  proven by a validator or permission rule on the test command that actually fires.
- Delegation: a scheduled run sees the delegating user's id and the task's scope; after a second user saves the
  task, the next scheduled run acts for that second user; a "Run now" acts for the clicker; a one-off run leaves
  no task row. A creator whose grant is revoked between two
  runs gets Forbidden on the second. A deactivated creator gets Forbidden. A system identity is never used.
- One target out of reach or deleted fails as NotFound, and the others succeed.
- Retry: a transient failure is retried and then succeeds; an `ErrorOr` Validation, NotFound or Forbidden error
  is not retried; max attempts gives failed.
- Overlap: a trigger while the previous run is unfinished records skipped.
- Arguments round-trip through JSON for value-object arguments.
- Engine seam: a fake `IJobScheduler` proves the Sergin side without TickerQ, and at least one test runs the
  real TickerQ adapter end to end (a cron or a run now firing through TickerQ).
- Opt-in: a module context that does not opt in has no scheduled-task tables and registers no runner.
- UI parts render, prerender correctly, and pass the pseudo-locale sweep.

Waiting for background work in tests uses polling with a timeout, not fixed sleeps.

## Calls I made (for the user to check)

- `Schedulable()` is refused on version-guarded commands unless they waive the version.
- Multi-target tasks need `ISelectable`.
- A revoked creator's task is **not** auto-paused. It keeps recording Forbidden.
- A module scheduling permission (`permission.<schema>.scheduled-tasks.manage`) is needed in addition to the
  command's own permissions.
- Overlapping runs are skipped, not queued.
- `ErrorOr` Validation, NotFound and Forbidden are not retried.
- The cron time zone is stored per task and defaults to the operator's browser zone.
- Tables live in the opting-in module's schema; TickerQ's tables live in a schema of their own.
- The TickerQ dashboard is off by default.
- TickerQ is wired in the test project only; the real host, Compose and TickerQ's migrations wait for the first
  real command.
- A TickerQ blocker stops the builder to ask; no silent engine swap.
- Audit stamps carry the acting user's id; the scheduler identity shows in run history, not in audit columns.

## Left to the builder

- Exact type, table and project names, including the adapter project and the engine abstraction.
- How the generic task-management commands are typed and routed to the right module's store, as long as they
  pass the existing guards (`CommandTypeGuard`, `CommandTargetMappingGuard`, `FieldTextGuard`) and are permission
  checked like any other command.
- Whether triggers are written synchronously with the task row or reconciled from the rows at start (see
  "Sergin owns the definition").
- Whether a schedulable query's response is stored in run history (and how much of it), or only its outcome.
- Run-item parallelism defaults and backoff numbers.
- The TickerQ version, after checking its current documentation (EF Core provider, runtime cron tickers, retry
  options, multi-node locking on PostgreSQL).
- The UI test method.
