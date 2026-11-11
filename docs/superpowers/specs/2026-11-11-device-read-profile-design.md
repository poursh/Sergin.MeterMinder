# Device "Read profile": first real scheduled command — design

Date: 2026-11-11. Builds on `docs/superpowers/specs/2026-11-10-scheduled-commands-design.md` (the scheduled-commands
plumbing, branch `feature/scheduled-commands`, not yet merged). Read that spec first; this one only adds to it.

## Intent

The scheduled-commands plumbing is proven only by test-only commands, a fake engine and an in-memory test page. No module
opts in and the real host registers no engine. This change puts the feature in front of a person on the real Device pages
with one command that does nothing real, so the whole flow (schedule, run now, run history, delegation, per-run grant
checks) can be clicked through before meter communication exists.

The TickerQ Community licence file is not available yet. The host must start and be useful without it.

## Goals

1. A `ReadDeviceProfile` command on the Device aggregate, schedulable as `dm.devices.read-profile`. Its handler
   simulates the work: it waits a fixed 3 seconds (`Task.Delay`, honouring the cancellation token) and succeeds. It
   reads no meter, writes nothing, changes no row version. *(Your decision: "use sleep awaiter to simulate".)*
2. DeviceManagement opts in to scheduled tasks as the first real module.
3. The real host (`Sergin.MeterMinder.Hosts.All`) wires the engine through `AddSerginTickerQ`, and starts **without a
   TickerQ licence** by falling back to a stand-in engine. *(Your decision: "start without the engine until you get
   one".)*
4. With the stand-in, saving a schedule is **allowed**; it is kept and starts firing once the licence is configured.
   *(Your decision.)*
5. The deferred headless-browser click check of the scheduled-task UI runs on the real device page.

## Non-goals

- No real meter read, no device parameter argument, no result stored on the device.
- No `tickerq` table creation in production yet: the stand-in never touches them. It becomes the job of the change that
  configures the licence (noted as residual work, below).
- No change to the scheduled-commands semantics (delegation, overlap, retry, recovery, purge) beyond what the stand-in
  needs.
- No new UI component in SharedKernel beyond the "engine off" indication.

## Anti-goals

- A schedule saved with the engine off must not silently look like it will fire.
- The licence's arrival must not need a code change or a data migration — configuration and a restart only.
- No `if (engineOff)` branches scattered through handlers; the difference lives behind `IScheduledTaskEngine`.

## The command slice (DeviceManagement)

- **Contracts**: `ReadDeviceProfileCommand(DeviceIntenralId Id) : ICommand, ITransitionCommand<Device, DeviceIntenralId>, ISelectable`
  under `Devices/Commands/ReadProfile/`. *(My call: `ITransitionCommand` is the closest entity-target effect for "do
  something to this device"; `ISelectable` because a schedule covers picked devices.)* No arguments beyond the id.
- **Configuration**: `ReadDeviceProfileCommandConfiguration`: `Schedulable("dm.devices.read-profile")`,
  `RequirePermissions(DevicePermissions.ReadProfile)`, `WithoutExpectedVersion()` (Device is `Versioned()`, and the
  scheduled-commands spec requires a version-guarded schedulable command to waive it; the command changes nothing).
- **Permission**: `DevicePermissions.ReadProfile` = `permission.dm.devices.read-profile`, label "Read device profile",
  registered in `DeviceAggregateFeatureConfiguration`. *(My call.)* The dev-seed's last block grants it where it grants
  the other device permissions to the seeded roles that hold `permission.dm.devices.update`. *(My call.)*
- **Handler**: `ReadDeviceProfileCommandHandler` loads nothing beyond an existence check through the device repository
  (scope filter applies, so a device out of reach is `Error.NotFound()`), then `Task.Delay(TimeSpan.FromSeconds(3), ct)`,
  then `Result.Success`. The delay is a named constant on the handler. *(My call: the existence check makes a deleted or
  moved-out-of-reach device fail its run item with a real error, which is the useful thing to see in the run history.)*
- **Validator**: none needed beyond the id; the scheduled-commands rule "every write command has a validator" is met
  with an empty-shape validator only if the existing guard requires one — the builder checks and follows the guard.
- **Texts**: `DeviceTexts` gains nothing a person sees on the command (the id is hidden by its target interface).
  `DeviceUiTexts` gains `ReadProfile` "Read profile" (the action and the schedule dialog's title).

## The UI (DeviceManagement Blazor)

- `DeviceDetailView`: a **Read profile** action (Secondary, `Permission: DevicePermissions.ReadProfile`) that sends the
  command through `ISerginDispatcher` and reports success or the error by snackbar; next to it the **Schedule…** action
  from `SerginScheduleAction.PageAsync`, opening `ShowScheduleAsync` with the one device as target, label = its
  `DeviceId`, no `ArgumentsForm`. `SerginScheduledTasksSection Schema="dm" TargetId="Id"` goes in the view's `Entities`.
- `DeviceListView`: both as selection-bar actions — **Read profile** runs per row through `SerginBulk.RunAsync` with
  `SerginBulkOutcome.Report`, as bulk delete does; **Schedule…** from `SerginScheduleAction.BulkAsync` opens one
  schedule over the selected devices (targets = selected rows, labels = their `DeviceId`s). *(My call: same shape as bulk
  delete.)*

"Read profile" from the page dispatches the command directly (synchronous, 3 s, the button shows busy); it does not go
through a one-off run. The schedule dialog's own "Run now without saving" stays the way to run it through the engine.

## Opting DeviceManagement in

Exactly the steps SharedKernel's CLAUDE.md "Scheduled tasks" lists: `DeviceManagementDbContext` implements
`IScheduledTasksDbContext`, calls `ApplyScheduledTasks()`, adds `ScheduledTaskConfigurationTypes` to its
`AggregateFeatures`, and a new migration `AddScheduledTasks` creates the four tables with their partial indexes in `dm`
(hand-converted to a file-scoped namespace; this breaks the "exactly one migration per module" note in the root CLAUDE.md,
which this change updates). *(My call: a second migration, not a rewritten `InitialCreate`, so existing dev databases
upgrade without a reset.)*

## The stand-in engine

- **Where**: in `Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ`, beside the real adapter, because NCrontab
  (cron validation and next-occurrence previews) is already referenced only there. Name: `InProcessScheduledTaskEngine`.
- **How it is chosen**: `AddSerginTickerQ(serginSection, connectionString)` reads
  `Sergin:ScheduledTasks:TickerQ:LicensePath`. Blank or absent: register the stand-in, log one warning at start
  ("TickerQ licence not configured; scheduled runs are off, run-now and one-off runs execute in process"). Set: the real
  TickerQ path exactly as today, including `TickerQLicenseGuard` (a path naming no file still fails start — a typo must
  not silently fall back). *(My call.)*
- **Behaviour**:
  - `IsValidCron`, `NextOccurrences`: same NCrontab code the real adapter uses (shared, not copied).
  - `EnqueueRunItemAsync`: writes the item reference into an in-process bounded channel and returns a new id; a hosted
    worker reads the channel and calls the same `ScheduledRunItemExecutor` the TickerQ function calls, in a fresh scope.
    Retries: the executor leaves a failed attempt Retrying; the worker re-enqueues it after the configured
    `RetryIntervalsSeconds` delay, like TickerQ's retry would. Lost items (process restart) are picked up by the existing
    `ScheduledRunItemRecoveryService`, which re-enqueues through the engine — no new recovery code.
  - `ScheduleAsync`/`RescheduleAsync`: return or keep an id; nothing ever fires. `SetEnabledAsync`, `RemoveAsync`: no-op.
  - `ExistsAsync`: `false`, `GetEnabledAsync`: `null`, `ListTriggersAsync`: empty — so when the real engine replaces it,
    the startup trigger rebuild recreates a trigger for every saved schedule. (The rebuild with the stand-in itself must
    not churn: it relinks every start only if the builder finds no cheaper way; acceptable if not, since rebuild runs
    once per start in the background. Builder's call.)
- **"Engine off" indication**: `IScheduledTaskEngine` gains `bool FiresTriggers { get; }` (true for TickerQ and the test
  fake, false for the stand-in). `SerginScheduledTasksSection` shows an info line "Scheduled runs are off until the
  scheduling engine is configured. Run now still works." when false, and `SerginScheduleDialog` shows the same line above
  Save. Text keys under `common.scheduled-tasks.*`. *(My call on the mechanism.)*

## Tests

- Integration: the command through `ISerginDispatcher` (delay honoured with a shortened delay or a cancelled token,
  out-of-reach device not found, forbidden without the permission); a scheduled-run item on a `dm` task executed through
  the stand-in end to end (one-off run completes Succeeded as the clicker); a saved cron schedule with the stand-in is
  stored and never fires; `FiresTriggers` false renders the info line; host start with and without a licence path picks
  the right engine and a bad path still refuses; the `dm` model carries the four tables; `MigrationPinTests` and the
  catalog keys file updated; `PseudoLocaleSweepTests` still passes on the device pages.
- Headless browser (the deferred check): on the real host, device detail: schedule daily, see it listed with "engine
  off", pause, resume, delete; Read profile shows busy then success; one-off "Run now" from the dialog appears in the
  latest one-off runs as Succeeded.

## Residual work (not in this change)

- Configuring the licence: provide the file, set the path, and create the `tickerq` tables before start (TickerQ's
  migrations or `CreateTablesAsync` at Development start). Then run the three licence-gated tests.
- A real "read profile" once device communication exists: replace the handler body; the schedule name stays.

## Settled after the builder check (my calls)

- **Recovery with the stand-in.** `ScheduledRunItemRecoveryService` selects unfinished items by `last_activity_at_utc`
  older than `StaleAfter`, not by `ExistsAsync`, so the stand-in's `ExistsAsync` (triggers only) never causes a re-send.
  An item the worker is running is claimed (Running, fresh activity) and is not stale. After a restart, items lost from
  the channel come back after `StaleAfter` (15 min) — accepted for a stand-in. Retry delays are held by the worker
  (`Task.Delay` before re-writing to the channel); a restart in between is again covered by recovery.
- **Shared cron code.** An `internal static class CronSchedule` (`IsValid`, `NextOccurrences`) in the adapter assembly;
  both engines call it. The adapter's public surface gains only the stand-in's registration inside `AddSerginTickerQ`.
- **Command shape.** If `CommandTypeGuard` or `FieldTextGuard` refuses `ITransitionCommand` with no reverse or with no
  shown property, the first task proves it with a startup test and switches the effect to `ISetPropertyCommand<Device,
  DeviceIntenralId>`; the Device category comes from the root, so "no category" cannot apply.
- **Scheduling permission.** The parent design's per-module permission was replaced during its build by the platform's
  `permission.sys.scheduled-tasks.manage` (`ScheduledTaskPermissions.Manage`); nothing new to define in `dm`.
- **Seeded grants.** `dev-seed-users.sql`'s every-run block adds `permission.dm.devices.read-profile` and
  `permission.sys.scheduled-tasks.manage` to the roles that hold `permission.dm.devices.update`. The dev user and
  `admin.root` hold the seeded `administrator` role; the first task checks whether that role passes every permission or
  holds explicit rows, and adds the two rows to it the same way (an idempotent insert in the every-run block, never in a
  migration that names another module's permission) if it holds explicit rows.
- **Registration.** The channel, the stand-in and its worker (`BackgroundService`) are registered only when the licence
  path is blank; with a path, none of them exist.

## Left to the builder

Exact file names inside the folders named above; whether the 3 s delay is overridable in tests (an option or an
injected `TimeProvider`); the channel's capacity; the trigger-rebuild churn question above.
