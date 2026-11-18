# Background Commands, Phase 2 (Activity) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every signed-in user sees their own background, run-now and one-off runs from every module in an app-bar activity indicator. Each run shows live status, a target label, a link to the target, and a snackbar when it finishes. This closes phase 1's gap where a user without the manage permission never saw an outcome.

**Architecture:**
- Run tables stay in each module's schema.
- `IBackgroundRunDirectory` (SharedKernel Application) fans out over one `IBackgroundRunSource` per opted-in module. Each local source reads its schema through `IScheduledTaskQueries`.
- A singleton in-process `IRunNotifier` carries `RunChanged` events from the starter and the executor to subscribed circuits. A 5-second poll covers runs that execute in another process.
- The indicator reads through one untyped query, `GetMyActivityQuery`, sent with `ISerginDispatcher`. It names commands and links targets through `ScheduledCommandCatalog` and `AggregateViewRegistry`.

**Tech Stack:** .NET 10, MediatR, EF Core + Npgsql + Dapper, Blazor Server + MudBlazor, xUnit + Testcontainers, Playwright (headless check).

**Spec:** `docs/superpowers/specs/2026-11-18-background-commands-design.md`, sections "Reading across modules", "Live updates", "Names and links", "UI → The activity indicator", Storage (`target_label`, the acting-user index). Phase 1 is merged (SharedKernel #49, host #65). Cancel and progress (spec plan (3)) are **not** in this plan.

## Global Constraints

- Work in the worktree `.claude/worktrees/background-commands-2`, on branch `feat/background-commands-2` in the host repo and in `src/SharedKernel`. SharedKernel changes are committed inside the submodule; the host commit carries tests, the migration, the seed and the submodule bump. Never push.
- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: the build has zero warnings. No `@code` blocks in `.razor` files. File-scoped namespaces. No `Co-Authored-By` trailer in commits.
- Every user-visible text is a declared `TextDefinition`, its key added to `tests/.../Commands/Types/catalog-keys.txt` in sorted order, with a Persian row in the `fa` block of `docker-compose/.files/seed/dev-seed.sql`. The pseudo-locale sweep must stay green.
- Exact values from the spec:
  - Activity triggers are `Background`, `RunNow` and `OneOff`, never `Scheduled`.
  - Finished runs are listed for **24 hours**; at most **50** runs are read per source.
  - The fallback poll runs every **5 seconds**, only while at least one run is unfinished.
  - A user's own runs ignore the scope filter.
  - A run is identified across modules by `BackgroundRunRef(Schema, RunId)`.
  - A source that throws is logged and skipped.
- Migration: one DeviceManagement migration, `AddRunActivity`, hand-converted to a file-scoped namespace (CLAUDE.md "A freshly scaffolded migration does not build as scaffolded"). It adds:
  - `scheduled_task_run_items.target_label text NULL`;
  - index `ix_scheduled_task_runs_acting_user_id_started_at_utc` on `scheduled_task_runs (acting_user_id, started_at_utc DESC)`.
- A target label is user data shown as stored, cut to **200** characters at start.

## Review Focus

1. **A user with runs in two modules sees both, in start order.** Pinned in Task 2 (`dm` + `tst` in one host).
2. **Another user's runs never appear**, through the query or through a notifier event. Pinned in Tasks 2 and 3.
3. **A circuit that closes unsubscribes.** No notifier handler outlives its component, and a publish after dispose neither throws nor reaches it. Pinned in Task 3.
4. **The indicator never breaks the shell.** A failed activity load renders nothing instead of an error panel, and an anonymous user renders nothing. Pinned in Task 4.
5. **Runs started before the migration have a null label.** The indicator shows "1 record" (or "{Count} records"). Pinned in Task 4's row-building tests.

---

### Task 1: Target labels and the activity index

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Domain/ScheduledTasks/ScheduledTaskRunItem.cs` (+ `string? TargetLabel`, set at creation)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/ScheduledRunStarter.cs` (`ScheduledRunStart` gains trailing `IReadOnlyDictionary<Guid, string>? TargetLabels = null`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/Commands/StartBackgroundCommand.cs` (targets become `BackgroundTarget`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ScheduledTasks/ScheduledTasksModelBuilderExtensions.cs` (column; index)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/SerginDispatcherBackgroundExtensions.cs`, `Aggregates/Views/Parts/GeneratedViewSupport.cs`, `SerginGeneratedDetailView.razor.cs`, `GeneratedListBody.razor.cs` (pass labels)
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Migrations/<ts>_AddRunActivity.cs` (+ Designer, snapshot)
- Test: `tests/.../ScheduledTasks/BackgroundStartTests.cs`, `ScheduledTaskModelTests.cs`, `DeviceManagementScheduledTasksModelTests.cs`, `Aggregates/GeneratedBackgroundCommandTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record BackgroundTarget(Guid TargetId, Guid? RootId = null, string? Label = null);`
  - `StartBackgroundCommand(IBaseCommand Template, IReadOnlyCollection<BackgroundTarget> Targets)` and `StartAsync(this ISerginDispatcher, IBaseCommand, IReadOnlyCollection<BackgroundTarget>, CancellationToken = default)`. The validator keeps every phase 1 rule; it maps targets to `ScheduledTaskTarget` for `ScheduledTaskRules.TemplateAndTargets`.
  - `ScheduledTaskRunItem.TargetLabel` (`string?`, max 200; the starter trims and cuts).
  - `GeneratedViewSupport.StartManyAsync(dispatcher, command, IReadOnlyCollection<BackgroundTarget>)`.

- [ ] **Step 1: Write the failing tests**
  - `BackgroundStartTests.Labels_AreStoredPerItem`: start over two probes with labels `"P-1"` and a 250-character string. The items' `TargetLabel` values are `"P-1"` and the first 200 characters of the long string.
  - `BackgroundStartTests.NoLabel_StoresNull`.
  - `ScheduledTaskModelTests` and `DeviceManagementScheduledTasksModelTests`: `target_label` is nullable `text`, and the index `ix_scheduled_task_runs_acting_user_id_started_at_utc` exists on `(acting_user_id, started_at_utc)` with `started_at_utc` descending.
  - `GeneratedBackgroundCommandTests.SendFromId_StoresTheRecordTitleAsLabel`: Read profile from the detail path stores the device's `DeviceId` as the item's label. `StartMany_StoresEachRowsTitle` does the same for three rows.
- [ ] **Step 2: Run, expect FAIL** (`--filter FullyQualifiedName~BackgroundStartTests|FullyQualifiedName~ScheduledTaskModelTests|FullyQualifiedName~DeviceManagementScheduledTasksModelTests|FullyQualifiedName~GeneratedBackgroundCommandTests`).
- [ ] **Step 3: Implement.**
  - **Labels from the views:** the label is the record's title as the generated view already shows it. On the detail page that is the value its trail and placeholders use; on a list row it is the row's title field. Reuse the existing title accessor, do not add a new configuration.
  - **Migration:** scaffold `AddRunActivity` with `dotnet ef migrations add AddRunActivity --project src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All`, then convert it to a file-scoped namespace.
- [ ] **Step 4: Run, expect PASS**, then the `ScheduledTasks`, `Aggregates` and `Devices` folders.
- [ ] **Step 5: Commit**: `feat(scheduled-tasks): store a label per background target`

---

### Task 2: Reading a user's runs across modules

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/Activity/` — `BackgroundRunRef.cs` (move the record here from `StartBackgroundCommand.cs`, same namespace if the move would break callers), `ActivityItem.cs`, `IBackgroundRunSource.cs`, `IBackgroundRunDirectory.cs`, `BackgroundRunDirectory.cs`, `GetMyActivityQuery.cs` (record, handler, configuration)
- Modify: `IScheduledTaskQueries.cs` and `Infrastructure.Data.EFCore/ScheduledTasks/ScheduledTaskQueries.cs` (+ `ActivityForUserAsync`)
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ScheduledTasks/LocalBackgroundRunSource.cs`
- Modify: `ModuleDbContextExtensions.cs` (one source per registration), `ScheduledTaskServiceExtensions.cs` (directory, handler)
- Test: `tests/.../ScheduledTasks/ActivityDirectoryTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ActivityItem(BackgroundRunRef Ref, string CommandName, ScheduledRunTrigger Trigger, ScheduledRunStatus Status, DateTime StartedAtUtc, DateTime? CompletedAtUtc, int Total, int Succeeded, int Failed, int Unfinished, Guid? TargetId, Guid? RootId, string? TargetLabel, string? FirstErrorCode);` The target fields and `FirstErrorCode` are set only for a one-item run.
  - `public interface IBackgroundRunSource { string Schema { get; } Task<IReadOnlyList<ActivityItem>> ListActivityAsync(UserId user, DateTime finishedSinceUtc, int take, CancellationToken ct); }`
  - `public interface IBackgroundRunDirectory { Task<IReadOnlyList<ActivityItem>> ListActivityAsync(UserId user, DateTime finishedSinceUtc, CancellationToken ct); }`. Singleton. It calls every source in parallel, each in its own async scope; logs and skips a source that throws; merges the results ordered by `StartedAtUtc` descending, then by `RunId`. `take` is 50 per source.
  - `IScheduledTaskQueries.ActivityForUserAsync(string schema, Guid userId, DateTime finishedSinceUtc, int take, CancellationToken ct)` → `ErrorOr<IReadOnlyList<ActivityItem>>`. It is one SQL read **without** `ScopeColumns.ReachSql`:
    - `acting_user_id = @UserId` and `"trigger" IN ('Background','RunNow','OneOff')`;
    - `status = 'Running' OR completed_at_utc >= @FinishedSince`;
    - `Unfinished` counts the run's items in Pending, Running or Retrying;
    - the one-item fields come from a lateral over the items, used only when the run's total is 1.
  - `public sealed record GetMyActivityQuery(DateTime FinishedSinceUtc) : IQuery<IReadOnlyList<ActivityItem>>;` Its handler passes the caller's `IUserContext.Id` to the directory. Its configuration requires no permission and sets `NotShown(x => x.FinishedSinceUtc)`; it is untyped, so it is checked on the current scope with nothing to check.

- [ ] **Step 1: Write the failing tests** in `ActivityDirectoryTests`. Use the `ScheduledTaskTestHost`, which composes the real `dm` module and the `tst` context, so two sources exist.
  - `ListsTheUsersRunsFromBothModules_NewestFirst`: start a `tst` probe run, then a Read profile run on a device as the same caller. The list holds both, `dm` first, with the right `Ref.Schema` values.
  - `CronRuns_AreNotActivity`: a run started with `ScheduledRunTrigger.Scheduled` for the same user is absent.
  - `OtherUsersRuns_AreNotListed`.
  - `RunOutsideTheUsersCurrentReach_IsStillListed`: revoke the caller's grant after the start; the run is still listed.
  - `FinishedOlderThanTheWindow_IsNotListed`: set `completed_at_utc` 25 hours back by SQL; the run is gone. A Running run started 25 hours ago stays listed.
  - `AThrowingSource_IsSkipped`: register an extra `IBackgroundRunSource` that throws; the other sources' runs are still returned.
  - `OneTargetRun_CarriesTargetAndLabel_ManyTargetRunDoesNot`.
  - `Query_ReturnsOnlyTheCallersRuns`: `GetMyActivityQuery` through `ISerginDispatcher` for two callers.
- [ ] **Step 2: Run, expect FAIL** (types missing).
- [ ] **Step 3: Implement** as in Interfaces. The SQL quotes the schema through the existing `TryQuote`, and an unknown schema is `UnknownSchema`. The local source turns an error into an empty list and logs a warning.
- [ ] **Step 4: Run, expect PASS**, then the `ScheduledTasks` folder.
- [ ] **Step 5: Commit**: `feat(scheduled-tasks): read a user's runs across modules`

---

### Task 3: Live run notifications

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/Activity/IRunNotifier.cs`, `RunNotifier.cs`, `RunChanged.cs`
- Modify: `ScheduledRunStarter.cs` (publish after the items are enqueued), `ScheduledRunItemExecutor.cs` (publish after each outcome save and after `CompleteRunIfFinishedAsync`, and after a claim moves an item to Running)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` (register the singleton in every host)
- Test: `tests/.../ScheduledTasks/RunNotifierTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record RunChanged(BackgroundRunRef Ref, UserId ActingUser);`
  - `public interface IRunNotifier { void Publish(RunChanged change); IDisposable Subscribe(UserId user, Func<RunChanged, Task> handler); }`
  - `Publish` never throws to its caller and never awaits handlers in line: it starts each handler and logs a handler fault. A disposed subscription is removed at once.

- [ ] **Step 1: Write the failing tests** in `RunNotifierTests`:
  - `Subscriber_HearsStartAndFinish`: subscribe user A, start a probe run as A, drain. The handler sees at least two events for the run's `Ref`, and the last one arrives after the run is `Completed`.
  - `OtherUser_HearsNothing`: user B's subscription gets no event for A's run.
  - `DisposedSubscription_HearsNothing_AndPublishDoesNotThrow`.
  - `ThrowingHandler_DoesNotBreakTheRunOrOtherHandlers`: one handler throws, the run still completes, and a second subscriber still hears it.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** Hold subscriptions in a thread-safe per-user collection. Publish only after the save it reports has committed.
- [ ] **Step 4: Run, expect PASS**, then the `ScheduledTasks` folder.
- [ ] **Step 5: Commit**: `feat(scheduled-tasks): notify circuits when a user's run changes`

---

### Task 4: The activity indicator

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/ScheduledTasks/SerginActivityIndicator.razor` + `.razor.cs`, `ActivityRows.cs` (pure row building), `ActivityPanelState.cs` (scoped: `event Func<Task>? OpenRequested`, `Task OpenAsync()`)
- Modify: `Layout/SerginMainLayout.razor` (render the indicator between `SerginScopePicker` and `SerginLanguageSwitcher`), `ScheduledTasks/ScheduledTaskUiTexts.cs` (+ the `Activity` texts), `Aggregates/Views/SerginGeneratedDetailView.razor.cs` and `Views/Parts/GeneratedListBody.razor.cs` (the started snackbar gets a "Show" action that calls `ActivityPanelState.OpenAsync()`), `AddSerginBlazorKit` (register `ActivityPanelState` scoped)
- Modify: DeviceManagement `DeviceUiTexts` and `DeviceViewConfiguration` (delete `ReadProfileDone`/`BulkReadProfileDone`, which are no longer shown; ruling R4 of phase 1 is closed), its `.claude/CLAUDE.md`, `catalog-keys.txt`, the seed
- Modify: `src/SharedKernel/.claude/CLAUDE.md` ("Background commands" bullet: the directory, notifier and indicator), the root `.claude/CLAUDE.md` (test-folder list)
- Test: `tests/.../ScheduledTasks/ActivityRowsTests.cs`, `tests/.../Shell/ActivityIndicatorRenderingTests.cs`

**Interfaces:**
- Consumes: `GetMyActivityQuery`, `ActivityItem` (Task 2); `IRunNotifier` (Task 3).
- Produces:
  - `internal sealed record ActivityRow(BackgroundRunRef Ref, TextDefinition? CommandLabel, string CommandName, string Target, string? Href, ActivityState State, DateTime StartedAtUtc, DateTime? CompletedAtUtc, int Done, int Total, string? FirstErrorCode);` with `enum ActivityState { Running, Succeeded, PartlyFailed, Failed }`.
  - `internal static IReadOnlyList<ActivityRow> ActivityRows.Build(IReadOnlyList<ActivityItem> items, ScheduledCommandCatalog catalog, AggregateViewRegistry views, Func<int, string> recordsText)`:
    - **Label:** the `CommandView.Label` of the catalogued request in its root's view model, else null, and the view shows `CommandName`.
    - **Target:** `TargetLabel` when present; else `recordsText(Total)`, where the caller's function returns `OneRecord` for 1 and `Records` otherwise.
    - **Href:** `model.Routes.Detail?.Invoke(TargetId)` for a one-item root target; null for a child target or many targets.
    - **State:** `Running` while `Unfinished > 0`; `Succeeded` when `Failed == 0`; `Failed` when `Succeeded == 0`; else `PartlyFailed`.
  - Texts, under `ScheduledTaskUiTexts.Activity` (`common.scheduled-tasks.activity.*`):

    | Key | Default |
    |---|---|
    | `Title` | "Activity" |
    | `Running` | "Running" |
    | `Finished` | "Finished in the last 24 hours" |
    | `Empty` | "Nothing is running." |
    | `Records` | "{Count} records" |
    | `OneRecord` | "1 record" |
    | `Clear` | "Clear" |
    | `Show` | "Show" |
    | `FinishedSucceeded` | "{Command} finished for {Target}." |
    | `FinishedFailed` | "{Command} failed for {Target}." |
    | `FinishedPartly` | "{Command} finished for {Done} of {Total} records." |
    | `OpenLabel` | "Activity, {Count} running" |

- [ ] **Step 1: Write the failing tests.**
  - `ActivityRowsTests` (no browser):
    - a one-device Read profile item yields the Read profile label, the label as target, and `/dm/devices/{id}`;
    - a three-target item yields `"3 records"` and no href;
    - a one-target item with a null label (a run started before the migration) yields `"1 record"`;
    - the four `State` cases;
    - an unknown command name yields a null label and the name kept.
  - `ActivityIndicatorRenderingTests`:
    - the prerendered page for a signed-in user contains no indicator markup, because it loads after the first interactive render;
    - the interactive render, through the existing interactive-render helper the shell tests use, shows the icon with no badge when the user has no runs;
    - with one Running run it shows badge `1`;
    - a failing activity load (a source that throws for every schema) renders the icon and nothing else, with no `SerginProblemPanel`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
  - **Lifecycle:** load in `OnAfterRenderAsync(firstRender)` through `ISerginDispatcher.SendAsync(new GetMyActivityQuery(clock.UtcNow - 24h))`. Subscribe to `IRunNotifier` for `IUserContext.Id`, and reload through `InvokeAsync` on each event. Run a `PeriodicTimer(5 s)` only while some row is `Running`. Dispose the subscription and the timer on dispose.
  - **Anonymous and failed loads:** render nothing for the anonymous identity (check the anonymous shape `ClaimsPrincipalUserContextFactory` builds) or on a load error, and log the error.
  - **Popover:** `MudBadge` + `MudIconButton` (`Icons.Material.Outlined.Pending`; `aria-label` = `OpenLabel`) opening a `MudPopover`. It holds the Running section, the Finished section and Clear.
    - Each row shows the label, the target, a progress text `Done / Total` for many targets, the elapsed time or the finished time through `ICultureFormatter`, and a link when there is an href.
    - The first error of a one-target failure shows through the existing error-text rendering used by the runs dialog.
  - **Clear:** stores the newest finished `CompletedAtUtc` in `localStorage` under `sergin.activity.cleared.{userId}` with `IJSRuntime.InvokeAsync("localStorage.setItem", …)`, wrapped in try/catch. Finished rows at or before it are hidden; a missing or unreadable value hides nothing.
  - **Finished snackbar:** when a row moves from `Running` to finished between two loads in this circuit, show one snackbar with the matching `Finished*` text, with an "Open" link when there is an href. Runs already finished at the first load raise none.
  - **Narrow screens:** below md the popover is full width (MudBlazor `Fixed` + a CSS class).
- [ ] **Step 4: Run, expect PASS**; then the `Shell`, `ScheduledTasks`, `Localization` (pseudo-locale sweep) and `Commands/Types` folders.
- [ ] **Step 5: Run the full suite** (`dotnet build Sergin.MeterMinder.slnx`, 0 warnings; `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`). Expected: all pass, with the TickerQ-licence skips.
- [ ] **Step 6: Headless check.** Use Playwright 1.46 per the memory note "Headless browser checks", with the host from this worktree on port 5555 and Compose Postgres. Apply `AddRunActivity` (Development auto-migrate).
  - Sign in as the DevUser.
  - Open a device and click Read profile. The badge shows `1` within 2 s, and "Show" opens the popover with the running row.
  - After about 4 s the badge clears, a "Read profile finished for MTR-…" snackbar shows, and the row is under Finished with a working link.
  - Bulk Read profile on 3 devices gives one row, "3 records", with `n / 3` progress.
  - Clear hides the finished rows and survives a reload.
  - Delete the created runs afterwards.
- [ ] **Step 7: Commit** (SharedKernel, then the host with the bump): `feat(views): activity indicator for background runs`

---

## Not in this plan (spec plan (3) or later)

- Cancel (`CancelBackgroundRunCommand`, `Cancelled` status, `cancel_requested_at_utc`) and progress (`IRunProgress`, progress columns).
- The per-action spinner while a record has an unfinished run of that command.
- An admin page of every user's runs.
- A remote source (gRPC or an outbox projection) behind `IBackgroundRunSource`.
- Phase 1's parked items: stale Pending items are re-enqueued every recovery pass; a 1000-target start does all its work while the user waits; the `< 1 s` timing assertion; translator notes on `BackgroundStarted(Many)`.
