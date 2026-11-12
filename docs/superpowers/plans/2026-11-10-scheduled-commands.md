# Scheduled Commands Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an operator run a schedulable command now or on a cron schedule, once per picked target, through the
normal MediatR pipeline, acting on behalf of a user, with TickerQ firing the triggers behind a SharedKernel
abstraction.

**Architecture:** SharedKernel owns the model (`ScheduledTask` aggregate, runs, run items), the `Schedulable(name)`
opt-in, the engine abstraction, the runner and the management commands; a module opts in through its `DbContext`
the way it opts into the outbox. A new adapter project implements the engine with TickerQ 10.5.1. Generic Blazor
parts live in `Sergin.SharedKernel.Presentation.Blazor`. Everything is proven by a test-only aggregate and
test-only schedulable commands; no real module opts in and the real host is not wired.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, MediatR, ErrorOr, FluentValidation, TickerQ 10.5.1
(`TickerQ`, `TickerQ.EntityFrameworkCore`), MudBlazor, xUnit 2.9.3 + Testcontainers PostgreSQL.

**Spec:** `docs/superpowers/specs/2026-11-10-scheduled-commands-design.md`

## Global Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: every task ends with a clean
  `dotnet build Sergin.MeterMinder.slnx`.
- Central Package Management: add `<PackageVersion Include="TickerQ" Version="10.5.1" />` and
  `<PackageVersion Include="TickerQ.EntityFrameworkCore" Version="10.5.1" />` to `Directory.Packages.props`,
  alphabetical; `PackageReference`s carry no `Version`. Never reference `TickerQ.Dashboard`.
- No TickerQ type in any module project, in `Sergin.SharedKernel.Application`, `.Domain`, `.Infrastructure.Data.EFCore`,
  `.Hosts` or `.Presentation.Blazor`. Only the adapter project and the test project reference TickerQ.
- Ids: `Guid.CreateVersion7()`. Aggregates: private parameterless constructor + `static Create` with
  object-initializer syntax. Handlers `internal sealed`. Requests and responses `sealed record`.
- `.razor` files are markup only; all C# in `.razor.cs`. Labels are `TextDefinition`s; texts declared under
  `common.scheduled-tasks.*` in SharedKernel's `CommonTexts` source.
- The run identity is **never** an `ISystemUserContext`.
- Licence path key: `Sergin:ScheduledTasks:TickerQ:LicensePath`. The licence file is never committed;
  add `*.tqlicense` to `.gitignore`.
- Options section `Sergin:ScheduledTasks`: `MaxAttempts` (default 3), `RetryIntervalsSeconds` (default `[30, 120]`),
  `Retention` (default 30 days), `PurgeInterval` (default 1 hour), validated by an `IValidateOptions<T>` that names
  the bad key (copy `OutboxOptions.Validate`).
- Stable command names match `^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*(-[a-z0-9]+)*)+$` and start with the owning module's
  schema segment (`dm.devices.read-profile`).
- Table names in the module schema: `scheduled_tasks`, `scheduled_task_targets`, `scheduled_task_runs`,
  `scheduled_task_run_items`. TickerQ's tables live in schema `tickerq`.
- Platform permission: `permission.sys.scheduled-tasks.manage`, label "Manage scheduled tasks".
  *(This replaces the spec's per-module `permission.<schema>.scheduled-tasks.manage`, my call: the catalogue refuses
  a SharedKernel-owned definition in a module schema, and one platform permission is what the shared UI can check
  without knowing the module.)*
- Migrations: none in this work. Test contexts build their tables with `IRelationalDatabaseCreator.CreateTablesAsync`
  (see `OutboxTestHost.ResetSchemaAsync`); the TickerQ test store does the same for its own context.
- SharedKernel is a submodule: commit SharedKernel changes on a branch inside `src/SharedKernel`, then commit the
  pointer bump plus root-repo changes in the root worktree.

## Review Focus

1. **A stored command name no command declares any more** (class renamed without keeping the name, or opt-in
   removed): the run item fails with error code `ScheduledTasks.UnknownCommand`; the run still completes.
   Test added to Task 5.
2. **Arguments that no longer deserialize** after a command's shape changed: run item fails with
   `ScheduledTasks.ArgumentsInvalid`, not an unhandled exception that retries forever. Test added to Task 5.
3. **Cron or time zone the engine cannot parse** at create/edit: refused as a validation error on `Cron` /
   `TimeZoneId` before anything is saved or scheduled. Test added to Task 7.
4. **Delegating user later out of reach of the task's scope**: the runner still loads the task (it ignores the
   `Sergin:Scope` filter), and each run item fails Forbidden; the task is neither hidden nor silently skipped.
   Test added to Task 5.
5. **Engine call fails while saving a task**: nothing is saved, the request returns an error, and no engine trigger
   is left behind. Test added to Task 7 with a failing fake engine.

---

### Task 1: Worktree, adapter project skeleton and TickerQ gate

Proves the two things the docs left open before anything depends on them: a `[TickerFunction]` declared in a
referenced library runs, and the licence is loaded from configuration. **If the function in the library never
runs, stop and ask the user** (spec: no silent engine swap).

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ/Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ.csproj`
- Create: `.../TickerQ/TickerQScheduledTaskOptions.cs`, `.../TickerQ/TickerQLicenseGuard.cs`, `.../TickerQ/TickerQEngineExtensions.cs`, `.../TickerQ/ProbeFunctions.cs` (deleted in Task 8)
- Modify: `Directory.Packages.props`, `Sergin.MeterMinder.slnx` (adapter under the SharedKernel infrastructure folder), `src/SharedKernel/Sergin.SharedKernel.slnx`, `.gitignore`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/ScheduledTasks/TickerQGateTests.cs`, `.../ScheduledTasks/RequiresTickerQLicenseFactAttribute.cs`

**Interfaces:**
- Produces: `TickerQScheduledTaskOptions { string LicensePath; }` bound from `Sergin:ScheduledTasks:TickerQ`;
  `IServiceCollection AddSerginTickerQ(this IServiceCollection services, IConfiguration serginSection, string connectionString)`;
  `RequiresTickerQLicenseFactAttribute : FactAttribute` (sets `Skip` in its constructor when the env var
  `SERGIN_TICKERQ_LICENSE` is unset or names no file).

- [ ] **Step 1:** Create worktree `../Sergin.MeterMinder-scheduled-commands` on branch `feature/scheduled-commands`;
  run `git submodule update --init --recursive`; inside `src/SharedKernel` create branch `feature/scheduled-commands`.
- [ ] **Step 2: Write the failing tests**
  - `TickerQGateTests.FunctionInReferencedAssembly_Runs` (`[RequiresTickerQLicenseFact]`): host with
    `AddSerginTickerQ`, enqueue a time ticker for `"Sergin.Probe"` via `ITimeTickerManager<TimeTickerEntity>`,
    poll ≤ 30 s; assert `ProbeFunctions.Calls == 1`.
  - `TickerQGateTests.MissingLicenseFile_FailsStart`: set `Sergin:ScheduledTasks:TickerQ:LicensePath` to a
    non-existent path; assert host start throws `InvalidOperationException` whose message contains
    `"Sergin:ScheduledTasks:TickerQ:LicensePath"`.
- [ ] **Step 3:** Run `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter TickerQGateTests`. Expected: compile failure.
- [ ] **Step 4:** Implement `AddSerginTickerQ`: `AddTickerQ(o => { o.UseLicense(path); o.UseApplicationNamespace("sergin"); o.AddOperationalStore(s => s.UseTickerQDbContext<TickerQDbContext>(db => db.UseNpgsql(cs), schema: "tickerq")); })`, no dashboard; `TickerQLicenseGuard` validates on start (`IValidateOptions<TickerQScheduledTaskOptions>`, message names the key). `ProbeFunctions` holds `[TickerFunction("Sergin.Probe")]` and a static counter. The test fixture creates TickerQ's tables with `CreateTablesAsync` on its context and calls `app.UseTickerQ()` through a startup filter.
- [ ] **Step 5:** Run the filter again. Expected: PASS (or `FunctionInReferencedAssembly_Runs` skipped with reason when no licence; run it once with the licence before continuing — if it fails, stop and ask the user).
- [ ] **Step 6:** Commit in `src/SharedKernel` (`Add TickerQ adapter skeleton`), then in root (`Add TickerQ gate tests`, pointer bump, packages, slnx, gitignore).

---

### Task 2: `Schedulable(name)` and the scheduled command catalogue

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandConfigurationBuilder.cs`, `CommandSettings.cs`, `CommandConfigurationRegistry.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/ScheduledCommand.cs`, `ScheduledCommandCatalog.cs`, `ScheduledCommandNames.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` (register catalogue singleton; resolve it at start beside the registry)
- Test: `tests/.../ScheduledTasks/ScheduledCommandCatalogTests.cs`, `tests/.../ScheduledTasks/ScheduledTaskTestTypes.cs`

**Interfaces:**
- Produces:
  - `CommandConfigurationBuilder<TCommand> Schedulable(string name)`; base builder `internal string? ScheduleName`.
  - `CommandSettings` gains `string? ScheduleName = null` (last positional parameter).
  - `sealed record ScheduledCommand(string Name, string Schema, Type RequestType, Type Root, CommandTargetKind TargetKind, Type IdType, Type? RootIdType, bool Selectable)`.
  - `sealed class ScheduledCommandCatalog { static ScheduledCommandCatalog FromSources(IEnumerable<ModuleConfigurationSource> sources, CommandConfigurationRegistry registry); IReadOnlyCollection<ScheduledCommand> Commands; bool TryGet(string name, [NotNullWhen(true)] out ScheduledCommand? command); ScheduledCommand? For(Type requestType); }`.
  - `static class ScheduledCommandNames { bool IsValid(string name); }`.
- Test types (in `ScheduledTaskTestTypes.cs`, reused by later tasks): aggregate `ScheduleProbe` (root, `ScheduleProbeId(Guid Value)`, child `ScheduleProbeChild`), `ScheduleProbeAggregateFeatureConfiguration` (`Audited().Scoped()`, `Permissions(ScheduleProbePermissions.Read, .Touch)` with names `permission.tst.probes.read` / `.touch`), and commands:
  - `TouchProbeCommand(ScheduleProbeId Id, ProbeNote Note) : ICommand<TouchProbeResponse>, ISetPropertyCommand<…>, ISelectable` — configured `Schedulable("tst.probes.touch").RequirePermissions(Touch)`, validator refuses `Note.Value == "invalid"`.
  - `TouchProbeChildCommand` (child target, `ISelectable`, `Schedulable("tst.probes.touch-child")`).
  - `TouchOneProbeCommand` (entity, not selectable, `Schedulable("tst.probes.touch-one")`).
  - Refusal fixtures, each in its own configuration type only fed to the test that needs it: a version-guarded command on a `Versioned()` root, a creation command, an `ISystemCommand`, a malformed name `"Tst.Bad"`, a duplicate name, a name outside the source schema `"dm.x"` (source schema `tst`).

- [ ] **Step 1: Write the failing tests** in `ScheduledCommandCatalogTests` (pure, no database, built with `CommandConfigurationRegistry.FromConfigurationTypes` and `ModuleConfigurationSource.FromAssemblies`/`FromTypes`):
  - `Schedulable_IsInTheCatalogue`: `TryGet("tst.probes.touch")` → `RequestType == typeof(TouchProbeCommand)`, `TargetKind == Entity`, `Selectable == true`, `IdType == typeof(ScheduleProbeId)`.
  - `ChildCommand_CarriesRootIdType`: `RootIdType == typeof(ScheduleProbeId)`, `TargetKind == ChildEntity`.
  - `Unconfigured_IsNotSchedulable`: `For(typeof(UnconfiguredProbeCommand))` is null.
  - `VersionGuarded_IsRefused`, `Creation_IsRefused`, `SystemCommand_IsRefused`, `MalformedName_IsRefused`, `DuplicateName_IsRefused`, `NameOutsideSchema_IsRefused`: each `Assert.Throws<InvalidOperationException>` and the message contains the request's `FullName` (and, for duplicate, both type names).
  - `VersionGuardedWithWaiver_IsAllowed`: the same command whose configuration also calls `WithoutExpectedVersion()` is in the catalogue.
- [ ] **Step 2:** Run `--filter ScheduledCommandCatalogTests`. Expected: compile failure.
- [ ] **Step 3:** Implement. The catalogue reads `registry.For(type).ScheduleName` for every request type of every source; schema check uses the source's `Schema` (skipped when null, as `PermissionCatalog.CheckSchema` does). Id types come from `CommandTypes.TargetOf(type)` and the `Id`/`RootId` property types.
- [ ] **Step 4:** Run the filter. Expected: PASS. Run the whole suite filter `Commands` to confirm no regression.
- [ ] **Step 5:** Commit (SharedKernel then root).

---

### Task 3: Model, EF mapping and per-module opt-in

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Domain/ScheduledTasks/ScheduledTask.cs` (with trailing `ScheduledTaskId`, `ScheduledTaskTarget`, `ScheduledTaskState`), `ScheduledTaskRun.cs` (with `ScheduledRunTrigger`, `ScheduledRunStatus`), `ScheduledTaskRunItem.cs` (with `ScheduledRunItemStatus`)
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/ScheduledTaskAggregateFeatureConfiguration.cs`, `ScheduledTaskPermissions.cs`, `ScheduledTaskTexts.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ScheduledTasks/IScheduledTasksDbContext.cs`, `ScheduledTasksModelBuilderExtensions.cs`
- Modify: `.../Infrastructure.Data.EFCore/ModuleDbContextExtensions.cs` (opt-in branch: register `ScheduledTasksRegistration`, and once per host one `ModuleConfigurationSource.FromTypes(typeof(ScheduledTaskAggregateFeatureConfiguration), …)` — Tasks 6 and 7 add their configurations to this same source, so the registry derives the scope and version rules for the management requests and the catalogue picks up `Manage`; a `FromTypes` source has no schema, which is what lets a `sys` permission through), `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` (host `AggregateFeatureRegistry` built as a factory that adds `ScheduledTaskAggregateFeatureConfiguration` when any `ScheduledTasksRegistration` is registered). *(Known gap, my call: a `FromTypes` source keys the permission label by name and leaves it out of the text catalogue, so "Manage scheduled tasks" shows untranslated on the role pages until the catalogue grows a SharedKernel-owned source kind.)*
- Test: `tests/.../ScheduledTasks/ScheduledTasksDbContext.cs` (test context, schema `tst`, maps `ScheduleProbe` and calls `ApplyScheduledTasks()`), `tests/.../ScheduledTasks/ScheduledTaskTestHost.cs`, `tests/.../ScheduledTasks/ScheduledTaskModelTests.cs`

**Interfaces:**
- Produces:
  - `ScheduledTask : AggregateRoot<ScheduledTaskId>` with `string CommandName`, `string Arguments`, `string Cron`, `string TimeZoneId`, `ScheduledTaskState State`, `UserId DelegatingUserId`, `Guid? EngineTriggerId`, `IReadOnlyCollection<ScheduledTaskTarget> Targets`; `static ScheduledTask Create(string commandName, string arguments, IReadOnlyCollection<ScheduledTaskTarget> targets, string cron, string timeZoneId, UserId savedBy)`; `void Update(string arguments, IReadOnlyCollection<ScheduledTaskTarget> targets, string cron, string timeZoneId, UserId savedBy)` (sets `DelegatingUserId = savedBy`); `void Pause()`; `void Resume()`; `void LinkTrigger(Guid engineTriggerId)`.
  - `sealed record ScheduledTaskTarget(Guid TargetId, Guid? RootId)`.
  - `ScheduledTaskRun` (plain entity, `Guid Id`): `ScheduledTaskId? TaskId`, `ScheduledRunTrigger Trigger` (`Scheduled|RunNow|OneOff`), `UserId ActingUserId`, `Guid ScopeId`, `string ScopePath`, `string CommandName`, `string Arguments`, `ScheduledRunStatus Status` (`Running|Completed|Skipped`), `DateTime StartedAtUtc`, `DateTime? CompletedAtUtc`, `int Total`, `int Succeeded`, `int Failed`; `static ScheduledTaskRun Start(...)`, `static ScheduledTaskRun Skipped(...)`, `void Complete(int succeeded, int failed, DateTime at)`.
  - `ScheduledTaskRunItem` (plain entity): `Guid Id`, `Guid RunId`, `Guid TargetId`, `Guid? RootId`, `ScheduledRunItemStatus Status` (`Pending|Running|Retrying|Succeeded|Failed`), `int Attempts`, `string? ErrorCode`, `string? ErrorDescription`, `DateTime? StartedAtUtc`, `DateTime? CompletedAtUtc`, `Guid? EngineJobId`; `MarkRunning(DateTime)`, `MarkSucceeded(DateTime)`, `MarkRetrying(string code, string description)`, `MarkFailed(string code, string description, DateTime)`.
  - `ScheduledTaskAggregateFeatureConfiguration`: `Audited().SoftDeletable().Versioned().Scoped()`, `Permissions(ScheduledTaskPermissions.Manage)`, `Texts(ScheduledTaskTexts.Category)`, `Named(ScheduledTaskTexts.Singular)`.
  - `ScheduledTaskPermissions.Manage = PermissionDefinition.Create("permission.sys.scheduled-tasks.manage", "Manage scheduled tasks")`.
  - `IScheduledTasksDbContext { DbSet<ScheduledTask> ScheduledTasks; DbSet<ScheduledTaskRun> ScheduledTaskRuns; DbSet<ScheduledTaskRunItem> ScheduledTaskRunItems; }`; `ModelBuilder ApplyScheduledTasks(this ModelBuilder)`.
  - `sealed record ScheduledTasksRegistration(string Schema, Type ContextType)` singleton per opted-in context.
  - A module context that opts in must also include `typeof(ScheduledTaskAggregateFeatureConfiguration)` in its `AggregateFeatures` (`AggregateFeatureRegistry.FromConfigurationTypes([...])`); document this on `IScheduledTasksDbContext`.

- [ ] **Step 1: Write the failing tests** in `ScheduledTaskModelTests` (test host, `tst` schema reset per class):
  - `OptedInContext_MapsTheFourTables`: the model's table names for the three entity types plus the owned targets are the four constraint names, in schema `tst`; `scheduled_tasks` carries `created_at_utc`, `deleted_at_utc`, `row_version`, `scope_id`, `scope_path`.
  - `Arguments_IsJsonb`: column type of `arguments` on tasks and runs is `jsonb`.
  - `TargetIndex_Exists`: index on `scheduled_task_targets.target_id`; run index `(task_id, started_at_utc)`.
  - `NotOptedInContext_HasNoTables_AndNoRegistration`: `DeviceManagementDbContext`'s model has no `ScheduledTask`; `services.GetServices<ScheduledTasksRegistration>()` has only `tst`.
  - `PermissionCatalogue_HasManage`: `PermissionCatalog.Contains(ScheduledTaskPermissions.Manage)`.
  - `RealHost_StillStarts`: plain `factory.CreateClient()` with no opt-in → `/` returns 200 (guards untouched).
- [ ] **Step 2:** Run `--filter ScheduledTaskModelTests`. Expected: compile failure.
- [ ] **Step 3:** Implement. Map runs and items with explicit config (no aggregate features); `Targets` with `OwnsMany` to `scheduled_task_targets`, key `(task_id, target_id)`. Enums stored as text.
- [ ] **Step 4:** Run the filter, then `--filter Aggregates|Permissions|SoftDelete|Audit|Concurrency`. Expected: PASS.
- [ ] **Step 5:** Commit.

---

### Task 4: Store, delegated identity and run-item execution

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/IScheduledTaskStore.cs`, `IScheduledTaskStoreResolver.cs`, `IScheduledTaskEngine.cs`, `IDelegatedUserContext.cs`, `DelegatedUserContext.cs`, `ScheduledCommandFactory.cs`, `ScheduledRunItemExecutor.cs`, `ScheduledTaskErrors.cs`, `ScheduledTaskOptions.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ScheduledTasks/EfScheduledTaskStore.cs`, `ScheduledTaskStoreResolver.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Hosts/ScheduledTasks/ScheduledTaskOptionsValidator.cs`, `ScheduledTaskServiceExtensions.cs` (`AddSerginScheduledTasks`, called once from `AddModuleDbContext`'s opt-in branch via a marker)
- Test: `tests/.../ScheduledTasks/FakeScheduledTaskEngine.cs`, `tests/.../ScheduledTasks/RunItemExecutionTests.cs`

**Interfaces:**
- Produces:
  - `interface IScheduledTaskEngine { Task<Guid> ScheduleAsync(ScheduledTrigger trigger, CancellationToken ct); Task RescheduleAsync(Guid engineTriggerId, ScheduledTrigger trigger, CancellationToken ct); Task SetEnabledAsync(Guid engineTriggerId, bool enabled, CancellationToken ct); Task RemoveAsync(Guid engineTriggerId, CancellationToken ct); Task<bool> ExistsAsync(Guid engineTriggerId, CancellationToken ct); Task<Guid> EnqueueRunItemAsync(ScheduledRunItemReference item, CancellationToken ct); bool IsValidCron(string cron, string timeZoneId); }`
  - `sealed record ScheduledTrigger(string Schema, ScheduledTaskId TaskId, string Cron, string TimeZoneId)`; `sealed record ScheduledRunItemReference(string Schema, Guid RunItemId)`.
  - `interface IScheduledTaskStore` (scoped, one per opted-in schema): `string Schema`; `Task<ScheduledTask?> GetTaskAsync(ScheduledTaskId id, bool ignoreScope, CancellationToken ct)`; `void AddTask(ScheduledTask)`; `void RemoveTask(ScheduledTask)`; `void AddRun(ScheduledTaskRun run, IReadOnlyCollection<ScheduledTaskRunItem> items)`; `Task<bool> HasUnfinishedRunAsync(ScheduledTaskId id, CancellationToken ct)`; `Task<(ScheduledTaskRun Run, ScheduledTaskRunItem Item)?> GetRunItemAsync(Guid runItemId, CancellationToken ct)`; `Task CompleteRunIfFinishedAsync(Guid runId, DateTime at, CancellationToken ct)`; `Task<IReadOnlyCollection<(ScheduledTaskId Id, Guid? EngineTriggerId, ScheduledTrigger Trigger)>> ListActiveTriggersAsync(CancellationToken ct)`; `Task<int> PurgeAsync(DateTime olderThanUtc, CancellationToken ct)`; `Task SaveAsync(CancellationToken ct)`.
  - `interface IScheduledTaskStoreResolver { IScheduledTaskStore For(string schema); }` (throws `InvalidOperationException` naming the schema when no module opted in).
  - `interface IDelegatedUserContext : IUserContext { Guid RunId { get; } }`; `internal sealed record DelegatedUserContext(UserId Id, Guid RunId) : IDelegatedUserContext` with `UserName = "scheduled-run"`, `FirstName = "Scheduled"`, `LastName = "Run"`, `Email = "scheduled-run@sergin.local"`.
  - `static class ScheduledCommandFactory { string ToArguments(IBaseCommand template); ErrorOr<IBaseCommand> Build(ScheduledCommand command, string arguments, Guid targetId, Guid? rootId); }` — `ToArguments` serializes the runtime type and removes `Id` and `RootId`; `Build` creates the id via `Activator.CreateInstance(command.IdType, targetId)`, serializes it into the `Id` node (and `RootId`), deserializes to `command.RequestType`; a `JsonException` gives `ScheduledTaskErrors.ArgumentsInvalid`.
  - `ScheduledTaskErrors`: `UnknownCommand` (`ScheduledTasks.UnknownCommand`), `ArgumentsInvalid` (`ScheduledTasks.ArgumentsInvalid`), `NotSchedulable` (`ScheduledTasks.NotSchedulable`, Validation), `TooManyTargets` (`ScheduledTasks.TooManyTargets`, Validation).
  - `internal sealed class ScheduledRunItemExecutor(IServiceScopeFactory scopes, ScheduledCommandCatalog catalog, IScheduledTaskStoreResolver stores, IDateTimeProvider clock, IOptions<ScheduledTaskOptions> options)` with `Task<bool> ExecuteAsync(string schema, Guid runItemId, CancellationToken ct)` — returns `true` when the engine should retry (it throws nothing to the engine). Per execution: load item and run; increment attempts; open a fresh scope; seed `UserContextAccessor.Current = new DelegatedUserContext(run.ActingUserId, run.Id)` and `IAuthorizationScopeContext.Set(run.ScopeId)`; build the command; `ISender.Send`. Outcome rule *(my call, refining the spec)*: success → `Succeeded`; any `ErrorOr` error whose type is not `Unexpected` → `Failed` with the first error's code and description, no retry; an exception or `Unexpected` → `Retrying` and return `true` while `Attempts < MaxAttempts`, else `Failed` with code `ScheduledTasks.Exhausted`. Then `CompleteRunIfFinishedAsync`.
  - `FakeScheduledTaskEngine` (test): records triggers in a dictionary, `EnqueueRunItemAsync` queues; `Task DrainAsync(IServiceProvider)` runs queued items through the executor, re-queuing on `true`; `bool FailNextCall`.

- [ ] **Step 1: Write the failing tests** in `RunItemExecutionTests` (insert run + items directly through the store, then `DrainAsync`):
  - `Success_SendsThroughThePipeline`: item `Succeeded`; the probe's `LastNote` equals the argument; the validator ran (a second item with note `"invalid"` is `Failed` with code `Note`).
  - `ActsForTheRunsUser_InTheRunsScope`: a recording handler decorator (test-only `IPipelineBehavior`) sees `IUserContext` of type `IDelegatedUserContext`, `Id == run.ActingUserId`, and `IAuthorizationScopeContext.ScopeId == run.ScopeId`; never `ISystemUserContext`.
  - `NoGrant_IsForbidden_NotRetried`: acting user without `Touch` → `Failed`, `ErrorCode == "General.Forbidden"` (use whatever code `Error.Forbidden()` produces), `Attempts == 1`.
  - `DeactivatedUser_IsForbidden`.
  - `TargetOutOfReach_IsNotFound_OthersSucceed`: three items, one probe in South while the user holds North; that item `Failed` NotFound, two `Succeeded`, run `Completed` with `Succeeded == 2, Failed == 1`.
  - `Exception_IsRetried_ThenSucceeds`: a handler that throws once (switch), then succeeds → `Succeeded`, `Attempts == 2`.
  - `Exception_Exhausts_AtMaxAttempts`: always throws, `MaxAttempts = 3` → `Failed`, `ErrorCode == "ScheduledTasks.Exhausted"`, `Attempts == 3`.
  - `Arguments_RoundTrip_ValueObjects`: `ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("hello")))` then `Build(...)` gives `Note.Value == "hello"` and `Id.Value == targetId`; the JSON has no `Id` key.
  - `ChildCommand_GetsRootIdAndId`.
- [ ] **Step 2:** Run `--filter RunItemExecutionTests`. Expected: compile failure.
- [ ] **Step 3:** Implement. `EfScheduledTaskStore<TContext>` uses `IgnoreQueryFilters(["Sergin:Scope"])` when `ignoreScope` is true (and always for run/run-item reads, which are not scoped). `AddSerginScheduledTasks` registers catalogue-dependent services, the executor, resolver, options + validator, once.
- [ ] **Step 4:** Run the filter. Expected: PASS.
- [ ] **Step 5:** Commit.

---

### Task 5: Starting runs — cron fire, overlap, run now, one-off

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/IScheduledTaskRunner.cs`, `ScheduledTaskRunner.cs`, `ScheduledRunStarter.cs`
- Test: `tests/.../ScheduledTasks/RunStartTests.cs`

**Interfaces:**
- Consumes: Task 4's store, engine, executor.
- Produces:
  - `public interface IScheduledTaskRunner { Task FireAsync(string schema, ScheduledTaskId taskId, CancellationToken ct); Task<bool> ExecuteRunItemAsync(string schema, Guid runItemId, CancellationToken ct); }` — the only two calls an engine adapter makes.
  - `internal sealed class ScheduledRunStarter { Task<ErrorOr<Guid>> StartAsync(IScheduledTaskStore store, ScheduledRunStart start, CancellationToken ct); }` with `sealed record ScheduledRunStart(ScheduledTaskId? TaskId, ScheduledRunTrigger Trigger, UserId ActingUserId, Guid ScopeId, string ScopePath, string CommandName, string Arguments, IReadOnlyCollection<ScheduledTaskTarget> Targets)` — writes the run (copying name and arguments) and one item per target, saves, then enqueues each item and stores its `EngineJobId`.
  - `FireAsync`: task missing, deleted or paused → nothing; `HasUnfinishedRunAsync` → write a `Skipped` run with `Total = 0`; else start a `Scheduled` run acting for `DelegatingUserId` in the task's `ScopeId`/`ScopePath`.

- [ ] **Step 1: Write the failing tests** in `RunStartTests`:
  - `Fire_StartsARun_ForTheDelegatingUser`: run `Trigger == Scheduled`, `ActingUserId == task.DelegatingUserId`, `Total == targets.Count`, run `CommandName`/`Arguments` equal the task's.
  - `Fire_PausedTask_DoesNothing`; `Fire_DeletedTask_DoesNothing`.
  - `Fire_WhilePreviousUnfinished_RecordsSkipped`: first fire not drained, second fire → second run `Skipped`, no new items.
  - `Fire_AfterTaskEdited_HistoryKeepsOldArguments`: run once, update the task's arguments, assert the first run's `Arguments` unchanged.
  - `Fire_UnknownCommandName_FailsItems` (Review Focus 1): task with `CommandName = "tst.probes.gone"` → every item `Failed` with `ScheduledTasks.UnknownCommand`, run `Completed`.
  - `Fire_BadArguments_FailsItems` (Review Focus 2): arguments `{"Note":42}` → `ScheduledTasks.ArgumentsInvalid`, `Attempts == 1`.
  - `Fire_DelegatingUserLostReach_StillLoads_ItemsForbidden` (Review Focus 4).
- [ ] **Step 2:** Run `--filter RunStartTests`. Expected: compile failure.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `--filter RunStartTests|RunItemExecutionTests`. Expected: PASS.
- [ ] **Step 5:** Commit.

---

### Task 6: Read side — tasks for a record, runs, run items, one-off runs

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/Queries/` — `GetScheduledTasksForTargetQuery.cs`, `GetScheduledTaskByIdQuery.cs`, `GetScheduledTaskRunsQuery.cs`, `GetScheduledTaskRunItemsQuery.cs`, `GetOneOffRunsForTargetQuery.cs`, their responses and handlers, and `ScheduledTaskQueryConfigurations.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ScheduledTasks/ScheduledTaskQueries.cs` (raw SQL via `IDbConnectionFactory`, schema from the request)
- Test: `tests/.../ScheduledTasks/ScheduledTaskQueryTests.cs`

**Interfaces:**
- Produces (every request carries `string Schema` first; all `RequirePermissions(ScheduledTaskPermissions.Manage)`; configurations registered through one `ModuleConfigurationSource.FromTypes(...)` added by `AddSerginScheduledTasks`; handlers registered by hand there):
  - `GetScheduledTasksForTargetQuery(string Schema, Guid TargetId) : IQuery<IReadOnlyCollection<ScheduledTaskListItem>>`; `ScheduledTaskListItem(Guid Id, string CommandName, string Cron, string TimeZoneId, ScheduledTaskState State, DateTime? LastRunAtUtc, ScheduledRunStatus? LastRunStatus, int? LastRunFailed, int TargetCount)`.
  - `GetScheduledTaskByIdQuery(string Schema, ScheduledTaskId Id)` → `ScheduledTaskResponse(Guid Id, string CommandName, string Arguments, string Cron, string TimeZoneId, ScheduledTaskState State, Guid DelegatingUserId, IReadOnlyCollection<ScheduledTaskTarget> Targets)`; handler derives from `VersionedQueryHandler`.
  - `GetScheduledTaskRunsQuery(string Schema, ScheduledTaskId TaskId, Paggination Paggination)` → `ListQueryResponse<ScheduledRunListItem>`; `ScheduledRunListItem(Guid Id, ScheduledRunTrigger Trigger, Guid ActingUserId, ScheduledRunStatus Status, DateTime StartedAtUtc, DateTime? CompletedAtUtc, int Total, int Succeeded, int Failed)`.
  - `GetScheduledTaskRunItemsQuery(string Schema, Guid RunId)` → `IReadOnlyCollection<ScheduledRunItemListItem>` (`TargetId, Status, Attempts, ErrorCode, ErrorDescription, CompletedAtUtc`).
  - `GetOneOffRunsForTargetQuery(string Schema, Guid TargetId, int Take)` → `IReadOnlyCollection<ScheduledRunListItem>`.
  - Every SQL over `scheduled_tasks` adds `deleted_at_utc IS NULL` and `ScopeColumns.ReachSql` with `ScopeColumns.AddParameters`; runs are reached through their task's scope, one-off runs through their own `scope_path`.

- [ ] **Step 1: Write the failing tests**:
  - `TasksForTarget_ListsOnlyTasksTargetingIt`, with last-run columns filled after one drained fire.
  - `TasksForTarget_HidesDeleted`, `TasksForTarget_HidesOutOfReach` (South task not visible to a North holder).
  - `Runs_NewestFirst_Paged`; `RunItems_CarryErrors`; `OneOffRuns_ForTarget_NewestFirst`.
  - `WithoutManage_IsForbidden`.
- [ ] **Step 2:** Run `--filter ScheduledTaskQueryTests`. Expected: compile failure.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the filter. Expected: PASS.
- [ ] **Step 5:** Commit.

---

### Task 7: Management commands

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ScheduledTasks/Commands/` — `CreateScheduledTaskCommand.cs`, `UpdateScheduledTaskCommand.cs`, `PauseScheduledTaskCommand.cs`, `ResumeScheduledTaskCommand.cs`, `DeleteScheduledTaskCommand.cs`, `RunScheduledTaskNowCommand.cs`, `RunCommandOnceCommand.cs`, handlers, validators, `ScheduledTaskCommandConfigurations.cs`, `ScheduledTaskAuthority.cs`
- Test: `tests/.../ScheduledTasks/ScheduledTaskCommandTests.cs`, `tests/.../ScheduledTasks/FailingScheduledTaskEngine.cs`

**Interfaces:**
- Consumes: catalogue (Task 2), store/engine (Task 4), `ScheduledRunStarter` (Task 5).
- Produces:
  - `CreateScheduledTaskCommand(string Schema, IBaseCommand Template, IReadOnlyCollection<ScheduledTaskTarget> Targets, string Cron, string TimeZoneId) : ICommand<CreateScheduledTaskCommandResponse>, ICreateCommand<ScheduledTask, ScheduledTaskId>`; response `(Guid Id) : ICreatedResponse`.
  - `UpdateScheduledTaskCommand(string Schema, ScheduledTaskId Id, IBaseCommand Template, IReadOnlyCollection<ScheduledTaskTarget> Targets, string Cron, string TimeZoneId) : ICommand<UpdateScheduledTaskCommandResponse>, IUpdateCommand<ScheduledTask, ScheduledTaskId>` — version-guarded by type.
  - `PauseScheduledTaskCommand(string Schema, ScheduledTaskId Id)`, `ResumeScheduledTaskCommand(...)` (`ITransitionCommand`, a reverse pair), `DeleteScheduledTaskCommand(...)` (`IDeleteCommand`) — all version-guarded.
  - `RunScheduledTaskNowCommand(string Schema, ScheduledTaskId Id) : ICommand<ScheduledRunStartedResponse>` with `WithoutExpectedVersion()` (it changes nothing on the task); acts for the **caller**.
  - `RunCommandOnceCommand(string Schema, IBaseCommand Template, IReadOnlyCollection<ScheduledTaskTarget> Targets) : ICommand<ScheduledRunStartedResponse>` (untyped, configured) — one-off, no task row, acts for the caller in the caller's current scope.
  - `ScheduledRunStartedResponse(Guid RunId)`.
  - `internal sealed class ScheduledTaskAuthority(IScopeAuthorization auth, CommandConfigurationRegistry registry)` with `Task<ErrorOr<Success>> EnsureMayScheduleAsync(Type requestType, CancellationToken ct)`: the command's `RequiredPermissions`, each checked on the current scope for `PermissionScope.Current` and with `CurrentUserHoldsAnywhereAsync` for `PermissionScope.Any`. `Manage` is required by every configuration above through the pipeline.
  - Validators: `Template` type must be in the catalogue (`ScheduledTasks.NotSchedulable`); `Targets` non-empty, distinct, and count 1 unless the command is `Selectable` (`ScheduledTasks.TooManyTargets`); child commands require `RootId` on every target; `Cron`/`TimeZoneId` pass `IScheduledTaskEngine.IsValidCron` (codes `Cron`, `TimeZoneId`); `Template`'s own validator is **not** run here (it runs per item at run time).
  - Create/Update handler order: authority check → build entity → `engine.ScheduleAsync`/`RescheduleAsync` → `LinkTrigger` → save; if the save throws, `engine.RemoveAsync` the new trigger; if the engine throws, nothing is saved and the handler returns `Error.Unexpected("ScheduledTasks.EngineUnavailable")`. Pause/Resume call `SetEnabledAsync`; Delete calls `RemoveAsync` after the soft delete saves.

- [ ] **Step 1: Write the failing tests**:
  - `Create_StoresTask_AndSchedulesTrigger`: fake engine holds one trigger whose `TaskId` matches; task `DelegatingUserId == caller`; `scope_id == caller's current scope`; audit `created_by == caller`.
  - `Create_UnconfiguredCommand_IsRefused` (`ScheduledTasks.NotSchedulable`).
  - `Create_SeveralTargets_NonSelectable_IsRefused` (`ScheduledTasks.TooManyTargets`).
  - `Create_BadCron_IsRefused`, `Create_BadTimeZone_IsRefused` (Review Focus 3): validation errors on `Cron` / `TimeZoneId`, nothing saved, no trigger.
  - `Create_CallerLacksCommandPermission_IsForbidden`; `Create_CallerLacksManage_IsForbidden`.
  - `Create_EngineFails_SavesNothing` (Review Focus 5): `FailingScheduledTaskEngine` → error, zero rows in `scheduled_tasks`.
  - `Update_BySecondUser_MakesThemDelegating`: after update, `DelegatingUserId == second`; next fire's run `ActingUserId == second`.
  - `Update_WithoutVersion_Is428`, `Update_StaleVersion_Is412` (via `ISerginDispatcher.SendVersionedAsync` vs plain send).
  - `Pause_ThenFire_DoesNothing`; `Resume_ThenFire_Runs`; `Delete_SoftDeletes_RemovesTrigger_KeepsRuns`.
  - `RunNow_ActsForClicker_NotDelegatingUser`.
  - `RunOnce_LeavesNoTask_AndRunHasNoTaskId`: zero `scheduled_tasks` rows, one run with `TaskId == null`, `Trigger == OneOff`, items succeed after drain.
- [ ] **Step 2:** Run `--filter ScheduledTaskCommandTests`. Expected: compile failure.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `--filter ScheduledTasks`. Expected: PASS.
- [ ] **Step 5:** Commit.

---

### Task 8: TickerQ engine, trigger rebuild at start, purge

**Files:**
- Create: `.../Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ/TickerQScheduledTaskEngine.cs`, `SerginTickerFunctions.cs`, `ScheduledTriggerRequest.cs`
- Delete: `.../TickerQ/ProbeFunctions.cs` (and its test, replaced below)
- Create: `src/SharedKernel/Sergin.SharedKernel.Hosts/ScheduledTasks/ScheduledTaskTriggerRebuildService.cs`, `ScheduledTaskPurgeService.cs`
- Modify: `AddSerginTickerQ` (registers the engine as `IScheduledTaskEngine`)
- Test: `tests/.../ScheduledTasks/TickerQEngineTests.cs`, `tests/.../ScheduledTasks/TriggerRebuildTests.cs`, `tests/.../ScheduledTasks/PurgeTests.cs`

**Interfaces:**
- Consumes: `IScheduledTaskRunner` (Task 5), `IScheduledTaskStoreResolver`, `ScheduledTaskOptions`.
- Produces:
  - `TickerQScheduledTaskEngine : IScheduledTaskEngine` over `ICronTickerManager<CronTickerEntity>` and `ITimeTickerManager<TimeTickerEntity>`. Cron ticker: `Function = "Sergin.ScheduledTasks.Fire"`, `Expression = cron`, `TimeZoneId = timeZoneId`, `Request = TickerHelper.CreateTickerRequest(new ScheduledTriggerRequest(schema, taskId))`, `SkipIfAlreadyRunning = false` (overlap is Sergin's). Run item: time ticker `Function = "Sergin.ScheduledTasks.Execute"`, no `ExecutionTime`, `Retries = 0`. Retry is Sergin's: when `ExecuteRunItemAsync` returns `true`, the function enqueues a new time ticker with `ExecutionTime = now + RetryIntervalsSeconds[min(attempts-1, len-1)]`. Every manager result is checked with `IsSucceeded`; a failure throws `InvalidOperationException` with the result's exception as inner. `IsValidCron` uses TickerQ's own cron parser and `TimeZoneInfo.FindSystemTimeZoneById`.
  - `SerginTickerFunctions`: `[TickerFunction("Sergin.ScheduledTasks.Fire")] Task FireAsync(TickerFunctionContext<ScheduledTriggerRequest> context, CancellationToken ct)` and `[TickerFunction("Sergin.ScheduledTasks.Execute")] Task ExecuteAsync(TickerFunctionContext<ScheduledRunItemReference> context, CancellationToken ct)`; each resolves `IScheduledTaskRunner` from a fresh scope.
  - `ScheduledTaskTriggerRebuildService : IHostedService` — at start, for every store: each active, non-deleted task whose `EngineTriggerId` is null or `!ExistsAsync` gets `ScheduleAsync` + `LinkTrigger` + save; a paused one gets its trigger disabled.
  - `ScheduledTaskPurgeService : BackgroundService` — every `PurgeInterval`, each store's `PurgeAsync(now - Retention)` deletes completed/skipped runs (and their items, by cascade) started before the cutoff.

- [ ] **Step 1: Write the failing tests**:
  - `TickerQEngineTests.CronFire_RunsTheCommand_EndToEnd` (`[RequiresTickerQLicenseFact]`): create a task with cron `* * * * *` through `ISerginDispatcher`, poll ≤ 90 s; one run `Completed`, all items `Succeeded`.
  - `TickerQEngineTests.RunOnce_EndToEnd` (`[RequiresTickerQLicenseFact]`): items `Succeeded` within 30 s.
  - `TickerQEngineTests.TransientFailure_IsRetriedThroughTickerQ` (`[RequiresTickerQLicenseFact]`, `RetryIntervalsSeconds = [1]`): throws once, ends `Succeeded`, `Attempts == 2`.
  - `TickerQEngineTests.InvalidCron_IsInvalid`: `IsValidCron("not a cron", "UTC") == false`, `IsValidCron("0 2 * * *", "Asia/Tehran") == true` (no licence needed if the parser works without the scheduler; otherwise mark it licence-gated).
  - `TriggerRebuildTests.MissingTrigger_IsRecreatedAtStart` (fake engine): task with `EngineTriggerId` the engine doesn't know → after host start, the engine knows it and the row holds the new id.
  - `PurgeTests.OldRuns_ArePurged_RecentKept`.
- [ ] **Step 2:** Run `--filter TickerQEngineTests|TriggerRebuildTests|PurgeTests`. Expected: compile failure.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `--filter ScheduledTasks` with `SERGIN_TICKERQ_LICENSE` set. Expected: PASS, no skips.
- [ ] **Step 5:** Commit.

---

### Task 9: Generic UI parts

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/ScheduledTasks/` — `ScheduledTaskUiTexts.cs`, `SerginScheduleAction.cs` (a factory returning a `SerginPageAction` and a `SerginBulkAction<TItem>`), `SerginScheduleDialog.razor{,.cs}`, `SerginCronPicker.razor{,.cs}`, `SerginScheduledTasksSection.razor{,.cs}`, `SerginScheduledRunsDialog.razor{,.cs}`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/_Imports.razor`, `GlobalUsings.cs`
- Test: `tests/.../ScheduledTasks/ScheduledTaskUiTests.cs`; add the test page route to `tests/.../Localization/PseudoLocaleSweepTests.cs`
- Test page: `tests/.../ScheduledTasks/ScheduleProbePage.razor{,.cs}` (`@page "/tst/probes/{Id:guid}"`, hosts the section and the action, registered for the test host only through a test `ISerginWebUiModule` with schema `tst`)

**Interfaces:**
- Consumes: Task 6 queries, Task 7 commands, `ISerginDispatcher`, `IUiTimeZoneStore`, `IScopeAuthorization`, `IDialogService`, `IUiErrorPresenter`.
- Produces:
  - `SerginScheduleDialog` parameters: `string Schema`, `TextDefinition CommandTitle`, `Func<IBaseCommand> BuildTemplate`, `RenderFragment? ArgumentsForm`, `IReadOnlyCollection<ScheduledTaskTarget> Targets`, `IReadOnlyList<string> TargetLabels`, `ScheduledTaskId? EditingId`. Buttons: "Run now" → `RunCommandOnceCommand`; "Save" → create or update (`SendVersionedAsync` when editing). Errors via `ErrorPresenter.Notify(result.Errors)`.
  - `SerginCronPicker`: choices Daily at (time), Hourly at (minute), Weekly on (day, time), Custom (cron text); shows the zone (default `IUiTimeZoneStore` zone) and the next three fire times (`ICultureFormatter`). Two-way `Cron`, `TimeZoneId`.
  - `SerginScheduledTasksSection` parameters: `string Schema`, `Guid TargetId`. Embedded outlined section titled "Scheduled tasks" with count; columns Command, Schedule, Next run, Last outcome, State; row commands Run now, Pause/Resume, Edit, Delete (delete through `IDialogService.ConfirmDeleteAsync`); row click opens `SerginScheduledRunsDialog`; below, "Recent one-off runs" (`GetOneOffRunsForTargetQuery`, `Take = 5`). Hidden entirely when the user lacks `ScheduledTaskPermissions.Manage`.
  - `ScheduledTaskUiTexts` keys under `common.scheduled-tasks.*`: `title`, `schedule-action`, `run-now`, `save`, `run-once-started`, `saved`, `paused`, `resumed`, `deleted`, `delete-confirm` (`Delete the schedule for {Command}?`), `columns.*`, `states.active`, `states.paused`, `runs.*`, `cron.daily`, `cron.hourly`, `cron.weekly`, `cron.custom`, `cron.next-runs`, `one-off-runs`.

- [ ] **Step 1: Write the failing tests** in `ScheduledTaskUiTests` (prerendered HTML through `factory.CreateClient()` on the test host):
  - `Section_Prerenders_TasksForTheRecord`: page HTML contains the section title and the task's cron.
  - `Section_Hidden_WithoutManage`.
  - `ScheduleAction_Hidden_WithoutCommandPermission`.
  - `CronPicker_DailyAt0200_Produces_0_2_Star`: unit-level, `SerginCronPicker.ToCron(CronChoice.Daily, new TimeOnly(2, 0)) == "0 2 * * *"` (make `ToCron` an `internal static` helper with `InternalsVisibleTo` for the test project).
  - Pseudo-locale sweep includes `/tst/probes/{id}` and passes.
- [ ] **Step 2:** Run `--filter ScheduledTaskUiTests|PseudoLocaleSweepTests`. Expected: FAIL.
- [ ] **Step 3:** Implement, following `DeviceModelListView` for the section and `SerginFormView` for the dialog's `MudForm` (validation through `FormValidator.RulesFor(ToCommand)` against `CreateScheduledTaskCommand`).
- [ ] **Step 4:** Run the filter. Expected: PASS. Then one headless browser check (see memory `headless-browser-checks`) on the test host: open the dialog, pick Daily 02:00, Save, see the row.
- [ ] **Step 5:** Commit.

---

### Task 10: Documentation and full verification

**Files:**
- Modify: `src/SharedKernel/.claude/CLAUDE.md` (a "Scheduled tasks" section: opt-in, `Schedulable(name)`, delegation, the engine seam, TickerQ licence key), `.claude/CLAUDE.md` (Cross-cutting conventions bullet; test-folder line for `ScheduledTasks/`; note that no module opts in yet and the real host is not wired)

- [ ] **Step 1:** Write both sections.
- [ ] **Step 2:** Run `dotnet build Sergin.MeterMinder.slnx` and `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All` (Docker running, licence env var set). Expected: build clean, all tests pass, zero skips.
- [ ] **Step 3:** Run `dotnet test` once **without** the licence env var. Expected: TickerQ tests reported as skipped with the reason, everything else passes.
- [ ] **Step 4:** Run `graphify update .` then `python .claude/skills/graphify/scripts/graphify_repair.py`.
- [ ] **Step 5:** Commit (SharedKernel, then root with pointer bump). Then `superpowers:finishing-a-development-branch`.
