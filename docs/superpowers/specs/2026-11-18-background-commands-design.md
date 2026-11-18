# Background commands and the activity indicator — design

Date: 2026-11-18. Status: draft, for the user's review.

## Intent

Some commands are background work by nature. Reading a meter's load profile talks to a device over a slow link,
and once meter communication is real it can take well over 30 seconds. Today a person who clicks "Read profile"
runs the command through `ISerginDispatcher.SendAsync`, so the Blazor circuit waits for the whole read. A bulk read
over 50 devices waits 50 times in a row. Nothing tells the person what is still running once they leave the page.

This design adds **background commands**: a command that a module marks as background is never run inline. A click
starts a run and returns at once; the run executes through the scheduled-tasks machinery that already exists
(delegation, live grant checks, per-target items, retries, recovery, run history); and an **activity indicator** in
the app bar shows the signed-in user's runs across every module, live, with progress and Cancel.

There is no new job table and no new engine. A background run is a scheduled-task run with no task behind it, the
same shape `RunCommandOnceCommand` already writes (`ScheduledTaskRun.TaskId` is null).

## Goals

- A module marks a command as background in its configuration, in one call.
- The generated views start a background command instead of sending it, with no module code.
- Starting needs only the command's own permissions, not `permission.sys.scheduled-tasks.manage`.
- A command may run for any length of time without the recovery sweep re-sending it.
- A run can report progress and can be cancelled, and cancellation works across processes.
- The app bar shows the user's active and recently finished runs from every module that opted into scheduled
  tasks, updated live, each linking to its target.
- The design keeps working when a module later becomes Remote.

## Non-goals

- A page listing every user's runs (an admin view). Left for later; it is a query over the same directory.
- Returning a large result from a background command. The effect lands in domain data; the run keeps status only.
- Changing how cron schedules work, or who a cron run acts for.
- A broker or LISTEN/NOTIFY transport for live updates. Polling covers the cross-process case for now.

## Anti-goals

- **No central run table.** A run row is saved in the same transaction as the module's own data (claim-before-send),
  so it must live in the module's schema. A `sys.background_runs` table would need a cross-context transaction.
- **No SQL that names more than one module's schema.** SharedKernel never hard-codes a module schema, and a Remote
  module's tables are not in this database at all.
- **No second execution path.** A background command always runs as a run item, through `ScheduledRunItemExecutor`.

## Decisions made with the user

- Background commands reuse scheduled tasks; no new job concept, table or engine.
- Each module keeps its own run tables. The indicator reads across modules through a fan-out reader in SharedKernel,
  in the shape of the outbox relay's per-module sources.
- A run is identified across modules by `(Schema, RunId)`.
- Live updates come from an in-process notifier, with polling as the cross-process fallback.
- The fan-out goes through an `IBackgroundRunSource` seam, so a Remote module adds a source later without changing
  the directory.

## Concepts

- **Background command**: a schedulable command whose configuration also calls `Background(...)`. It is only ever
  executed as a run item.
- **Background run**: a `ScheduledTaskRun` with `TaskId = null` and `Trigger = Background`, one item per target,
  acting for the person who started it, in that person's current scope.
- **Activity**: the signed-in user's runs of triggers `Background`, `RunNow` and `OneOff` — the runs that person
  started by hand. Cron runs (`Scheduled`) are not activity: they fire unattended and would fill the indicator.
- **Run reference**: `BackgroundRunRef(string Schema, Guid RunId)`, the identity of a run outside its module.

## Rules

### Declaring a background command

```csharp
builder.Schedulable("dm.devices.read-profile")
    .Background(timeout: TimeSpan.FromMinutes(10))
    .RequirePermissions(DevicePermissions.ReadProfile)
    .WithoutExpectedVersion();
```

- `Background(TimeSpan? timeout = null)` on `CommandConfigurationBuilder<TCommand>`, read as
  `CommandSettings.Background` (`BackgroundSettings(TimeSpan? Timeout)`). `ScheduledCommand` gains the same value.
- Host start is refused, naming the request, for: `Background()` without `Schedulable(name)`; a timeout of zero or
  below; a timeout above `Sergin:ScheduledTasks:MaxBackgroundTimeout` (default 1 hour). Every other refusal that
  applies to a schedulable command already applies.
- A background command that may take several targets is `ISelectable`, as for scheduling.

### Starting

- `StartBackgroundCommand(IBaseCommand Template, IReadOnlyCollection<BackgroundTarget> Targets)` →
  `ErrorOr<BackgroundRunStartedResponse(BackgroundRunRef Run)>`, a SharedKernel command beside
  `RunCommandOnceCommand`. `BackgroundTarget(Guid TargetId, Guid? RootId, string? Label)`. The schema comes from
  the catalogue (`ScheduledCommand.Schema`), not from the caller.
- Its configuration requires **no** permission of its own. The handler checks the template command's own
  permissions on the current scope (a new `ScheduledTaskAuthority.EnsureMayRunAsync`, the half of
  `EnsureMayScheduleAsync` without the manage permission). This is the one rule that differs from Run once.
- The validator refuses: a template the catalogue does not hold, a template that is not `Background()`, no targets,
  several targets for a command that is not `ISelectable`, more targets than `Sergin:ScheduledTasks:MaxTargets`.
- Before the run is written, the handler builds the command for each target and runs its `IValidator<TCommand>`, if
  any, and refuses with every error. A bad request fails at the click, not as a failed run. The executor still runs
  the full pipeline per item; this check is a convenience.
- The handler writes the run through `ScheduledRunStarter` with `ScheduledRunTrigger.Background` and returns the
  reference. It returns as soon as the items are enqueued.
- `ISerginDispatcher` gains no method. A convenience extension, `Dispatcher.StartAsync(template, targets)`, sends the
  start command.

### A background command is never sent inline

- `BackgroundCommandGuardBehavior`, a pipeline behavior registered first in `AddSerginCore`, refuses a request whose
  settings say `Background` unless the scope carries an `IRunItemContext` (seeded by `ScheduledRunItemExecutor`).
  The error is `ScheduledTaskErrors.MustRunInBackground` (`ErrorType.Validation`, declared text).
- So a WebApi endpoint, a test, or a hand-written page cannot block on it by accident. Tests that sent
  `ReadDeviceProfileCommand` directly start it instead and wait for the run, or run it through the executor.

### Running: heartbeat, timeout, progress

- **Heartbeat in the executor.** While a handler runs, `ScheduledRunItemExecutor` updates the item's
  `last_activity_at_utc` every `StaleAfter / 3`. The recovery sweep then reads silence, not duration: an item is
  stale only when the process running it stopped. This removes the documented caveat ("a command that runs longer
  than `StaleAfter` is re-sent") for every schedulable command, not only background ones.
- **Timeout.** When `Background(timeout)` is set, the executor cancels the handler's `CancellationToken` after it;
  the item ends `Failed` with `ScheduledTasks.TimedOut`, final, no retry.
- **Progress.** `IRunProgress` (scoped; a no-op outside a run item) has `ReportAsync(int percent, TextDefinition?
  message = null, object? arguments = null, CancellationToken ct = default)`. It writes `progress` and
  `progress_message_key`/`progress_message_args` on the item, and touches `last_activity_at_utc`. Writes are
  throttled to one per two seconds per item; the last call before completion is always written. A handler that
  never reports shows an indeterminate bar.
- **Retries** are unchanged. A background command may run more than once, so it must be safe to repeat. Reading a
  profile is; a background command that writes owns its idempotency, as an outbox consumer does.

### Cancelling

- `CancelBackgroundRunCommand(string Schema, Guid RunId)`. Allowed to the run's acting user, or to a holder of
  `permission.sys.scheduled-tasks.manage` on the run's scope.
- It stamps `cancel_requested_at_utc` on the run and moves every `Pending` or `Retrying` item to `Cancelled`.
- A `Running` item learns of it on its next heartbeat: the heartbeat's UPDATE returns the run's cancel stamp, and the
  executor cancels the token. Because the flag is in the database, this works when the item runs in another process.
- The claim refuses an item of a cancelled run, so a late engine delivery does nothing.
- New item status `Cancelled`. The run completes when its last item finishes, as today, and gains a `Cancelled`
  count beside `Succeeded` and `Failed`.

## Architecture

### Where things live (all SharedKernel unless named)

| Piece | Project |
|---|---|
| `Background()`, `BackgroundSettings`, the declaration refusals | `Application` (`Commands/Configuration`, `ScheduledTasks`) |
| `StartBackgroundCommand`, `CancelBackgroundRunCommand`, their handlers and validators | `Application/ScheduledTasks/Commands` |
| `BackgroundCommandGuardBehavior`, `IRunItemContext`, `IRunProgress` | `Application/ScheduledTasks` |
| Heartbeat, timeout and cancel in `ScheduledRunItemExecutor` | `Application/ScheduledTasks` |
| `IBackgroundRunSource`, `IBackgroundRunDirectory`, `IRunNotifier`, `BackgroundRunRef`, `ActivityItem` | `Application/ScheduledTasks/Activity` |
| `LocalBackgroundRunSource`, the store queries | `Infrastructure.Data.EFCore/ScheduledTasks` |
| `SerginActivityIndicator`, the start path in generated views | `Presentation.Blazor` (`ScheduledTasks`, `Aggregates`) |
| `ReadDeviceProfileCommandConfiguration` calls `Background(...)` | DeviceManagement `.Application.Configurations` |

### Storage

A migration in every module that opted into scheduled tasks (today: DeviceManagement):

- `scheduled_task_runs`: `cancel_requested_at_utc timestamptz NULL`, `cancelled int NOT NULL DEFAULT 0`, and an index
  `ix_scheduled_task_runs_acting_user_id_started_at_utc` on `(acting_user_id, started_at_utc DESC)`.
- `scheduled_task_run_items`: `target_label text NULL`, `progress smallint NULL` (0–100),
  `progress_message_key text NULL`, `progress_message_args jsonb NULL`. The status column accepts `Cancelled`.
- The `trigger` column accepts `Background`.

`target_label` is the text the person saw when starting (`MTR-12`), stored as user data, so the indicator never
reads a module's tables to name a target. It is null for cron runs.

### Reading across modules

```
SerginActivityIndicator
   └─ IBackgroundRunDirectory            (singleton; merges, sorts by StartedAtUtc desc)
        ├─ LocalBackgroundRunSource("dm")  ──> dm.scheduled_task_runs / _items
        ├─ LocalBackgroundRunSource("xx")  ──> xx.scheduled_task_runs / _items
        └─ (later) RemoteBackgroundRunSource("yy") ──> gRPC ListActivity
```

- `IBackgroundRunSource`: `string Schema`; `ListActivityAsync(UserId user, DateTime finishedSinceUtc, int take, ct)`;
  `GetRunAsync(Guid runId, ct)`. Returns `ActivityItem`s: `Ref`, command name, trigger, status, started and
  completed times, total / succeeded / failed / cancelled counts, and for a one-target run its target id, root id,
  label and progress.
- `AddModuleDbContext` registers one `LocalBackgroundRunSource` per `ScheduledTasksRegistration`, beside the module's
  store. Each source opens its own scope and runs one indexed SQL read: the user's runs that are unfinished, or
  finished after `finishedSinceUtc` (24 hours), activity triggers only, newest first, at most `take`.
- **Own runs ignore the scope filter.** A person always sees the runs they started, in every scope, even after a
  grant change. The list shows only what the person typed or saw (label, command, status), never a record's data.
- `IBackgroundRunDirectory.ListActivityAsync(user)` calls every source in parallel. A source that throws is logged and
  skipped, so one module's failure does not empty the indicator.
- `CancelAsync(BackgroundRunRef)` resolves the schema's store through `IScheduledTaskStoreResolver`.

### Live updates

- `IRunNotifier` (singleton, in-process): `Publish(RunChanged)` and `Subscribe(UserId, Func<RunChanged, Task>)`
  returning an `IDisposable`. `RunChanged(BackgroundRunRef Ref, UserId ActingUser)`.
- The starter, the executor (claim, progress write, item finish, run completion) and the cancel handler publish after
  their save. Publishing never throws into the caller.
- Each circuit's indicator subscribes for its own user and reloads through `InvokeAsync`.
- **Fallback poll**: while the badge is above zero the indicator also reloads every 5 seconds, which covers a run
  executed by another process (TickerQ in a worker host). With no active run it does not poll.

### Names and links

- Command label: `ScheduledCommandCatalog` maps the stored command name to the request type; the label is the
  command's view label from `AggregateViewRegistry`, else the schedule name.
- Module: the `SerginNavGroup`/caption of the module whose schema matches, from `SerginUiModuleCatalog`.
- Target link: the root's detail route from `AggregateViewRegistry` with the target id (and root id for a child
  target). No link when the registry has no detail route.

## UI

### Starting from a generated view

- A command whose settings say `Background` is started, not sent: the detail bar action, the list row action and the
  bulk action call `StartAsync` with the record(s) as targets and their title field as `Label`.
- Bulk becomes one run with N items, not N sends through `SerginBulk.RunAsync`.
- On success: a snackbar "Read profile started" with an action "Show" that opens the indicator. On refusal: the
  errors, as any refused send.
- The action shows a small spinner while the record has an unfinished run of that command, read from the directory
  through the notifier. Clicking again is allowed; overlap is a cron-task rule only.

### The activity indicator

- `SerginActivityIndicator`, rendered by `SerginMainLayout` in the app bar beside the scope picker, for a signed-in
  user only. Nothing renders until the first activity load succeeds.
- Icon with a badge: the count of the user's unfinished runs. With none, the icon stays, without a badge.
- A popover, two parts:
  - **Running**: per run, the command label, the target label (or "{Count} records"), the module caption, a progress
    bar (determinate when one target reported progress, else the items done / total), the elapsed time, Cancel, and
    a link to the target's detail.
  - **Finished (last 24 hours)**: per run, label, target, outcome (succeeded / failed / cancelled counts, the first
    error text for a one-target failure), finished time, and the link. A "Clear" that hides finished entries for
    this user (stored as a per-user watermark in browser storage; losing it only shows them again).
- When a run of the user's finishes, a snackbar names it and links to the target.
- Below md, the indicator stays an icon; the popover becomes a full-width sheet.
- Every text is a declared `TextDefinition` in `ScheduledTaskUiTexts` (`common.activity.*`); times go through
  `ICultureFormatter`.

### The record's own section

`SerginScheduledTasksSection`'s "Recent one-off runs" also lists `Background` runs on that target, with their
progress and Cancel.

## Proof

- **Declarations**: `Background()` without `Schedulable`, a bad timeout, and a timeout above the maximum each refuse
  host start naming the request.
- **Start**: a holder of the command's permission without the manage permission starts a run; a caller without the
  command's permission is refused and no run is written; a validator error refuses with no run written; the schema is
  the catalogue's; one start over N targets writes one run with N items.
- **Guard**: sending a background command through `ISerginDispatcher.SendAsync` is `MustRunInBackground`; the same
  command inside a run item passes.
- **Heartbeat**: a handler running longer than `StaleAfter` is not re-sent by the sweep; an item whose heartbeat stops
  is re-sent.
- **Timeout**: an item exceeding its timeout ends `Failed`, `ScheduledTasks.TimedOut`, not retried.
- **Cancel**: pending items are cancelled at once; a running item stops on its next heartbeat and ends `Cancelled`; a
  cancel from a second `ScheduledRunItemExecutor` scope (standing in for another process) reaches it; a late delivery
  of a cancelled item does nothing; only the acting user or a manage holder may cancel.
- **Directory**: two test contexts under two schemas, runs in both, one merged list sorted by start; a cron run is not
  listed; another user's run is not listed; a run in a scope the user no longer reaches is still listed; a throwing
  source is skipped.
- **Notifier**: a subscribed circuit is told of start, progress and finish; another user's circuit is not.
- **UI**: the indicator prerenders nothing for an anonymous user, shows the badge count, and renders a running and a
  finished entry; a generated Device page's Read profile starts a run (headless-browser walk: click, badge appears,
  progress moves, finished snackbar, link opens the device).
- **Read profile**: `ReadDeviceProfileTests` and `DeviceReadProfileScheduleTests` move to the start path.

## Calls I made (for the user to check)

1. **Only runs a person started are activity.** Cron runs stay on the record's schedule section. Alternative: show
   cron runs to the user who last saved the task.
2. **Starting needs the command's own permission only.** Run once keeps needing the manage permission. Alternative:
   fold Run once into Start and drop the manage requirement there too.
3. **The executor heartbeats for every schedulable command**, not only background ones, which changes existing
   behavior (for the better: the `StaleAfter` caveat goes away).
4. **A background command sent inline is refused**, not silently started. It breaks any caller that sends it inline
   today; there are only tests.
5. **`target_label` is stored on the item.** It can go stale if the record is renamed; the link always goes to the
   live record.
6. **Own runs ignore the scope filter.**
7. **Timeout is final**, no retry.
8. **"Clear" lives in browser storage**, per viewer, not on the server.

## Left to the builder

- The exact `MaxTargets` default (proposal: 1000) and whether scheduling shares it.
- Whether `progress_message_args` is worth a column before a handler needs a message (could ship percent only).
- The heartbeat implementation: a `PeriodicTimer` beside the handler task, cancelled with it.
- Placement of the indicator relative to the language switcher and scope picker in the app bar at each breakpoint.
- Split into plans: (1) heartbeat, timeout, `Background()`, start, guard, Read profile; (2) directory, notifier,
  indicator; (3) cancel and progress.
