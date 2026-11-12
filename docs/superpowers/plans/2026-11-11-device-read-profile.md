# Device Read Profile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put scheduled tasks on the real Device pages with a simulated `ReadDeviceProfile` command, and let the host run without a TickerQ licence through an in-process stand-in engine.

**Architecture:** DeviceManagement gains one schedulable command slice and opts its `DbContext` in to scheduled tasks. The TickerQ adapter's `AddSerginTickerQ` registers either real TickerQ (licence path set) or `InProcessScheduledTaskEngine` (blank path), which runs run items through a channel and a hosted worker and never fires cron triggers. `IScheduledTaskEngine.FiresTriggers` lets the UI say scheduled runs are off.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, MediatR, ErrorOr, MudBlazor 9.9, TickerQ 10.5.1, NCrontab 3.3.0, xUnit + Testcontainers, Playwright-Python (browser check).

**Spec:** `docs/superpowers/specs/2026-11-11-device-read-profile-design.md` (builds on `docs/superpowers/specs/2026-11-10-scheduled-commands-design.md`).

## Global Constraints

- Work in worktree `C:\@factory\Sergin\Sergin.MeterMinder-scheduled-commands`, branch `feature/scheduled-commands` in the root and in `src/SharedKernel`. SharedKernel changes are committed in the submodule first, then the root commits the bump.
- Never add a `Co-Authored-By` trailer (root CLAUDE.md).
- `TreatWarningsAsErrors`, `AnalysisMode=All`, Sonar: the build must stay at zero warnings. Central Package Management: no `Version` on a `PackageReference`.
- `.razor` files hold no `@code`; C# in `.razor.cs`. Every visible text is a declared `TextDefinition`; no literals.
- Stable schedule name: `dm.devices.read-profile`. Permission: `permission.dm.devices.read-profile`, label `Read device profile`. UI text `ReadProfile` = `Read profile`.
- The simulated work: `Task.Delay(TimeSpan.FromSeconds(3), cancellationToken)`; reads no meter, writes nothing, changes no row version.
- Licence key: `Sergin:ScheduledTasks:TickerQ:LicensePath`. Blank or absent selects the stand-in; a non-blank path that names no file still refuses start (`TickerQLicenseGuard` unchanged).
- Off-state copy, exact: `Scheduled runs are off until the scheduling engine is configured. Run now still works.` Key under `common.scheduled-tasks.*`.
- Saving a schedule is allowed with the stand-in.
- Test command: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` (Docker must be running). Build: `dotnet build Sergin.MeterMinder.slnx`.

## Review Focus

1. A device deleted (or moved out of the caller's reach) after a schedule was saved: its run item must end Failed with `NotFound`, not hang or retry. Test in Task 1 (handler) and Task 4 (through a run).
2. A user with `permission.sys.scheduled-tasks.manage` but without `permission.dm.devices.read-profile`: no Read profile or Schedule… action visible, and a direct send is Forbidden. Test in Tasks 1 and 5.
3. Host restart with the stand-in while an item sits in the channel: the item is recovered by `ScheduledRunItemRecoveryService` after `StaleAfter`, never run twice concurrently. Test in Task 2 (a recovery pass re-enqueues a stale item once).
4. Licence path pointing at a missing file: start fails, it does not fall back to the stand-in. Test in Task 2.
5. Two clicks on Read profile in a row on the detail page: the button is disabled while the first send runs. Test in Task 5 (render state) and the browser check in Task 6.

---

### Task 1: The `ReadDeviceProfile` command slice

**Files:**
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Devices/Commands/ReadProfile/ReadDeviceProfileCommand.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Configurations/Devices/Commands/ReadProfile/ReadDeviceProfileCommandConfiguration.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/ReadProfile/ReadDeviceProfileCommandHandler.cs` (+ `ReadDeviceProfileCommandValidator.cs` only if a startup or convention test demands one)
- Modify: `.../Application.Configurations/Devices/DevicePermissions.cs`, `.../Devices/DeviceAggregateFeatureConfiguration.cs`
- Modify: `tests/.../Commands/Types/catalog-keys.txt` if the permission label adds a key; `tests/.../Commands/ModuleCommandConfigurationTests.cs` if it enumerates dm declarations
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Devices/ReadDeviceProfileTests.cs`

**Interfaces:**
- Produces: `public sealed record ReadDeviceProfileCommand(DeviceIntenralId Id) : ICommand, ITransitionCommand<Device, DeviceIntenralId>, ISelectable` (namespace `...Application.Contracts.Devices.Commands.ReadProfile`); `DevicePermissions.ReadProfile`.
- If `CommandTypeGuard` refuses `ITransitionCommand` (for example it needs `IReverseOf`), use `ISetPropertyCommand<Device, DeviceIntenralId>` instead and say so in the report; later tasks use only the record name.

- [ ] **Step 1: Write the failing tests** in `ReadDeviceProfileTests` (collection `IntegrationTestCollection`, dispatch through `ISerginDispatcher` from a scope, as `DeviceManagementSoftDeleteTests` does):
  - `ReadProfile_OnALiveDevice_SucceedsAfterTheDelay`: create a device, send `ReadDeviceProfileCommand`, assert `!result.IsError` and elapsed ≥ 3 s (Stopwatch, tolerance 100 ms below).
  - `ReadProfile_LeavesTheRowVersionAlone`: version from `GetDeviceByIdQueryCommand` via `SendVersionedAsync` before and after is equal.
  - `ReadProfile_OnADeletedDevice_IsNotFound`: `result.FirstError.Type == ErrorType.NotFound`.
  - `ReadProfile_WithoutThePermission_IsForbidden`: a seeded caller holding `permission.dm.devices.read` only (use `RbacScenario`) gets `ErrorType.Forbidden`.
  - `ReadProfile_IsSchedulable`: `ScheduledCommandCatalog` resolves `"dm.devices.read-profile"` to `typeof(ReadDeviceProfileCommand)`.
- [ ] **Step 2: Run** `dotnet test ... --filter FullyQualifiedName~ReadDeviceProfileTests`. Expected: build fails (type missing).
- [ ] **Step 3: Implement.** Permission `ReadProfile` = `PermissionDefinition.Create("permission.dm.devices.read-profile", "Read device profile")`, registered in `.Permissions(...)`. Configuration: `builder.Schedulable("dm.devices.read-profile").RequirePermissions(DevicePermissions.ReadProfile).WithoutExpectedVersion();`. Handler `internal sealed class ReadDeviceProfileCommandHandler(IDeviceRepository repository) : ICommandHandler<ReadDeviceProfileCommand>` (match the non-generic `ICommand` handler interface the repo uses): `ExistsAsync` false gives `Error.NotFound()`; else `await Task.Delay(SimulatedReadDuration, cancellationToken)` with `internal static readonly TimeSpan SimulatedReadDuration = TimeSpan.FromSeconds(3);` and return success. A comment says the delay stands in for meter communication.
- [ ] **Step 4: Run** the filter, then the full suite. Expected: all pass (update `catalog-keys.txt` / pin tests if they fail only because of the new key or declaration).
- [ ] **Step 5: Commit** `Device read profile: simulated schedulable command`.

### Task 2: The in-process stand-in engine (SharedKernel)

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ/CronSchedule.cs` (`internal static class CronSchedule { bool IsValid(string cron, string timeZoneId); IReadOnlyList<DateTimeOffset> NextOccurrences(string cron, string timeZoneId, DateTimeOffset after, int count); }` — the bodies move from `TickerQScheduledTaskEngine`, which now delegates)
- Create: `.../InProcessScheduledTaskEngine.cs`, `.../InProcessRunItemWorker.cs` (`BackgroundService`)
- Modify: `.../TickerQEngineExtensions.cs` (branch on the licence path; add `public static IHost UseSerginTickerQ(this IHost host)` that calls `UseTickerQ()` only when the real engine is registered)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/IScheduledTaskEngine.cs` (add `bool FiresTriggers { get; }`), `TickerQScheduledTaskEngine` (`true`), `tests/.../ScheduledTasks/FakeScheduledTaskEngine.cs` and `FailingScheduledTaskEngine.cs` (`true`)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/InProcessEngineTests.cs`

**Interfaces:**
- Produces: `public sealed class InProcessScheduledTaskEngine : IScheduledTaskEngine` (`FiresTriggers => false`); `AddSerginTickerQ(services, serginSection, connectionString)` unchanged signature; `UseSerginTickerQ(this IHost)`.
- Consumes: `IScheduledTaskRunner.ExecuteRunItemAsync(schema, runItemId, ct)` → `true` = retry; `IScheduledTaskStoreResolver`, `ScheduledTaskOptions.RetryIntervalsSeconds`, `IDateTimeProvider` — the worker's retry follows `SerginTickerFunctions.ExecuteAsync` exactly (pick the interval by `Attempts - 1` clamped, wait it, re-enqueue, `LinkJob`, save). Extract the shared "after an attempt" logic only if it removes real duplication without touching TickerQ types; otherwise mirror it.

Stand-in behaviour (from the spec): `EnqueueRunItemAsync` writes `(reference, notBefore)` to an unbounded `Channel` and returns `Guid.CreateVersion7()`; `ScheduleAsync` returns a new id; `RescheduleAsync`, `SetEnabledAsync`, `RemoveAsync` no-ops; `ExistsAsync` false; `GetEnabledAsync` null; `ListTriggersAsync` empty; `IsValidCron`/`NextOccurrences` call `CronSchedule`. The worker runs each item in a fresh `AsyncServiceScope`, catches and logs every exception (never faults the loop), and runs items concurrently up to `Environment.ProcessorCount` (a `SemaphoreSlim`). With the stand-in, `AddSerginTickerQ` does not call `AddTickerQ` and registers neither `TickerQScheduledTaskEngine` nor the licence guard; it logs once at start (in the worker's `StartAsync`, `LogWarning`): `TickerQ licence not configured; scheduled runs are off, run-now and one-off runs execute in process.`

- [ ] **Step 1: Write the failing tests** (use `ScheduledTaskTestHost` with the stand-in registered instead of `FakeScheduledTaskEngine` — add a host option for it):
  - `BlankLicensePath_RegistersTheStandIn`: `IScheduledTaskEngine` is `InProcessScheduledTaskEngine`, `FiresTriggers` false; `TickerQScheduledTaskEngine` not registered.
  - `MissingLicenseFile_StillRefusesStart`: path `C:\nope\x.tqlicense` → host start throws `OptionsValidationException` naming `LicensePath` (reuse `TickerQGateTests`' shape).
  - `OneOffRun_ThroughTheStandIn_Succeeds`: `RunCommandOnce` with `TouchProbeCommand` → poll the run (≤ 10 s) until Completed, item Succeeded, probe `LastNote` set, delegated actor is the sender.
  - `Retry_ThroughTheStandIn_EndsExhausted`: with `RetryIntervalsSeconds=[1]`, `MaxAttempts=2`, a command that throws → item Failed `ScheduledTasks.Exhausted`, attempts 2.
  - `SavedCron_IsStoredAndNeverFires`: create a task with cron `* * * * * *` (every second), wait 3 s, task stays Active with zero runs.
  - `NextOccurrences_MatchesTheTickerQEngine`: same cron/zone/after on both engines (construct `TickerQScheduledTaskEngine` only if constructible without TickerQ managers; otherwise compare `CronSchedule` against a hard-coded expected list for `0 0 3 * * *` in `Asia/Tehran`).
  - `RecoveryPass_ReEnqueuesAStaleItemOnce`: an item left Pending with old `LastActivityAtUtc` is run exactly once after one recovery pass.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~InProcessEngineTests`. Expected: build fails.
- [ ] **Step 3: Implement** as above.
- [ ] **Step 4: Run** the full suite. Expected: all pass; the three licence-gated tests still skip.
- [ ] **Step 5: Commit** in `src/SharedKernel` `Scheduled tasks: in-process stand-in engine when no TickerQ licence`, then the root (test changes + bump).

### Task 3: Wire the real host

**Files:**
- Modify: `src/Hosts/Sergin.MeterMinder.Hosts.All/Sergin.MeterMinder.Hosts.All.csproj` (reference `Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ`), `Program.cs`
- Modify: `src/Hosts/Sergin.MeterMinder.Hosts.All/appsettings.json` (`"ScheduledTasks": { "TickerQ": { "LicensePath": "" } }` under `Sergin`)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/HostEngineTests.cs`

**Interfaces:**
- Consumes: `AddSerginTickerQ`, `UseSerginTickerQ` (Task 2).

- [ ] **Step 1: Write the failing test** `RealHost_WithoutALicence_UsesTheStandIn`: `factory.Services.GetRequiredService<IScheduledTaskEngine>()` is `InProcessScheduledTaskEngine`.
- [ ] **Step 2: Run** it. Expected: FAIL (no engine registered).
- [ ] **Step 3: Implement.** In `Program.cs`, before `Build()`: `builder.Services.AddSerginTickerQ(builder.Configuration.GetSection("Sergin"), builder.Configuration["Sergin:ConnectionStrings:Database"] ?? string.Empty);` with a comment that a blank licence path runs the stand-in. After `Build()`, before `UseSerginWebUiAsync`: `app.UseSerginTickerQ();`.
- [ ] **Step 4: Run** the full suite. Expected: all pass.
- [ ] **Step 5: Commit** `Host: register the scheduling engine (stand-in without a licence)`.


### Task 4: DeviceManagement opts in to scheduled tasks

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/DeviceManagementDbContext.cs`
- Create: `.../Infrastructure.Data/Migrations/<timestamp>_AddScheduledTasks.cs` (+ Designer, snapshot updated) — scaffold with the `dotnet ef migrations add AddScheduledTasks` command from root CLAUDE.md, then convert the `.cs` to a file-scoped namespace.
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/DeviceManagementScheduledTasksModelTests.cs`

**Interfaces:**
- Consumes: `IScheduledTasksDbContext`, `ApplyScheduledTasks()`, `ScheduledTaskConfigurationTypes.AggregateConfigurations` (SharedKernel EFCore/Application).
- Produces: the four scheduled-task tables in schema `dm`; the real host (engine wired in Task 3) now runs the scheduled-task hosted services for `dm`.

- [ ] **Step 1: Write the failing test** `DmModel_CarriesTheScheduledTaskTables`: from the host's `DeviceManagementDbContext` model, `FindEntityType(typeof(ScheduledTask))` has schema `dm` and table `scheduled_tasks`; same for runs and run items; index `ix_scheduled_task_runs_task_id_running` exists with filter containing `'Running'`. And `DmContext_IsAScheduledTasksContext`: the resolved `IDeviceManagementDbContext` is `IScheduledTasksDbContext`.
  - In `DeviceReadProfileScheduleTests`: `OneOffReadProfile_OnTwoDevices_BothSucceed` (RunCommandOnce over two devices through the stand-in, run Completed, both items Succeeded) and `ScheduledReadProfile_OnADeletedDevice_FailsNotFound` (a saved task whose device is deleted afterwards; Run now ends with that item Failed, code `General.NotFound`, run Completed).
- [ ] **Step 2: Run** them. Expected: FAIL (entity type not found).
- [ ] **Step 3: Implement.** Context implements `IScheduledTasksDbContext` (the three `DbSet`s), `OnModelCreating` calls `modelBuilder.ApplyScheduledTasks()` after `ApplyOutbox()`, and `AggregateFeatures` becomes `AggregateFeatureRegistry.FromAssemblies([...ConfigurationsAssembly]).Including(ScheduledTaskConfigurationTypes.AggregateConfigurations)`. Update the class's doc comment. Scaffold and convert the migration.
- [ ] **Step 4: Run** the full suite. Expected: all pass, including `AggregateFeatureGuardTests` and the outbox tests.
- [ ] **Step 5: Commit** `DeviceManagement: opt in to scheduled tasks (AddScheduledTasks migration)`.

### Task 5: The UI — off-state line and the Device actions

**Files:**
- Modify (SharedKernel): `Presentation.Blazor/ScheduledTasks/SerginScheduledTasksSection.razor{,.cs}`, `SerginScheduleDialog.razor{,.cs}`, `ScheduledTaskUiTexts.cs` (`EngineOff` = the exact copy in Global Constraints)
- Modify (dm): `.../Presentation.Blazor/Devices/Components/DeviceDetailView.razor{,.cs}`, `DeviceListView.razor.cs`, `.../Presentation.Blazor/Devices/DeviceUiTexts.cs` (`ReadProfile` "Read profile", `ReadProfileDone` "Profile read for {DeviceId}.", `BulkReadProfileDone` — reuse the shape `CommonTexts.BulkDeleted` uses, e.g. "Profile read for {Count} devices.")
- Modify: `tests/.../Commands/Types/catalog-keys.txt`, `tests/.../Localization/PseudoLocaleSweepTests` only if a route needs adding (none expected)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/DeviceScheduleUiTests.cs`, extend `ScheduledTaskUiTests` for the off-state line

**Interfaces:**
- Consumes: `SerginScheduleAction.PageAsync(scopeAuthorization, onClick, DevicePermissions.ReadProfile)`, `SerginScheduleAction.BulkAsync<GetDeviceListItem>(...)`, `IDialogService.ShowScheduleAsync(new SerginScheduleRequest("dm", DeviceUiTexts.ReadProfile, () => new ReadDeviceProfileCommand(new DeviceIntenralId(Id)), targets, labels))`, `SerginScheduledTasksSection Schema="dm" TargetId="Id"`, `IScheduledTaskEngine.FiresTriggers`.

Detail view: the schedule action is created in `OnInitializedAsync` and kept in a field; `Actions` becomes Edit, Read profile (Secondary, `Icons.Material.Outlined.Sensors`, `Permission: DevicePermissions.ReadProfile`, `Disabled: reading`), Schedule… (when not null), Delete. Read profile sends through `Dispatcher.SendAsync`, sets `reading` around it, snackbars `ReadProfileDone` on success, `ErrorPresenter.Notify(result.Errors)` on error. The schedules section goes in the view's `Entities` slot; after a saved schedule, `section.ReloadAsync()`. List view: two bulk actions, Read profile via `SerginBulk.RunAsync` + `outcome.Report(..., DeviceUiTexts.BulkReadProfileDone)`, and Schedule… with targets = selected rows' ids (`new ScheduledTaskTarget(item.Id, null)`) and labels = their `DeviceId`.

- [ ] **Step 1: Write the failing tests** (prerender with `factory.CreateClient()` as `ModulePageRenderingTests` does, dev user):
  - `DeviceDetail_ShowsReadProfileAndSchedule`: GET `/dm/devices/{id}` contains `Read profile`, `Schedule` and the schedules section's title.
  - `DeviceDetail_ShowsEngineOffLine`: contains the exact off-state copy (the stand-in is the real host's engine).
  - `EngineOn_HidesTheOffLine`: the probe page under `ScheduledTaskTestHost` with `FakeScheduledTaskEngine` (`FiresTriggers` true) does not contain it.
  - `DeviceDetail_WithoutReadProfilePermission_HidesBoth`: a seeded user with `devices.read` and `sys.scheduled-tasks.manage` only — neither action rendered (use the host's `DevUser` override pattern from `CurrentScopeTests` or `ScopedPageRenderingTests`).
- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement** as above.
- [ ] **Step 4: Run** the full suite, including `PseudoLocaleSweepTests` and `UiTextPlacementTests`. Expected: all pass.
- [ ] **Step 5: Commit** SharedKernel (`Scheduled tasks UI: say when scheduled runs are off`), then root (`Device pages: Read profile and Schedule actions`).

### Task 6: Dev seed, docs, headless browser check

**Files:**
- Modify: `docker-compose/.files/seed/dev-seed-users.sql` (every-run block: add `permission.dm.devices.read-profile` and `permission.sys.scheduled-tasks.manage` to every role holding `permission.dm.devices.update`, idempotent `INSERT ... WHERE NOT EXISTS`; `administrator` holds `permission.sys.platform-all` and needs nothing)
- Modify: `.claude/CLAUDE.md` (Scheduled tasks bullet: dm opts in, stand-in engine, host wired; migrations note: dm now has `InitialCreate` + `AddScheduledTasks`), `src/Modules/DeviceManagement/CLAUDE.md` (the slice, validator count if one was added), `src/SharedKernel/.claude/CLAUDE.md` (stand-in engine, `FiresTriggers`, `UseSerginTickerQ`, remove "no module opts in" and "headless check deferred")
- Create: `C:\Users\pspej\AppData\Local\Temp\claude\5eb5f13e14ff6214\afae4f35-bbd8-450a-8962-98571cf89876\scratchpad\read_profile_check.py` (not committed)

- [ ] **Step 1:** Seed edit; run it twice against a throwaway Postgres (`docker run postgres:17`, after the host migrated it) — second run inserts 0 rows.
- [ ] **Step 2: Browser check** (memory "Headless browser checks": Playwright-Python 1.46, chromium-1129, host on `:5555`, `DevUser` mode). Script steps, each asserted: open a device detail; click Read profile → button disabled, then the `Profile read for …` snackbar; click Schedule… → choose Daily, Save → the schedules section lists it and shows the off-state line; Pause → status Paused; Resume → Active; Delete → gone; Schedule… → Run now without saving → the latest one-off runs list shows Succeeded within 10 s. On the list page: select two rows → Read profile → `Profile read for 2 devices.`. Stop the host and remove temp files afterwards.
- [ ] **Step 3:** Docs as listed; `graphify update .` then the repair script, only if `graphify-out/graph.json` exists in the worktree.
- [ ] **Step 4: Run** the full suite once more. Expected: all pass, 3 skipped.
- [ ] **Step 5: Commit** SharedKernel docs, then root `Device read profile: seed grants, docs, browser check`.
