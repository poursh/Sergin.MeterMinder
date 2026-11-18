# Background Commands, Phase 1 (Core) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A command declared `Background()` is never run inline: a click starts a scheduled-task run that returns at once, the executor keeps long items alive with a heartbeat and ends them on a timeout, and Read profile is the first such command.

**Architecture:** Everything rides on the existing scheduled-tasks machinery in SharedKernel. `Background()` is a new command-configuration setting; `StartBackgroundCommand` writes a task-less run with the new trigger `Background` through `ScheduledRunStarter`; `ScheduledRunItemExecutor` gains a heartbeat (`IScheduledTaskStore.TouchRunItemAsync`) and a per-command timeout; a new first pipeline behavior refuses a background command sent outside a run item. Generated views start background commands instead of sending them.

**Tech Stack:** .NET 10, MediatR, FluentValidation, EF Core + Npgsql, Blazor Server + MudBlazor, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-18-background-commands-design.md` (calls 1–8 approved by the user on 2026-11-18). This plan covers the spec's plan (1): heartbeat, timeout, `Background()`, start, guard, Read profile. Directory, notifier, indicator, cancel and progress are phases 2 and 3.

## Global Constraints

- Work in a git worktree (CLAUDE.md "Work in a git worktree by default"); run `git submodule update --init --recursive` in it before building.
- SharedKernel is a submodule: commit SharedKernel changes on a branch inside `src/SharedKernel` and open a PR in Sergin.SharedKernel; the host repo commits the submodule bump plus its own changes (DeviceManagement configuration, tests, seed).
- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: the build must be clean. No `@code` blocks in `.razor` files.
- No commit carries a `Co-Authored-By: Claude` trailer (CLAUDE.md "Commit authorship").
- No migration in this phase: run trigger and item status are stored as strings with no CHECK, so the new enum value needs no schema change.
- Every new user-visible text is a declared `TextDefinition`, added to `tests/.../Commands/Types/catalog-keys.txt` and translated in the `fa` block of `docker-compose/.files/seed/dev-seed.sql`.
- Exact values from the spec: maximum background timeout **1 hour**; Read profile timeout **10 minutes**; heartbeat interval **`StaleAfter / 3`**; maximum targets per start **1000**; timeout error code **`ScheduledTasks.TimedOut`**, final (no retry); guard error code **`ScheduledTasks.MustRunInBackground`**, `ErrorType.Validation`.
- Deviations from the spec, decided here: the maximum timeout is a constant `BackgroundSettings.MaxTimeout` (not an option); the maximum target count is `ScheduledTaskOptions.MaxBackgroundTargets` (default 1000); start targets reuse `ScheduledTaskTarget(Guid TargetId, Guid? RootId)` — the spec's `Label` arrives with phase 2, which needs it.

## Review Focus

1. **Outer cancellation vs. timeout.** Host shutdown cancels the executor's own `ct`; that must still propagate as today (item stays Running, recovery picks it up), not be recorded as `TimedOut`. Test in Task 2.
2. **Heartbeat outliving the handler.** The heartbeat loop must stop before the executor saves the item's outcome; a late touch on a finished item must change nothing (`TouchRunItemAsync` only updates `Running` rows). Test in Task 2.
3. **Non-background commands untouched by the guard.** Every existing command still sends inline. Test in Task 3.
4. **Background start by a caller lacking the permission writes nothing.** No run row, no engine enqueue. Test in Task 4.
5. **Bulk start in a list with a mix of rows the caller cannot reach.** One run is written; unreachable targets fail as their own items (live grant check), the rest succeed. Test in Task 4.

---

### Task 1: Declare `Background()`

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/BackgroundSettings.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandConfigurationBuilder.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandSettings.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandConfigurationRegistry.cs:195-215`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/ScheduledCommand.cs`, `ScheduledCommandCatalog.cs` (`Add`)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/BackgroundDeclarationTests.cs`, test types in `ScheduledTaskTestTypes.cs`

**Interfaces:**
- Produces:
  - `public sealed record BackgroundSettings(TimeSpan? Timeout) { public static TimeSpan MaxTimeout { get; } = TimeSpan.FromHours(1); }`
  - `CommandConfigurationBuilder<TCommand>.Background(TimeSpan? timeout = null)` returning the builder; internal `CommandConfigurationBuilder.Background` (`BackgroundSettings?`).
  - `CommandSettings` gains trailing `BackgroundSettings? Background = null`.
  - `ScheduledCommand` gains trailing `BackgroundSettings? Background = null`.

- [ ] **Step 1: Write the failing tests** in `BackgroundDeclarationTests` (build the registry and catalog the way `ScheduledCommandCatalogTests` does, over test-only configurations on the `ScheduleProbe` root):
  - `Background_IsReadIntoSettingsAndCatalog`: a probe command configured `Schedulable("tst.probes.slow").Background(TimeSpan.FromMinutes(2))` → `registry.For(type).Background!.Timeout == TimeSpan.FromMinutes(2)` and `catalog.For(type)!.Background` equals it.
  - `Background_WithoutSchedulable_RefusesNamingTheRequest`: `Background()` alone → `InvalidOperationException` whose message contains the request's `FullName` and `Schedulable(`.
  - `Background_ZeroOrNegativeTimeout_Refuses` (theory: `0`, `-1` seconds) → message contains the request's `FullName` and `timeout`.
  - `Background_TimeoutAboveOneHour_Refuses`: `TimeSpan.FromMinutes(61)` → message contains the request's `FullName` and `1 hour`.
  - `NoBackground_LeavesSettingNull`: the existing `TouchProbeCommand` → `registry.For(...).Background` is null.

- [ ] **Step 2: Run them, expect FAIL** (compile error: `Background` not defined)
  Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter FullyQualifiedName~BackgroundDeclarationTests`

- [ ] **Step 3: Implement.** Builder stores the value only. The registry, where it already throws for a pointless `WithoutExpectedVersion()`, throws naming the configuration and request types for: `Background` set with `ScheduleName` null; `Timeout <= TimeSpan.Zero`; `Timeout > BackgroundSettings.MaxTimeout`. Pass `builder.Background` into `CommandSettings` (and make the `CommandSettings.None` shortcut false when it is set). `ScheduledCommandCatalog.Add` copies `settings.Background` onto `ScheduledCommand`.

- [ ] **Step 4: Run, expect PASS**; also run `--filter FullyQualifiedName~ScheduledTasks|FullyQualifiedName~Commands` to confirm nothing else moved.

- [ ] **Step 5: Commit** (SharedKernel branch, then host): `feat(scheduled-tasks): declare background commands`

---

### Task 2: Heartbeat and timeout in the executor

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/IScheduledTaskStore.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ScheduledTasks/EfScheduledTaskStore.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/ScheduledRunItemExecutor.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/ScheduledTaskErrors.cs`
- Modify: `src/SharedKernel/.claude/CLAUDE.md` ("Hosted services" bullet: drop the `StaleAfter` caveat, describe the heartbeat; "Runs" bullet: the timeout)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/RunItemHeartbeatTests.cs`; probe types in `ScheduledTaskTestTypes.cs`

**Interfaces:**
- Consumes: `ScheduledCommand.Background` (Task 1).
- Produces:
  - `Task<bool> IScheduledTaskStore.TouchRunItemAsync(Guid runItemId, DateTime now, CancellationToken ct)` — one `ExecuteUpdateAsync` setting `LastActivityAtUtc = now` where `Id == runItemId && Status == Running`; true when a row changed.
  - `ScheduledTaskErrors.TimedOutCode = "ScheduledTasks.TimedOut"`.
  - Test probes, reused by Tasks 3–4: `SlowProbeCommand(ScheduleProbeId Id) : ICommand<TouchProbeResponse>, ITransitionCommand<ScheduleProbe, ScheduleProbeId>, ISelectable`, configured `Schedulable("tst.probes.slow").Background().RequirePermissions(<the probe's touch permission>)`; its handler awaits a singleton `ProbeGate` (`Task WaitAsync(CancellationToken)`, `void Release()`, `int Entered`). `TimedProbeCommand(ScheduleProbeId Id)`, same shape, `Schedulable("tst.probes.timed").Background(TimeSpan.FromSeconds(1))`, handler `Task.Delay(TimeSpan.FromSeconds(30), ct)`.

- [ ] **Step 1: Write the failing tests** in `RunItemHeartbeatTests` (host from `ScheduledTaskTestHost.Create` with the fake engine as `RunItemExecutionTests` does, the probe handlers and `ProbeGate` registered, and `Sergin:ScheduledTasks` set to `StaleAfter=00:00:03`, `RecoveryInterval=00:00:01`, `RetryIntervalsSeconds=[1]`):
  - `LongItem_IsNotResentByRecovery`: start a run of `SlowProbeCommand` on one probe; `drain = Engine.DrainAsync(host.Services)` not awaited; wait until `gate.Entered == 1`; `await Task.Delay(5s)`; `await recovery.RecoverOnceAsync(default)` returns `0`; `gate.Release()`; await `drain`. Assert item `Succeeded`, `Attempts == 1`, `gate.Entered == 1`.
  - `Timeout_FailsTheItemWithoutRetry`: run of `TimedProbeCommand`; drain. Assert item `Failed`, `ErrorCode == "ScheduledTasks.TimedOut"`, `Attempts == 1`; run `Completed`.
  - `Touch_OnAFinishedItem_ChangesNothing`: after a succeeded item, `store.TouchRunItemAsync(itemId, later, ct)` returns false and `LastActivityAtUtc` is unchanged.
  - `HostShutdown_IsNotATimeout`: run of `SlowProbeCommand`; call `executor.ExecuteAsync(schema, itemId, cts.Token)` directly and cancel `cts` after `gate.Entered == 1` → `OperationCanceledException` propagates; item stays `Running` with `ErrorCode` null.

- [ ] **Step 2: Run, expect FAIL** (`TouchRunItemAsync` missing).
  Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter FullyQualifiedName~RunItemHeartbeatTests`

- [ ] **Step 3: Implement.** In `ExecuteAsync`, around `SendAsync`:
  - `using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct)`; a heartbeat task that runs a `PeriodicTimer(settings.StaleAfter / 3)` and on each tick opens its **own** async scope, resolves `IScheduledTaskStoreResolver.For(schema)` and calls `TouchRunItemAsync(item.Id, clock.UtcNow, …)` (never the executor's store: that context is not thread-safe). Swallow and log a failed touch; stop on `stop` cancellation.
  - `using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct)`; `timeout.CancelAfter(command.Background.Timeout)` when set; pass `timeout.Token` to `SendAsync`.
  - Cancel and await the heartbeat in a `finally` **before** the outcome is saved.
  - Catch `OperationCanceledException` when `timeout.IsCancellationRequested && !ct.IsCancellationRequested`: `item.MarkFailed(ScheduledTaskErrors.TimedOutCode, "The command ran longer than its timeout of {timeout}.", now)`, save, complete the run; no retry. Any other `OperationCanceledException` propagates as today.
  - `SendAsync` needs the `ScheduledCommand` it already looks up; hoist the catalogue lookup so `ExecuteAsync` has it for the timeout.

- [ ] **Step 4: Run, expect PASS**; then the whole `ScheduledTasks` folder (`--filter FullyQualifiedName~ScheduledTasks`).

- [ ] **Step 5: Commit**: `feat(scheduled-tasks): heartbeat run items and time out background commands`

---

### Task 3: Refuse a background command sent inline

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/IRunItemContext.cs` (interface + internal `RunItemContext`)
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/BackgroundCommandGuardBehavior.cs`
- Modify: `ScheduledRunItemExecutor.cs` (`SendAsync` seeds the context), `ScheduledTaskErrors.cs`, `ScheduledTaskTexts.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs:105-112` (register the behavior **first**, and `RunItemContext` scoped)
- Modify: `tests/.../Commands/Types/catalog-keys.txt`, `docker-compose/.files/seed/dev-seed.sql` (`fa` row)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/BackgroundGuardTests.cs`

**Interfaces:**
- Produces:
  - `public interface IRunItemContext { Guid? RunItemId { get; } }`; internal `RunItemContext : IRunItemContext` with `void Set(Guid runItemId)`; both registered scoped (`IRunItemContext` forwards to the same instance).
  - `ScheduledTaskTexts.MustRunInBackground` = `errors.Text("MustRunInBackground", "This action runs in the background. Start it instead of sending it.")`.
  - `ScheduledTaskErrors.MustRunInBackground` = `Error.Validation("ScheduledTasks.MustRunInBackground", <the text's default>, ErrorText.Of(ScheduledTaskTexts.MustRunInBackground.Key.Value))`.
  - `BackgroundCommandGuardBehavior<TRequest, TResponse>`: when `registry.For(typeof(TRequest)).Background is not null && runItem.RunItemId is null`, return the error the way `PermissionCheckPipelineBehavior` returns `Error.Forbidden()`; otherwise `next()`.

- [ ] **Step 1: Write the failing tests** in `BackgroundGuardTests` (same host as Task 2):
  - `Inline_IsRefused`: `ISerginDispatcher.SendAsync(new SlowProbeCommand(id))` from a scope → `FirstError.Code == "ScheduledTasks.MustRunInBackground"`, `Type == Validation`; `gate.Entered == 0`.
  - `InsideARunItem_Passes`: a run of `SlowProbeCommand`, `gate.Release()` up front, drain → item `Succeeded`.
  - `OtherCommands_StillSendInline`: `TouchProbeCommand` through `SendAsync` succeeds.

- [ ] **Step 2: Run, expect FAIL** (first test: command runs inline).

- [ ] **Step 3: Implement** as in Interfaces. Add the key to `catalog-keys.txt` (sorted position) and its `fa` translation to the seed's `fa` block.

- [ ] **Step 4: Run, expect PASS**: this test class, `RunItemHeartbeatTests`, `Commands/Types/MigrationPinTests`, `Localization/` (text-catalog tests).

- [ ] **Step 5: Commit**: `feat(scheduled-tasks): refuse background commands sent inline`

---

### Task 4: `StartBackgroundCommand`

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/Commands/StartBackgroundCommand.cs` (record, response, `BackgroundRunRef`, validator, handler — the shape of `RunCommandOnceCommand.cs`)
- Modify: `Commands/ScheduledTaskCommandConfigurations.cs` (add `StartBackgroundCommandConfiguration`), `ScheduledTaskConfigurationTypes.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Domain/ScheduledTasks/ScheduledTaskRun.cs` (`ScheduledRunTrigger.Background`)
- Modify: `ScheduledTaskOptions.cs` (+ `MaxBackgroundTargets`, validated `>= 1`), `ScheduledTaskTexts.cs` (+ `NotBackground`), `ScheduledTaskErrors.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ScheduledTasks/ScheduledTaskServiceExtensions.cs` (register the handler and validator as the other management commands are)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ScheduledTasks/ScheduledTaskQueries.cs:251` (one-off runs read: `r."trigger" IN ('OneOff','Background')`)
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/SerginDispatcherBackgroundExtensions.cs`
- Modify: `tests/.../ScheduledTasks/ScheduledTaskTestHost.cs:75` (`FromTypesInSchema(ScheduledTasksDbContext.Schema, ProbeConfigurations)`, so probe commands carry their schema), catalog keys, `fa` seed
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/BackgroundStartTests.cs`

**Interfaces:**
- Consumes: `ScheduledCommand.Background` (Task 1); `ScheduledTaskAuthority.EnsureMayScheduleAsync(Type, ct)` (checks the command's own permissions only — the manage permission comes from `RunCommandOnceCommand`'s configuration, so reuse it unchanged); `ScheduledRunStarter.StartAsync(store, ScheduledRunStart, ct)`; `ScheduledCommandFactory.Build` / `ToArguments`.
- Produces:
  - `public sealed record BackgroundRunRef(string Schema, Guid RunId);`
  - `public sealed record StartBackgroundCommand(IBaseCommand Template, IReadOnlyCollection<ScheduledTaskTarget> Targets) : ICommand<BackgroundRunStartedResponse>;`
  - `public sealed record BackgroundRunStartedResponse(BackgroundRunRef Run);`
  - `ScheduledTaskTexts.NotBackground` = `errors.Text("NotBackground", "This command does not run in the background.")`.
  - `public static Task<ErrorOr<BackgroundRunStartedResponse>> StartAsync(this ISerginDispatcher dispatcher, IBaseCommand template, IReadOnlyCollection<ScheduledTaskTarget> targets, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests** in `BackgroundStartTests` (Task 2's host; callers from `RbacScenario`):
  - `HolderOfTheCommandPermission_WithoutManage_Starts`: caller granted only the probe permission on North; start `SlowProbeCommand` on one Feeder1 probe → success; run row: `TaskId` null, `Trigger == Background`, `ActingUserId` = caller, `CommandName == "tst.probes.slow"`; `response.Run.Schema == "tst"`.
  - `WithoutTheCommandPermission_IsForbidden_AndWritesNothing`: → `ErrorType.Forbidden`; no run row; `Engine` enqueued nothing.
  - `ValidatorError_RefusesAtStart_AndWritesNothing`: a background probe whose validator refuses (reuse `TouchProbeCommandValidator`'s "invalid" rule on a `SlowProbeCommand` note, or add `Note` to the slow probe) → `ErrorType.Validation` with the property name as code; no run row.
  - `NotBackground_IsRefused`: template `TouchProbeCommand` → `FirstError.Code == nameof(StartBackgroundCommand.Template)`, description = `NotBackground` default.
  - `SeveralTargets_OneRun_NItems`: three probes → one run, three items `Pending`, each target once.
  - `UnreachableTargets_FailAlone`: caller on North, targets one Feeder1 probe and one South probe; drain → Feeder1 item `Succeeded`, South item `Failed` with `NotFound` (live reach check); run `Completed`.
  - `TooManyTargets_IsRefused`: `MaxBackgroundTargets` set to 2, three targets → validation error on `Targets`.

- [ ] **Step 2: Run, expect FAIL** (types missing).

- [ ] **Step 3: Implement.**
  - Configuration: no permission; `NotShown` on both properties (untyped, as the other management commands).
  - Validator: `ScheduledTaskRules.TemplateAndTargets(this, catalog, r => catalog.For(r.Template.GetType())?.Schema ?? string.Empty, r => r.Template, r => r.Targets)`, plus: catalogued command's `Background` is null → `NotBackground` on `Template`; its `Schema` is null → `NotSchedulable` on `Template`; `Targets.Count > options.MaxBackgroundTargets` → `TooManyTargets` on `Targets`.
  - Handler: `authority.EnsureMayScheduleAsync(command.RequestType)`; then for each target build the command with `ScheduledCommandFactory.Build(command, ToArguments(template), target.TargetId, target.RootId)`, resolve `IValidator<>` of its runtime type from the request scope, validate, and collect failures mapped exactly as `ValidationPipelineBehavior` maps them (extract that mapping to one internal static helper both use); any error → return them all, nothing written. Then the same start as `RunCommandOnceCommandHandler` with `ScheduledRunTrigger.Background` and `stores.For(command.Schema)`.
  - Texts: keys in `catalog-keys.txt`, `fa` rows in the seed.

- [ ] **Step 4: Run, expect PASS**; then the `ScheduledTasks`, `Commands`, `Localization` folders.

- [ ] **Step 5: Commit**: `feat(scheduled-tasks): start background commands`

---

### Task 5: Read profile runs in the background

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Configurations/Devices/Commands/ReadProfile/ReadDeviceProfileCommandConfiguration.cs`
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Devices/ReadDeviceProfileTests.cs`
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/BackgroundRuns.cs` (test helper)
- Modify: `src/Modules/DeviceManagement/.claude/CLAUDE.md` (Read profile is a background command), host `.claude/CLAUDE.md` (Scheduled tasks bullet: one sentence on `Background()` and the test-folder list: the four new test classes)

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces: `internal static class BackgroundRuns { public static Task<(string Status, IReadOnlyList<(string Status, string? ErrorCode)> Items)> WaitAsync(IServiceProvider services, BackgroundRunRef run, TimeSpan timeout); }` — polls `"{schema}".scheduled_task_runs`/`_items` every 200 ms through `IDbConnectionFactory` until the run's status is not `Running`; throws `TimeoutException` naming the run.

- [ ] **Step 1: Rewrite the tests** in `ReadDeviceProfileTests`:
  - `ReadProfile_SentInline_IsRefused`: `SendAsync(new ReadDeviceProfileCommand(id))` → `"ScheduledTasks.MustRunInBackground"`.
  - `ReadProfile_Started_SucceedsInTheBackground`: `StartAsync(template, [new(id, null)])` returns in under 1 s; `BackgroundRuns.WaitAsync(…, 30 s)` → run `Completed`, item `Succeeded`.
  - `ReadProfile_LeavesTheRowVersionAlone`: as today, around a started and finished run.
  - `ReadProfile_OnADeletedDevice_FailsAsNotFound`: item `Failed`, `ErrorCode` is the not-found code.
  - `ReadProfile_WithoutThePermission_IsForbiddenAtStart`: `RbacScenario.Read` caller → `Forbidden`, no run.
  - Keep `ReadProfile_IsSchedulable`; add `Background!.Timeout == TimeSpan.FromMinutes(10)`.

- [ ] **Step 2: Run, expect FAIL** (inline send still succeeds).

- [ ] **Step 3: Implement**: `builder.Schedulable("dm.devices.read-profile").Background(TimeSpan.FromMinutes(10)).RequirePermissions(DevicePermissions.ReadProfile).WithoutExpectedVersion();`

- [ ] **Step 4: Run, expect PASS**: `--filter FullyQualifiedName~ReadDeviceProfileTests|FullyQualifiedName~DeviceReadProfileScheduleTests|FullyQualifiedName~Aggregates`.

- [ ] **Step 5: Commit** (host repo, with the SharedKernel bump): `feat(device-management): read profile runs in the background`. Between this commit and Task 6 the Read profile button is refused by the guard; land both in one PR.

---

### Task 6: Generated views start background commands

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Model/CommandView.cs` (+ `BackgroundSettings? Background = null`, trailing optional)
- Modify: `Aggregates/AggregateViewModelJoin.cs:~479` (copy `settings.Background`)
- Modify: `Aggregates/Views/Parts/GeneratedViewSupport.cs` (`SendFromIdAsync` branch; new `StartManyAsync`)
- Modify: `Aggregates/Views/SerginGeneratedDetailView.razor.cs:291-306` and `Aggregates/Views/Parts/GeneratedListBody.razor.cs:379-385` (bulk)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Localizations/ShellTexts.cs` (`CommandActions`), catalog keys, `fa` seed
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/GeneratedBackgroundCommandTests.cs`

**Interfaces:**
- Consumes: `StartAsync` extension, `BackgroundRunStartedResponse` (Task 4); Read profile declared `Background` (Task 5).
- Produces:
  - `ShellTexts.CommandActions.BackgroundStarted` = `category.Text("BackgroundStarted", "Started. It keeps running in the background.")`; `BackgroundStartedMany` = `category.Text("BackgroundStartedMany", "Started for {Count} records. It keeps running in the background.")`.
  - `GeneratedViewSupport.SendFromIdAsync(...)`: when `command.Background is not null`, dispatches `StartAsync(command.BuildFromId!(id), [new(id, null)])` (child commands: `BuildFromIds` with `RootId`) and returns `ViewSendResult(response, null, errors)` — no version is sent or read.
  - `internal static Task<ViewSendResult> StartManyAsync(ISerginDispatcher dispatcher, CommandView command, IReadOnlyCollection<ScheduledTaskTarget> targets)`.

- [ ] **Step 1: Write the failing tests** in `GeneratedBackgroundCommandTests` (registry from the real host, as `GeneratedDeviceViewTests` reads it; dispatcher from a scope):
  - `ReadProfile_CommandView_IsBackground`: Device model's Read profile `CommandView.Background` is not null.
  - `SendFromId_OnABackgroundCommand_StartsARun`: `GeneratedViewSupport.SendFromIdAsync(dispatcher, readProfile, deviceId, version: null)` → not error, `Value` is `BackgroundRunStartedResponse` with `Run.Schema == "dm"`; a `dm.scheduled_task_runs` row with `trigger = 'Background'` exists.
  - `StartMany_OneRunForAllRows`: three devices → one run, three items.

- [ ] **Step 2: Run, expect FAIL** (`CommandView.Background` missing).

- [ ] **Step 3: Implement.**
  - Detail `RunAsync`: on success of a background command, snackbar `BackgroundStarted` (`Severity.Info`) instead of `SucceededText`, and do **not** reload the record (nothing changed yet).
  - List row action: same path through `SendFromIdAsync`.
  - List bulk `RunSelectedAsync`: when `command.Background is not null`, one `StartManyAsync` with every selected row's target instead of `SerginBulk.RunAsync`; on success snackbar `BackgroundStartedMany` with `Count` formatted by `ICultureFormatter`; on refusal `ErrorPresenter.Notify(result.Errors)`. Clear the selection either way, as today.

- [ ] **Step 4: Run, expect PASS**; then `Aggregates/`, `Shell/`, `Localization/PseudoLocaleSweepTests`.

---

- [ ] **Step 5: Run the full suite**: `dotnet build Sergin.MeterMinder.slnx` (0 warnings), then `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` (Docker Desktop running). Expected: all pass, the TickerQ-licence tests skipped as before.

- [ ] **Step 6: Headless check** (Playwright, see memory "Headless browser checks"): on a device detail page click Read profile → info snackbar within 1 s, page stays interactive; the record's schedule section lists the run under recent one-off runs and it reaches Succeeded after about 3 s; on the device list, select three rows, bulk Read profile → one "Started for 3 records" snackbar.

- [ ] **Step 7: Commit** (SharedKernel views change, then the host bump): `feat(views): start background commands from generated views`
