# Background Commands, Phase 3 (Cancel and Progress) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A person can cancel their own background, run-now or one-off run, and can watch a running item's progress, from the app-bar activity indicator and from the record's schedule section. Cancellation works when the item runs in another process.

**Architecture:**
- **Cancel state lives in the database.**
  - A run gets `cancel_requested_at_utc` and a `cancelled` count, and an item gets the status `Cancelled`.
  - Cancelling stamps the run and moves its Pending and Retrying items to Cancelled in one store call.
  - A Running item learns of the cancel from its heartbeat, which now also reads the run's cancel stamp. The executor then cancels the command's token.
  - Claim and recovery refuse items of a cancelled run, so a late delivery sends nothing.
- **Progress is a per-item column.**
  - It is written through a scoped `IRunProgress` that is a no-op outside a run item.
  - Writes are throttled per item, and the last value is flushed when the attempt ends.
- **The indicator shows it all.**
  - It gains a Cancel button, a cancelled outcome, a determinate progress bar for a one-record run, and the module's caption.
  - A circuit-scoped `ActivityPanelState` shares its rows, so a generated detail view can show a spinner on an action whose run is still going.

**Tech Stack:** .NET 10, MediatR, EF Core + Npgsql + Dapper, Blazor Server + MudBlazor, xUnit + Testcontainers, Playwright (headless check).

**Spec:** `docs/superpowers/specs/2026-11-18-background-commands-design.md`, sections "Running: heartbeat, timeout, progress" (Progress), "Cancelling", Storage (the cancel and progress columns), "Names and links" (Module), "UI → Starting from a generated view" (the spinner), "UI → The activity indicator" (Cancel, progress bar, caption), "UI → The record's own section". Phases 1 and 2 are merged (SharedKernel #49 and #50, host #65 and #66).

## Global Constraints

- Work in the worktree `.claude/worktrees/background-commands-3`, on branch `feat/background-commands-3` in the host repo and in `src/SharedKernel`.
  - SharedKernel changes are committed inside the submodule.
  - The host commit carries the tests, migrations, seed and the submodule bump.
  - Never push. No `Co-Authored-By` trailer in commits.
- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: the build has zero warnings.
  - No `@code` blocks in `.razor` files. File-scoped namespaces.
  - No `using` for a namespace already imported globally. IDE0005 is not gated by the build, so check by hand.
- **Texts.** Every user-visible text is a declared `TextDefinition`.
  - Its key goes in `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/Types/catalog-keys.txt` in sorted order.
  - Each gets a Persian row in the `fa` block of `docker-compose/.files/seed/dev-seed.sql`.
  - The pseudo-locale sweep must stay green.
- **Who may cancel:** the run's acting user, or a holder of `permission.sys.scheduled-tasks.manage` (`ScheduledTaskPermissions.Manage`) on the run's scope (`run.ScopeId`).
- **Cancel reaches a Running item on its next heartbeat.** The heartbeat interval is `min(StaleAfter / 3, Sergin:ScheduledTasks:HeartbeatInterval)`, with `HeartbeatInterval` defaulting to **5 seconds**.
- **Cancel is final.** A cancelled item is never retried. A Cancelled outcome beats a timeout, but a host shutdown still propagates and leaves the item for recovery, as in phase 1.
- **Progress:**
  - It is an integer **0–100**; out-of-range values are clamped.
  - At most **one write per 2 seconds** per item, and the last reported value is always written when the attempt ends.
  - Outside a run item it is a no-op.
  - It holds a percent only: no message key or arguments column.
- **Migrations.** Two DeviceManagement migrations, each hand-converted to a file-scoped namespace (CLAUDE.md "A freshly scaffolded migration does not build as scaffolded"):
  - `AddRunCancellation`:
    - `scheduled_task_runs.cancel_requested_at_utc timestamptz NULL`;
    - `scheduled_task_runs.cancelled integer NOT NULL DEFAULT 0`.
  - `AddRunItemProgress`: `scheduled_task_run_items.progress smallint NULL`.
- The finished-run window (24 h), the 50-per-source cap, the 5 s poll and the 1 s notifier floor from phase 2 are unchanged.

## Review Focus

1. **A cancel reaches an item that runs in another executor scope.** It ends `Cancelled` within one heartbeat interval: not Failed, not retried. Pinned in Task 2 (`Cancel_ReachesARunningItem_OnItsNextHeartbeat`).
2. **A cancel racing the item's own finish leaves one outcome.** The item ends Succeeded or Cancelled, never both, and the run completes once with `Succeeded + Failed + Cancelled = Total`. Pinned in Task 2 (`CancelAfterTheSendFinished_KeepsTheOutcome`).
3. **A cancelled run's item whose process died is never re-sent.** Recovery cancels it and completes the run. Pinned in Task 2 (`Recovery_CancelsAnAbandonedItemOfACancelledRun`).
4. **Nobody else cancels.** Another user without Manage is refused with Forbidden, and nothing changes. A Manage holder on a scope that does not reach the run is refused too. Pinned in Task 1.
5. **Cancelling a finished run changes nothing.** It is refused as `ScheduledTasks.RunFinished` (Conflict), and the indicator reloads rather than showing an error panel. Pinned in Tasks 1 and 4.

---

### Task 1: Cancel a run: domain, store, migration, command

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Domain/ScheduledTasks/ScheduledTaskRun.cs`, `ScheduledTaskRunItem.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/IScheduledTaskStore.cs`, `IScheduledTaskStoreResolver.cs`, `ScheduledTaskErrors.cs`, `ScheduledTaskTexts.cs`, `Commands/ScheduledTaskCommandConfigurations.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/Commands/CancelBackgroundRunCommand.cs` (record, handler)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ScheduledTasks/EfScheduledTaskStore.cs`, `ScheduledTaskStoreResolver.cs`, `ScheduledTasksModelBuilderExtensions.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Migrations/<ts>_AddRunCancellation.cs` (+ Designer, snapshot)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/CancelRunTests.cs` (new), `ScheduledTaskModelTests.cs`, `DeviceManagementScheduledTasksModelTests.cs`, `RunNotifierTests.cs`

**Interfaces:**
- Produces:
  - `ScheduledRunItemStatus.Cancelled` (appended last) and `ScheduledTaskRunItem.MarkCancelled(DateTime at)`. It sets `CompletedAtUtc` and clears no error.
  - `ScheduledTaskRun.CancelRequestedAtUtc` (`DateTime?`), `ScheduledTaskRun.Cancelled` (`int`), and `ScheduledTaskRun.Complete(int succeeded, int failed, int cancelled, DateTime at)`, which replaces the two-count overload.
  - `IScheduledTaskStore`:
    - `Task<ScheduledTaskRun?> GetRunAsync(Guid runId, CancellationToken ct)`: the run, tracked.
    - `Task CancelRunAsync(Guid runId, DateTime at, CancellationToken ct)`: under the same row lock as `CompleteRunIfFinishedAsync`, it stamps `cancel_requested_at_utc` if it is null and the run is Running, and moves every Pending or Retrying item to Cancelled (`completed_at_utc = at`). It commits.
  - `CompleteRunIfFinishedAsync` counts `Cancelled` as finished and passes the cancelled count.
  - `TryClaimRunItemAsync` refuses an item whose run has `cancel_requested_at_utc` set.
  - `IScheduledTaskStoreResolver.TryFor(string schema, out IScheduledTaskStore? store)`: false for a schema no module opted into.
  - `public sealed record CancelBackgroundRunCommand(string Schema, Guid RunId) : ICommand;` (untyped; its configuration requires no permission and marks `Schema` and `RunId` `NotShown`).
  - `ScheduledTaskErrors.RunFinished`: `Error.Conflict("ScheduledTasks.RunFinished", <declared text>, ErrorText.Of(...))`, with `ScheduledTaskTexts.RunFinished` = "This run has already finished."
  - The handler publishes `RunChanged` after the cancel commits.

- [ ] **Step 1: Write the failing tests in `CancelRunTests`.** Runs are started through `ISerginDispatcher.StartAsync` with the test-only background probe from phase 1 (`GeneratedBackgroundCommandTests` / `BackgroundStartTests` show how), and the engine is held so items stay Pending.
  - `ActingUser_CancelsPendingItems`: after cancel, every item is `Cancelled`, the run is `Completed` with `Cancelled == Total`, `Succeeded == 0`, and `CancelRequestedAtUtc` is set.
  - `ManageHolderOnTheRunsScope_MayCancel` / `ManageHolderElsewhere_IsForbidden`: the second asserts `ErrorType.Forbidden` and the items are still Pending.
  - `AnotherUserWithoutManage_IsForbidden`: `ErrorType.Forbidden`; nothing changes.
  - `UnknownRunOrSchema_IsNotFound`: `ErrorType.NotFound` for both.
  - `FinishedRun_IsRunFinished`: code `ScheduledTasks.RunFinished`, and the run's counts are unchanged.
  - `ALateDeliveryOfACancelledItem_SendsNothing`: call the executor for a cancelled item; the probe's handler counter stays 0.
  - `Cancel_PublishesRunChanged` (in `RunNotifierTests`): the acting user's subscriber hears the run ref.
- [ ] **Step 2: Run** `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~CancelRunTests|FullyQualifiedName~RunNotifierTests"`. Expected: compile failure, because `CancelBackgroundRunCommand` is not defined.
- [ ] **Step 3: Domain and store.**
  - Implement the members above.
  - Map `cancel_requested_at_utc` and `cancelled` (default 0) in `ScheduledTasksModelBuilderExtensions`. The status column already stores the enum name, so `Cancelled` needs no schema change.
  - Extend the model tests to assert both columns, their nullability and the default.
- [ ] **Step 4: Migration `AddRunCancellation`** (DeviceManagement).
  - Scaffold with the CLAUDE.md `dotnet ef migrations add` command.
  - Convert the `.cs` file to a file-scoped namespace.
  - Leave the Designer and snapshot files as generated.
- [ ] **Step 5: Command and handler.**
  - Resolve the store with `TryFor`; an unknown schema answers NotFound, and so does `GetRunAsync` returning null.
  - Authorize: allowed when `run.ActingUserId == caller.Id`, or `auth.CurrentUserHasAsync(ScheduledTaskPermissions.Manage, run.ScopeId, ct)`; otherwise `Error.Forbidden()`.
  - A run whose `Status != Running` answers `RunFinished`.
  - Then call `CancelRunAsync`, then `CompleteRunIfFinishedAsync`, then publish.
  - Add `RunFinished` to the texts, the catalog keys and the `fa` seed.
- [ ] **Step 6: Run** the Step 2 filter, then the `ScheduledTasks` folder. Expected: PASS, with 0 build warnings.
- [ ] **Step 7: Commit** the SharedKernel change, then the host change: `feat(scheduled-tasks): cancel a background run`.

### Task 2: Cancel reaches a running item; recovery honours it

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/ScheduledRunItemExecutor.cs`, `ScheduledTaskOptions.cs` (+ its validator), `IScheduledTaskStore.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ScheduledTasks/EfScheduledTaskStore.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/ScheduledTasks/ScheduledRunItemRecoveryService.cs`
- Test: `tests/.../ScheduledTasks/RunItemCancelTests.cs` (new), `RunItemRecoveryTests.cs`, `ScheduledTaskOptionsBindingTests.cs`, `RunItemHeartbeatTests.cs`

**Interfaces:**
- Consumes: Task 1's `CancelRunAsync`, `MarkCancelled`, `Cancelled` status, and `CompleteRunIfFinishedAsync` counting cancelled items.
- Produces:
  - `ScheduledTaskOptions.HeartbeatInterval` (`TimeSpan`, default `TimeSpan.FromSeconds(5)`). It must be positive; the validator names `Sergin:ScheduledTasks:HeartbeatInterval`. The executor ticks every `min(StaleAfter / 3, HeartbeatInterval)`.
  - `IScheduledTaskStore.TouchRunItemAsync(Guid runItemId, DateTime now, CancellationToken ct)` now returns `Task<RunItemHeartbeat>`, with `public sealed record RunItemHeartbeat(bool Touched, bool CancelRequested);`. It is one statement: the UPDATE plus a read of the run's `cancel_requested_at_utc` (an `UPDATE … RETURNING` through a join, or an update followed by a read in the same transaction; implementer's choice).
  - `IScheduledTaskStore.ListStaleRunItemsAsync` leaves out items of a cancel-requested run.
  - New `Task<IReadOnlyCollection<Guid>> CancelAbandonedRunItemsAsync(DateTime quietSince, DateTime at, CancellationToken ct)`. It moves each unfinished item of a cancel-requested run that has been quiet since before `quietSince` to Cancelled, and answers the distinct run ids it touched.
  - The executor's rules:
    - **Before the send:** after the claim, if `run.CancelRequestedAtUtc` is set (a cancel landed between the claim and the read), the item is `MarkCancelled` without sending.
    - **During the send:** a heartbeat that answers `CancelRequested` cancels a `cancelSource` linked into the send token.
    - **After the send:**
      - If `cancelSource` fired and `ct` did not, the outcome is Cancelled, final, and beats a timeout.
      - A send that returned before the cancel fired keeps its own outcome.
    - Publish after the save, as today.
  - The recovery pass calls `CancelAbandonedRunItemsAsync`, completes each run it returns, and publishes `RunChanged` for each. The recovery service gains `IRunNotifier`.

- [ ] **Step 1: Write the failing tests.** Use a test-only background probe whose handler awaits its token until cancelled, with `HeartbeatInterval` = 200 ms in the test host.
  - `Cancel_ReachesARunningItem_OnItsNextHeartbeat`: start the run, wait until the item is Running, then send `CancelBackgroundRunCommand` from a second scope (standing in for another process). Within 2 s the item is `Cancelled`, the run is Completed with `Cancelled == 1`, and `Attempts == 1` (no retry).
  - `CancelBeatsTimeout`: a probe with `Background(TimeSpan.FromSeconds(1))` and a cancel at 300 ms ends `Cancelled`, not `TimedOut`.
  - `CancelAfterTheSendFinished_KeepsTheOutcome`: a probe that finishes at once. A cancel sent after the item is Succeeded answers `RunFinished`, and the item stays Succeeded.
  - `CancelBetweenClaimAndRead_SendsNothing`: stamp the cancel directly, then call the executor's path with the item already claimed. Use a store seam or call `CancelRunAsync` before `ExecuteAsync` while forcing the item Running. Nothing is sent and the item is `Cancelled`.
  - `Recovery_CancelsAnAbandonedItemOfACancelledRun` (in `RunItemRecoveryTests`): an item left Running and quiet, with the run cancel-requested. One `RecoverOnceAsync` makes it Cancelled, completes the run, and enqueues no job.
  - `HeartbeatInterval_IsBoundAndValidated` (in `ScheduledTaskOptionsBindingTests`): 0 is refused with a message naming the key.
  - Update `RunItemHeartbeatTests` for the new `TouchRunItemAsync` result.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~RunItemCancelTests|FullyQualifiedName~RunItemRecoveryTests|FullyQualifiedName~RunItemHeartbeatTests|FullyQualifiedName~ScheduledTaskOptionsBindingTests"`. Expected: FAIL. The Cancel tests time out with the item still Running.
- [ ] **Step 3: Implement** the store members, the option, the executor rules and the recovery step listed in Interfaces.
- [ ] **Step 4: Run** the Step 2 filter, then the `ScheduledTasks` folder. Expected: PASS.
- [ ] **Step 5: Commit**: `feat(scheduled-tasks): cancel reaches a running item`.

### Task 3: Progress

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/IRunProgress.cs` (interface plus the internal `RunProgress`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Domain/ScheduledTasks/ScheduledTaskRunItem.cs` (`int? Progress`, `SetProgress(int percent)`)
- Modify: `IScheduledTaskStore.cs`, `EfScheduledTaskStore.cs`, `ScheduledTasksModelBuilderExtensions.cs`, `ScheduledRunItemExecutor.cs`, the scheduled-task service registration (scoped `IRunProgress`)
- Modify: `Activity/ActivityItem.cs` (+ `int? Progress`, set for a one-item run, and `int Cancelled`), `EFCore/ScheduledTasks/ScheduledTaskQueries.cs` (activity SQL; `RunItemsAsync` gains `Progress`), `Queries/ScheduledTaskResponses.cs` (`ScheduledRunItemListItem` + `int? Progress`; `ScheduledRunListItem` + `int Cancelled`)
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/ReadProfile/ReadDeviceProfileCommandHandler.cs` (reports 33, 66, 100 across its three 1-second steps)
- Create: `src/Modules/DeviceManagement/.../Migrations/<ts>_AddRunItemProgress.cs` (+ Designer, snapshot)
- Test: `tests/.../ScheduledTasks/RunProgressTests.cs` (new), `ActivityDirectoryTests.cs`, the model tests, `Devices/ReadDeviceProfileTests.cs`

**Interfaces:**
- Produces:
  - `public interface IRunProgress { ValueTask ReportAsync(int percent, CancellationToken ct = default); }`. It is scoped:
    - Outside a run item (`IRunItemContext.RunItemId` is null) it does nothing.
    - Inside one it clamps the value to 0–100 and keeps it as the pending value.
    - It writes through its own scope and store when 2 s have passed since its last write, or on its first call, and then publishes `RunChanged`.
  - `IScheduledTaskStore.SetRunItemProgressAsync(Guid runItemId, int percent, DateTime now, CancellationToken ct)`: an `ExecuteUpdate` of `progress` and `last_activity_at_utc` while the item is Running.
  - **The flush.** The executor reads `RunProgress`'s pending value from the send scope once the send ends. It puts that value on the tracked item before the outcome save, so the last report is always kept. To do that, the send scope's `RunProgress` hands its last value back through `RunItemContext`.
  - `ActivityItem` gains `int Cancelled` and `int? Progress`, appended after `FirstErrorDescription` in that order. `ScheduledRunListItem` gains `int Cancelled`, and `ScheduledRunItemListItem` gains `int? Progress`.

- [ ] **Step 1: Write the failing tests in `RunProgressTests`.** Use a test-only background probe that reports through `IRunProgress`.
  - `Report_OutsideARunItem_DoesNothing`: resolve `IRunProgress` from a plain scope and report 50; no exception, nothing written.
  - `Report_IsClampedAndStored`: a probe reports 150; the stored progress is 100.
  - `Reports_AreThrottled_AndTheLastIsKept`: a probe reports 10, 20 and 30 within 100 ms, then returns. One mid-run write happened (10), and the finished item's progress is 30.
  - `Report_PublishesRunChanged`: the subscriber hears it before the item finishes.
  - In `ActivityDirectoryTests`, `OneItemRun_CarriesProgress_ManyItemRun_DoesNot` and `CancelledCount_IsRead`.
  - In `ReadDeviceProfileTests`, a finished Read profile item has progress 100.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~RunProgressTests|FullyQualifiedName~ActivityDirectoryTests|FullyQualifiedName~ReadDeviceProfileTests"`. Expected: compile failure, because `IRunProgress` is not defined.
- [ ] **Step 3: Implement.**
  - The column, the store method, `IRunProgress` with its throttle and flush, and the scoped registration.
  - The `ActivityItem` and query changes: the activity lateral selects `i.progress`; the cancelled count comes from `r.cancelled`.
  - Read profile's three steps.
  - Migration `AddRunItemProgress`, file-scoped, with the model tests extended.
- [ ] **Step 4: Run** the Step 2 filter, then the `ScheduledTasks` and `Devices` folders. Expected: PASS.
- [ ] **Step 5: Commit**: `feat(scheduled-tasks): report a run item's progress`.

### Task 4: The indicator: Cancel, progress, cancelled outcome, module caption

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/ScheduledTasks/ActivityRows.cs`, `SerginActivityIndicator.razor`, `SerginActivityIndicator.razor.cs`, `ScheduledTaskUiTexts.cs`, `ActivityPanelState.cs`
- Test: `tests/.../ScheduledTasks/ActivityRowsTests.cs`, `tests/.../Shell/ActivityIndicatorRenderingTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3 (`CancelBackgroundRunCommand`, `ActivityItem.Cancelled` and `ActivityItem.Progress`, `RunFinished`).
- Produces:
  - `ActivityState` gains `Cancelled`. `StateOf` is:
    - `Running` while any item is unfinished;
    - else `Cancelled` when `Cancelled > 0` and `Succeeded == 0` and `Failed == 0`;
    - else the phase 2 rules, where a run with some succeeded and some cancelled items is `PartlyFailed`.
  - `ActivityRow` gains:
    - `Guid? TargetId` (for a one-record run);
    - `int? Progress`;
    - `TextDefinition? ModuleCaption`: from the `ISerginWebUiModule` whose `Schema` equals `Ref.Schema`, its `NavCaption` or else its `NavGroup.Title`; null when no UI module matches.
  - `ActivityRows.Build` takes a new `Func<string, TextDefinition?> moduleCaption`. The indicator builds it from `SerginUiModuleCatalog.Modules`.
  - `ActivityPanelState` gains:
    - `IReadOnlyList<ActivityRow> Rows` (empty until the first load);
    - `event Action? RowsChanged`;
    - `bool IsRunning(string commandName, Guid targetId)`.
    The indicator sets the rows after every load it applies.
  - New texts in `ScheduledTaskUiTexts.Activity`: `Cancel` "Cancel", `Cancelled` "Cancelled", `FinishedCancelled` "{Command} was cancelled for {Target}.", `CancelFailed` "The run could not be cancelled."
- Rendering:
  - **Running rows:**
    - A Cancel icon button, labelled `Cancel`, sends `CancelBackgroundRunCommand` through `ISerginDispatcher`.
    - On `RunFinished` the indicator reloads silently. Any other error goes through `IUiErrorPresenter.Notify`.
    - In every case it reloads.
  - **Progress bar on a running row:**
    - one record with progress: determinate at `Progress`;
    - one record without progress: indeterminate;
    - several records: `Done / Total`, as today.
  - **Finished cancelled rows** show the `Cancelled` caption and a neutral icon. The finished snackbar uses `FinishedCancelled`.
  - **Module caption:** the caption renders as a small secondary caption beside the command label.
  - **Duplicate announce (the phase 2 parked finding):** the indicator keeps a per-circuit `HashSet<BackgroundRunRef>` of runs already announced. A run is never announced twice, even after a source drops out of one read and comes back.

- [ ] **Step 1: Write the failing tests.**
  - `ActivityRowsTests`:
    - `TheState_FollowsTheCounts` gains the cancelled cases: all cancelled is Cancelled; succeeded plus cancelled is PartlyFailed.
    - `ModuleCaption_IsTheModulesCaption`: `dm` maps to DeviceManagement's caption or group title; an unknown schema maps to null.
    - `Progress_IsCarried_ForOneRecord`.
  - `ActivityIndicatorRenderingTests`:
    - `CancelOnARunningRow_SendsTheCancel_AndReloads`.
    - `CancelOfAFinishedRun_ReloadsWithoutAnError`: the dispatcher fake answers `RunFinished`; no error snackbar, and one more read.
    - `ARunThatComesBackAfterASourceDropout_IsNotAnnouncedTwice`.
    - `ACancelledRun_IsAnnouncedAsCancelled`.
    - `PanelState_PublishesRows_AfterALoad`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~ActivityRowsTests|FullyQualifiedName~ActivityIndicatorRenderingTests"`. Expected: compile failure on `ActivityState.Cancelled`.
- [ ] **Step 3: Implement.**
  - Add the rows, state, panel-state and rendering changes listed in Interfaces.
  - Add the texts with their catalog keys and `fa` rows.
- [ ] **Step 4: Run** the Step 2 filter, then the `Shell` and `ScheduledTasks` folders, then the pseudo-locale sweep (`--filter FullyQualifiedName~PseudoLocaleSweepTests`). Expected: PASS.
- [ ] **Step 5: Commit**: `feat(views): cancel and progress in the activity indicator`.

### Task 5: The record's section, the runs dialog and the action spinner

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/ScheduledTasks/SerginScheduledTasksSection.razor`, `.razor.cs`, `SerginScheduledRunsDialog.razor`, `.razor.cs`, `ScheduledTaskUiTexts.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Aggregates/Views/SerginGeneratedDetailView.razor`, `.razor.cs`
- Modify: `.claude/CLAUDE.md` (migration paragraph: `AddRunCancellation`, `AddRunItemProgress`), `src/Modules/DeviceManagement/CLAUDE.md`, `src/SharedKernel/.claude/CLAUDE.md` ("Scheduled tasks": cancel, heartbeat interval, progress, the spinner)
- Test: `tests/.../ScheduledTasks/ScheduledTaskUiTests.cs`, `tests/.../Aggregates/GeneratedBackgroundCommandTests.cs`

**Interfaces:**
- Consumes: Task 1's `CancelBackgroundRunCommand` and `ScheduledRunListItem.Cancelled`; Task 3's `ScheduledRunItemListItem.Progress`; Task 4's `ActivityPanelState.IsRunning` and `RowsChanged`.
- Produces:
  - **Section, "Recent one-off runs":** a Running row shows a Cancel button. It sends the cancel, then reloads the section, and `RunFinished` reloads silently. The summary counts cancelled items (new text `ScheduledTaskUiTexts.Runs.Cancelled` "{Count} cancelled", used only when the count is above 0).
  - **Runs dialog:** an item's status column shows `Cancelled`, and a Running item shows its progress as a percent through `ICultureFormatter.Number`.
  - **Detail view:** a bar action whose command is `Background()` shows a small `MudProgressCircular` beside its label while `ActivityPanelState.IsRunning(commandName, Id)` holds. It re-renders on `RowsChanged` (subscribed on init, unsubscribed on dispose). The action stays clickable.
  - **List rows** get no spinner. A run over several records names none of them in the activity read.

- [ ] **Step 1: Write the failing tests.**
  - `ScheduledTaskUiTests`:
    - `OneOffRuns_RunningRow_HasCancel_AndCancelling_Reloads`.
    - `RunsDialog_ShowsCancelledAndProgress`.
  - `GeneratedBackgroundCommandTests`: `DetailAction_ShowsASpinner_WhileItsRunIsUnfinished`. Seed `ActivityPanelState` rows for the device and render the detail view; the spinner's markup is present, and it is gone after the rows clear.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~ScheduledTaskUiTests|FullyQualifiedName~GeneratedBackgroundCommandTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the section, dialog, spinner, texts (catalog keys plus `fa` rows) and the three CLAUDE.md updates.
- [ ] **Step 4: Run** the full integration suite (`dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`) and the UserAccess unit tests. Expected: 0 failed, and only the 3 licence-gated TickerQ skips.
- [ ] **Step 5: Headless walk.** Use the host on port 5555 against Compose Postgres (see memory "Headless browser checks").
  1. Start Read profile on one device. The badge appears and the row's bar moves 33, then 66.
  2. The detail action shows its spinner.
  3. Cancel from the popover: the row moves to Finished as Cancelled, a "was cancelled" snackbar appears, and the spinner goes.
  4. Start bulk on 3 devices and cancel at once: the Finished row reads Cancelled for 3 records.
  5. Open the device's schedule section: the cancelled one-off run is listed with its cancelled count.

  Delete the runs the walk created, and stop the host.
- [ ] **Step 6: Commit**: `feat(views): cancel and progress on the record's section; action spinner`.

## Not in this plan

- `IBackgroundRunSource.GetRunAsync`. Nothing reads one run by its ref: cancel resolves the run through the module's store. Dropped until a consumer exists.
- Progress messages (`progress_message_key` and `progress_message_args`). No handler needs a message yet; the percent is enough.
- A spinner on list-row actions and bulk actions. The activity read names no target for a run over several records.
- An admin page of every user's runs, and a remote `IBackgroundRunSource`.
- Phase 1's parked items:
  - stale Pending items are re-enqueued every recovery pass;
  - a 1000-target start does all its work while the user waits;
  - the `< 1 s` timing assertion;
  - translator notes on `BackgroundStarted(Many)`.
- Phase 2's parked minors:
  - a failed indicator start does not retry for the life of the circuit;
  - a singleton-registered source is rebuilt on every call.
