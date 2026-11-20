using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Application.Tasks.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Hosts.Commands;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.RevokePermission;
using Sergin.Administration.UserAccess.Domain.Roles;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// The management commands: creating, editing, pausing, resuming and deleting a schedule, running a saved one now, and
/// running a command once without saving. Each keeps Sergin's row and the engine's trigger in step, refuses a command
/// that is not schedulable or a trigger the engine cannot run, checks the command's own permissions besides Manage,
/// and acts for the person who chose the run: the last saver for a scheduled run, the clicker for a run now.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TaskScheduleCommandTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Schema = TasksDbContext.Schema;
    private const string Cron = "0 * * * *";

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;
    private TestCaller manager = default!;

    private FakeTaskEngine Engine => host.Services.GetRequiredService<FakeTaskEngine>();

    public async Task InitializeAsync()
    {
        host = CreateHost(services =>
        {
            services.AddSingleton<FakeTaskEngine>();
            services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(provider => provider.GetRequiredService<FakeTaskEngine>()));
        });

        await TaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
        manager = await ManagerAsync(tree.North);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task Create_StoresTask_AndSchedulesTrigger()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(manager, tree.North, Create(Touch("hello"), probe));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Code : string.Empty);
        var id = new TaskScheduleId(created.Value.Id);
        (Guid triggerId, (TaskScheduleTrigger trigger, bool enabled)) = Assert.Single(Engine.Triggers);
        Assert.Equal(id, trigger.ScheduleId);
        Assert.Equal(Schema, trigger.Schema);
        Assert.Equal(Cron, trigger.Cron);
        Assert.True(enabled);

        (TaskSchedule task, Guid scopeId, Guid createdBy) = await StoredAsync(id);
        Assert.Equal(manager.Id, task.DelegatingUserId);
        Assert.Equal(triggerId, task.EngineTriggerId);
        Assert.Equal("tst.probes.touch", task.CommandName);
        Assert.Equal(new TaskScheduleTarget(probe.Id.Value, null), Assert.Single(task.Targets));
        Assert.Equal(tree.North, scopeId);
        Assert.Equal(manager.Id.Value, createdBy);
    }

    [Fact]
    public async Task Create_UnconfiguredCommand_IsRefused()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(
            manager, tree.North, Create(new UnconfiguredProbeCommand(new(Guid.Empty), new ProbeNote("x")), probe));

        Assert.Contains(created.Errors, error => error.Code == TaskErrors.NotSchedulable.Code && error.Type == ErrorType.Validation);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_SeveralTargets_NonSelectable_IsRefused()
    {
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder2);

        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(
            manager, tree.North, Create(new TouchOneProbeCommand(new(Guid.Empty), new ProbeNote("x")), first, second));

        Assert.Contains(created.Errors, error => error.Code == TaskErrors.TooManyTargets.Code);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_DuplicateTargets_AreRefused()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        // The targets' key is (schedule_id, target_id): the same id twice collides.
        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(
            manager,
            tree.North,
            new CreateTaskScheduleCommand(Schema, Touch("x"), [new(probe.Id.Value, null), new(probe.Id.Value, null)], Cron, "UTC"));

        Assert.Contains(created.Errors, error => error.Code == nameof(CreateTaskScheduleCommand.Targets));
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_NoTargets_IsRefused()
    {
        ErrorOr<CreateTaskScheduleCommandResponse> none = await SendAsync(
            manager, tree.North, new CreateTaskScheduleCommand(Schema, Touch("x"), [], Cron, "UTC"));

        Assert.Contains(none.Errors, error => error.Code == nameof(CreateTaskScheduleCommand.Targets));
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_BadCron_IsRefused()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(
            manager, tree.North, Create(Touch("x"), probe) with { Cron = "every hour" });

        Error error = Assert.Single(created.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(CreateTaskScheduleCommand.Cron), error.Code);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_BadTimeZone_IsRefused()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(
            manager, tree.North, Create(Touch("x"), probe) with { TimeZoneId = "Mars/Olympus_Mons" });

        Error error = Assert.Single(created.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(CreateTaskScheduleCommand.TimeZoneId), error.Code);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_CallerLacksCommandPermission_IsForbidden()
    {
        TestCaller manageOnly = await tree.CreateGrantedCallerAsync(tree.North, TaskPermissions.Manage.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(manageOnly, tree.North, Create(Touch("x"), probe));

        Assert.Equal(ErrorType.Forbidden, created.FirstError.Type);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_CallerLacksManage_IsForbidden()
    {
        TestCaller touchOnly = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(touchOnly, tree.North, Create(Touch("x"), probe));

        Assert.Equal(ErrorType.Forbidden, created.FirstError.Type);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_EngineFails_SavesNothing()
    {
        await using WebApplicationFactory<Program> failing = CreateHost(services =>
        {
            services.AddSingleton<FailingTaskEngine>();
            services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(provider => provider.GetRequiredService<FailingTaskEngine>()));
        });
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(failing, manager, tree.North, Create(Touch("x"), probe));

        Assert.Equal(ErrorType.Unexpected, created.FirstError.Type);
        Assert.Equal(TaskErrors.EngineUnavailable.Code, created.FirstError.Code);
        Assert.Equal(1, failing.Services.GetRequiredService<FailingTaskEngine>().Calls);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Update_BySecondUser_MakesThemDelegating()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);
        TestCaller second = await ManagerAsync(tree.North);

        ErrorOr<UpdateTaskScheduleCommandResponse> updated =
            await SendAtCurrentVersionAsync(second, id, Update(id, Touch("edited"), probe) with { Cron = "30 * * * *" });

        Assert.False(updated.IsError, updated.IsError ? updated.FirstError.Code : string.Empty);
        (TaskSchedule task, _, _) = await StoredAsync(id);
        Assert.Equal(second.Id, task.DelegatingUserId);
        Assert.Equal("30 * * * *", Assert.Single(Engine.Triggers).Value.Trigger.Cron);

        await FireAsync(id);

        TaskRun run = Assert.Single(await RunsAsync(id));
        Assert.Equal(second.Id, run.ActingUserId);
        Assert.Contains("edited", run.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_WithoutVersion_Is428()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);

        ErrorOr<UpdateTaskScheduleCommandResponse> updated = await SendAsync(manager, tree.North, Update(id, Touch("edited"), probe));

        Assert.Equal(VersionErrors.Required.Code, updated.FirstError.Code);
        Assert.DoesNotContain("edited", (await StoredAsync(id)).Task.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_StaleVersion_Is412_AndTriggerIsRestored()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);

        ErrorOr<Versioned<UpdateTaskScheduleCommandResponse>> updated = await SendVersionedAsync(
            manager, tree.North, Update(id, Touch("edited"), probe) with { Cron = "30 * * * *" }, RowVersion.Create());

        Assert.True(VersionErrors.IsStale(updated.FirstError));
        Assert.DoesNotContain("edited", (await StoredAsync(id)).Task.Arguments, StringComparison.Ordinal);
        Assert.Equal(Cron, Assert.Single(Engine.Triggers).Value.Trigger.Cron);
    }

    [Fact]
    public async Task Update_EditorLacksCommandPermission_IsForbidden_TaskUnchanged()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);
        (TaskSchedule before, _, _) = await StoredAsync(id);
        TestCaller manageOnly = await tree.CreateGrantedCallerAsync(tree.North, TaskPermissions.Manage.Permission);

        ErrorOr<UpdateTaskScheduleCommandResponse> updated =
            await SendAtCurrentVersionAsync(manageOnly, id, Update(id, Touch("edited"), probe) with { Cron = "30 * * * *" });

        Assert.Equal(ErrorType.Forbidden, updated.FirstError.Type);
        (TaskSchedule after, _, _) = await StoredAsync(id);
        Assert.Equal(Cron, after.Cron);
        Assert.Equal(before.Arguments, after.Arguments);
        Assert.DoesNotContain("edited", after.Arguments, StringComparison.Ordinal);
        Assert.Equal(manager.Id, after.DelegatingUserId);
        Assert.Equal(before.EngineTriggerId, after.EngineTriggerId);
        (TaskScheduleTrigger trigger, bool enabled) = Engine.Triggers[after.EngineTriggerId!.Value];
        Assert.Equal(Cron, trigger.Cron);
        Assert.True(enabled);
    }

    [Fact]
    public async Task Pause_ThenFire_DoesNothing()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);

        ErrorOr<Success> paused = await SendAtCurrentVersionAsync(manager, id, new PauseTaskScheduleCommand(Schema, id));

        Assert.False(paused.IsError, paused.IsError ? paused.FirstError.Code : string.Empty);
        Assert.Equal(TaskScheduleState.Paused, (await StoredAsync(id)).Task.State);
        Assert.False(Assert.Single(Engine.Triggers).Value.Enabled);

        await FireAsync(id);

        Assert.Empty(await RunsAsync(id));
    }

    [Fact]
    public async Task Resume_ThenFire_Runs()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);
        Assert.False((await SendAtCurrentVersionAsync(manager, id, new PauseTaskScheduleCommand(Schema, id))).IsError);

        ErrorOr<Success> resumed = await SendAtCurrentVersionAsync(manager, id, new ResumeTaskScheduleCommand(Schema, id));

        Assert.False(resumed.IsError, resumed.IsError ? resumed.FirstError.Code : string.Empty);
        Assert.True(Assert.Single(Engine.Triggers).Value.Enabled);

        await FireAsync(id);

        Assert.Equal(TaskRunTrigger.Schedule, Assert.Single(await RunsAsync(id)).Trigger);
    }

    [Fact]
    public async Task Delete_SoftDeletes_RemovesTrigger_KeepsRuns()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);
        await FireAsync(id);

        ErrorOr<Success> deleted = await SendAtCurrentVersionAsync(manager, id, new DeleteTaskScheduleCommand(Schema, id));

        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Code : string.Empty);
        Assert.Empty(Engine.Triggers);
        Assert.Single(await RunsAsync(id));

        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        Assert.False(await context.TaskSchedules.IgnoreQueryFilters([ScopeColumns.QueryFilterName]).AnyAsync(task => task.Id == id));
        Assert.True(await context.TaskSchedules.IgnoreQueryFilters().AnyAsync(task => task.Id == id));
    }

    [Fact]
    public async Task Delete_WithoutVersion_Is428()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);

        ErrorOr<Success> deleted = await SendAsync(manager, tree.North, new DeleteTaskScheduleCommand(Schema, id));

        Assert.Equal(VersionErrors.Required.Code, deleted.FirstError.Code);
        Assert.Single(Engine.Triggers);
    }

    [Fact]
    public async Task RunNow_ActsForClicker_NotDelegatingUser()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);
        TestCaller clicker = await ManagerAsync(tree.North);

        ErrorOr<TaskScheduleRunStartedResponse> started = await SendAsync(clicker, tree.North, new RunTaskScheduleNowCommand(Schema, id));

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        TaskRun run = Assert.Single(await RunsAsync(id));
        Assert.Equal(started.Value.RunId, run.Id);
        Assert.Equal(TaskRunTrigger.ScheduleNow, run.Trigger);
        Assert.Equal(clicker.Id, run.ActingUserId);
        Assert.Equal(tree.North, run.ScopeId);
        Assert.Equal(manager.Id, (await StoredAsync(id)).Task.DelegatingUserId);
    }

    [Fact]
    public async Task RunNow_WhilePreviousUnfinished_RecordsSkipped()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);
        await FireAsync(id);

        ErrorOr<TaskScheduleRunStartedResponse> started = await SendAsync(manager, tree.North, new RunTaskScheduleNowCommand(Schema, id));

        Assert.Equal(TaskErrors.Overlap.Code, started.FirstError.Code);
        IReadOnlyCollection<TaskRun> runs = await RunsAsync(id);
        TaskRun skipped = Assert.Single(runs, run => run.Status == TaskRunStatus.Skipped);
        Assert.Equal(TaskRunTrigger.ScheduleNow, skipped.Trigger);
        Assert.Equal(manager.Id, skipped.ActingUserId);
    }

    [Fact]
    public async Task RunNow_CallerLacksCommandPermission_IsForbidden()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);
        TestCaller manageOnly = await tree.CreateGrantedCallerAsync(tree.North, TaskPermissions.Manage.Permission);

        ErrorOr<TaskScheduleRunStartedResponse> started = await SendAsync(manageOnly, tree.North, new RunTaskScheduleNowCommand(Schema, id));

        Assert.Equal(ErrorType.Forbidden, started.FirstError.Type);
        Assert.Empty(await RunsAsync(id));
    }

    [Fact]
    public async Task RunNow_OutOfReach_IsNotFound()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(manager, probe);
        TestCaller southManager = await ManagerAsync(tree.South);

        ErrorOr<TaskScheduleRunStartedResponse> started = await SendAsync(southManager, tree.South, new RunTaskScheduleNowCommand(Schema, id));

        Assert.Equal(ErrorType.NotFound, started.FirstError.Type);
        Assert.Empty(await RunsAsync(id));
    }

    [Fact]
    public async Task RunOnce_LeavesNoTask_AndRunHasNoScheduleId()
    {
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder2);

        ErrorOr<TaskScheduleRunStartedResponse> started = await SendAsync(
            manager,
            tree.North,
            new RunTaskOnceCommand(Schema, Touch("once"), [new(first.Id.Value, null), new(second.Id.Value, null)]));

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        await Engine.DrainAsync(host.Services);

        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        Assert.False(await context.TaskSchedules.IgnoreQueryFilters().AnyAsync());
        TaskRun run = await context.TaskRuns.AsNoTracking().SingleAsync(one => one.Id == started.Value.RunId);
        Assert.Null(run.ScheduleId);
        Assert.Equal(TaskRunTrigger.Once, run.Trigger);
        Assert.Equal(manager.Id, run.ActingUserId);
        Assert.Equal(tree.North, run.ScopeId);
        Assert.Equal(TaskRunStatus.Completed, run.Status);
        Assert.Equal(2, run.Succeeded);
        Assert.All(
            await context.TaskRunItems.AsNoTracking().Where(item => item.RunId == run.Id).ToListAsync(),
            item => Assert.Equal(TaskRunItemStatus.Succeeded, item.Status));
        Assert.Equal("once", (await context.Probes.AsNoTracking().IgnoreQueryFilters().SingleAsync(probe => probe.Id == first.Id)).LastNote);
    }

    [Fact]
    public async Task RunOnce_CallerLacksCommandPermission_IsForbidden()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TestCaller manageOnly = await tree.CreateGrantedCallerAsync(tree.North, TaskPermissions.Manage.Permission);

        ErrorOr<TaskScheduleRunStartedResponse> started =
            await SendAsync(manageOnly, tree.North, new RunTaskOnceCommand(Schema, Touch("once"), [new(probe.Id.Value, null)]));

        Assert.Equal(ErrorType.Forbidden, started.FirstError.Type);
        Assert.Empty(Engine.QueuedItems);
        using IServiceScope scope = host.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<TasksDbContext>().TaskRuns.AnyAsync());
    }

    [Fact]
    public async Task Fire_AfterTheDelegatingUsersPermissionIsRevoked_FailsTheItemForbidden_WithoutRetrying()
    {
        Guid role = await tree.CreateRoleAsync(
            tree.North, $"delegate-{Guid.NewGuid():N}"[..20], inheritable: true, TaskPermissions.Manage.Permission, ScheduleProbePermissions.Touch.Permission);
        Guid user = await tree.CreateUserAsync("delegate");
        Assert.False((await tree.AssignAsync(user, role, tree.North, includeSubtree: true)).IsError);
        var delegating = new TestCaller { Id = new(user) };
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskScheduleId id = await CreateTaskAsync(delegating, probe);

        await FireAsync(id);
        await Engine.DrainAsync(host.Services);

        TaskRun first = Assert.Single(await RunsAsync(id));
        Assert.Equal(TaskRunItemStatus.Succeeded, Assert.Single(await ItemsAsync(first.Id)).Status);

        ErrorOr<RevokeRolePermissionCommandResponse> revoked =
            await tree.SendAtRoleVersionAsync(role, new RevokeRolePermissionCommand(new RoleId(role), ScheduleProbePermissions.Touch.Permission));
        Assert.False(revoked.IsError, revoked.IsError ? revoked.FirstError.Description : string.Empty);

        await FireAsync(id);
        await Engine.DrainAsync(host.Services);

        TaskRun second = (await RunsAsync(id)).Single(run => run.Id != first.Id);
        Assert.Equal(TaskRunStatus.Completed, second.Status);
        TaskRunItem item = Assert.Single(await ItemsAsync(second.Id));
        Assert.Equal(TaskRunItemStatus.Failed, item.Status);
        Assert.Equal(Error.Forbidden().Code, item.ErrorCode);
        Assert.Equal(1, item.Attempts);
    }

    [Fact]
    public async Task RunOnce_UnconfiguredCommand_IsRefused()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<TaskScheduleRunStartedResponse> started = await SendAsync(
            manager, tree.North, new RunTaskOnceCommand(Schema, new UnconfiguredProbeCommand(new(Guid.Empty), new ProbeNote("x")), [new(probe.Id.Value, null)]));

        Assert.Contains(started.Errors, error => error.Code == TaskErrors.NotSchedulable.Code);
        Assert.Empty(Engine.QueuedItems);
    }

    [Fact]
    public void Registry_DerivesScopeAndVersion_FromTheSharedSource()
    {
        CommandConfigurationRegistry registry = host.Services.GetRequiredService<CommandConfigurationRegistry>();

        foreach (Type guarded in new[] { typeof(UpdateTaskScheduleCommand), typeof(PauseTaskScheduleCommand), typeof(ResumeTaskScheduleCommand), typeof(DeleteTaskScheduleCommand) })
        {
            CommandSettings settings = registry.For(guarded);
            Assert.True(settings.RequiresExpectedVersion, guarded.Name);
            Assert.Equal(PermissionScope.Current, settings.PermissionScope);
            Assert.Equal(typeof(TaskSchedule), settings.AggregateRoot);
            Assert.Equal(new[] { TaskPermissions.Manage }, settings.RequiredPermissions);
        }

        foreach (Type unguarded in new[] { typeof(CreateTaskScheduleCommand), typeof(RunTaskScheduleNowCommand), typeof(RunTaskOnceCommand) })
        {
            CommandSettings settings = registry.For(unguarded);
            Assert.False(settings.RequiresExpectedVersion, unguarded.Name);
            Assert.Equal(PermissionScope.Current, settings.PermissionScope);
            Assert.Equal(new[] { TaskPermissions.Manage }, settings.RequiredPermissions);
        }
    }

    [Fact]
    public void ManagementCommands_FitTheCommandModel()
    {
        // The start guards scan module Contracts assemblies only, so these SharedKernel records are checked here.
        CommandConfigurationRegistry registry = host.Services.GetRequiredService<CommandConfigurationRegistry>();
        AggregateFeatureRegistry aggregates = host.Services.GetRequiredService<AggregateFeatureRegistry>();
        Type[] typed =
        [
            typeof(CreateTaskScheduleCommand), typeof(UpdateTaskScheduleCommand), typeof(PauseTaskScheduleCommand),
            typeof(ResumeTaskScheduleCommand), typeof(DeleteTaskScheduleCommand),
        ];

        CommandTypeGuard.Check([.. typed, typeof(RunTaskScheduleNowCommand), typeof(RunTaskOnceCommand)], registry, aggregates, requireTyped: false);
        CommandTargetMappingGuard.EnsureMapped(typed, host.Services);
    }

    private static TouchProbeCommand Touch(string note) => new(new(Guid.Empty), new ProbeNote(note));

    private static CreateTaskScheduleCommand Create(Sergin.SharedKernel.Application.Commands.IBaseCommand template, params ScheduleProbe[] targets) =>
        new(Schema, template, [.. targets.Select(probe => new TaskScheduleTarget(probe.Id.Value, null))], Cron, "UTC");

    private static UpdateTaskScheduleCommand Update(
        TaskScheduleId id, Sergin.SharedKernel.Application.Commands.IBaseCommand template, params ScheduleProbe[] targets) =>
        new(Schema, id, template, [.. targets.Select(probe => new TaskScheduleTarget(probe.Id.Value, null))], Cron, "UTC");

    private WebApplicationFactory<Program> CreateHost(Action<IServiceCollection> engine) =>
        TaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            engine(services);
            services.AddSingleton<ProbeFailureSwitch>();
            services.AddSingleton<ProbeSendRecorder>();
            services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<Success>>, TouchProbeCommandHandler>();
            services.AddScoped<FluentValidation.IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
        }));

    private Task<TestCaller> ManagerAsync(Guid scopeId) =>
        tree.CreateGrantedCallerAsync(scopeId, TaskPermissions.Manage.Permission, ScheduleProbePermissions.Touch.Permission);

    private async Task<TaskScheduleId> CreateTaskAsync(TestCaller caller, params ScheduleProbe[] targets)
    {
        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(caller, tree.North, Create(Touch("hello"), targets));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Code : string.Empty);

        return new TaskScheduleId(created.Value.Id);
    }

    private Task<ErrorOr<T>> SendAsync<T>(IUserContext caller, Guid currentScope, IRequest<ErrorOr<T>> request) =>
        SendAsync(host, caller, currentScope, request);

    private static async Task<ErrorOr<T>> SendAsync<T>(
        WebApplicationFactory<Program> on, IUserContext caller, Guid currentScope, IRequest<ErrorOr<T>> request)
    {
        using IServiceScope circuit = Circuit(on, caller, currentScope);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendAsync(request);
    }

    private async Task<ErrorOr<Versioned<T>>> SendVersionedAsync<T>(IUserContext caller, Guid currentScope, IRequest<ErrorOr<T>> request, RowVersion? expected)
    {
        using IServiceScope circuit = Circuit(host, caller, currentScope);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendVersionedAsync(request, expected);
    }

    /// <summary>Reads the schedule's version, then sends <paramref name="command"/> with it, as a detail page does.</summary>
    private async Task<ErrorOr<T>> SendAtCurrentVersionAsync<T>(IUserContext caller, TaskScheduleId id, IRequest<ErrorOr<T>> command)
    {
        using IServiceScope circuit = Circuit(host, caller, tree.North);
        ISerginDispatcher dispatcher = circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<Versioned<TaskScheduleResponse>> loaded = await dispatcher.SendVersionedAsync(new GetTaskScheduleByIdQuery(Schema, id));
        Assert.False(loaded.IsError, loaded.IsError ? loaded.FirstError.Code : string.Empty);

        return (await dispatcher.SendVersionedAsync(command, loaded.Value.Version)).Then(sent => sent.Value);
    }

    private static IServiceScope Circuit(WebApplicationFactory<Program> on, IUserContext caller, Guid currentScope)
    {
        IServiceScope circuit = on.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(currentScope);

        return circuit;
    }

    private async Task FireAsync(TaskScheduleId id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITaskScheduleRunner>().FireAsync(Schema, id, CancellationToken.None);
    }

    private async Task<(TaskSchedule Task, Guid ScopeId, Guid CreatedBy)> StoredAsync(TaskScheduleId id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        TaskSchedule task = await context.TaskSchedules.IgnoreQueryFilters().Include(one => one.Targets).SingleAsync(one => one.Id == id);

        return (
            task,
            (Guid)context.Entry(task).Property(ScopeColumns.ScopeId).CurrentValue!,
            (Guid)context.Entry(task).Property(AuditColumns.CreatedBy).CurrentValue!);
    }

    private async Task AssertNothingSavedAsync()
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        Assert.False(await context.TaskSchedules.IgnoreQueryFilters().AnyAsync());
        Assert.Empty(Engine.Triggers);
    }

    private async Task<IReadOnlyCollection<TaskRun>> RunsAsync(TaskScheduleId id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.TaskRuns.AsNoTracking().Where(run => run.ScheduleId == id).OrderBy(run => run.StartedAtUtc).ToListAsync();
    }

    private async Task<IReadOnlyCollection<TaskRunItem>> ItemsAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.TaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
    }

    private async Task<ScheduleProbe> CreateProbeAsync(Guid scopeId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        var probe = ScheduleProbe.Create();
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        return probe;
    }
}
