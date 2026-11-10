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
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Application.ScheduledTasks.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Hosts.Commands;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The management commands: creating, editing, pausing, resuming and deleting a schedule, running a saved one now, and
/// running a command once without saving. Each keeps Sergin's row and the engine's trigger in step, refuses a command
/// that is not schedulable or a trigger the engine cannot run, checks the command's own permissions besides Manage,
/// and acts for the person who chose the run: the last saver for a scheduled run, the clicker for a run now.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScheduledTaskCommandTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Schema = ScheduledTasksDbContext.Schema;
    private const string Cron = "0 * * * *";

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;
    private TestCaller manager = default!;

    private FakeScheduledTaskEngine Engine => host.Services.GetRequiredService<FakeScheduledTaskEngine>();

    public async Task InitializeAsync()
    {
        host = CreateHost(services =>
        {
            services.AddSingleton<FakeScheduledTaskEngine>();
            services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(provider => provider.GetRequiredService<FakeScheduledTaskEngine>()));
        });

        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
        manager = await ManagerAsync(tree.North);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task Create_StoresTask_AndSchedulesTrigger()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(manager, tree.North, Create(Touch("hello"), probe));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Code : string.Empty);
        var id = new ScheduledTaskId(created.Value.Id);
        (Guid triggerId, (ScheduledTrigger trigger, bool enabled)) = Assert.Single(Engine.Triggers);
        Assert.Equal(id, trigger.TaskId);
        Assert.Equal(Schema, trigger.Schema);
        Assert.Equal(Cron, trigger.Cron);
        Assert.True(enabled);

        (ScheduledTask task, Guid scopeId, Guid createdBy) = await StoredAsync(id);
        Assert.Equal(manager.Id, task.DelegatingUserId);
        Assert.Equal(triggerId, task.EngineTriggerId);
        Assert.Equal("tst.probes.touch", task.CommandName);
        Assert.Equal(new ScheduledTaskTarget(probe.Id.Value, null), Assert.Single(task.Targets));
        Assert.Equal(tree.North, scopeId);
        Assert.Equal(manager.Id.Value, createdBy);
    }

    [Fact]
    public async Task Create_UnconfiguredCommand_IsRefused()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(
            manager, tree.North, Create(new UnconfiguredProbeCommand(new(Guid.Empty), new ProbeNote("x")), probe));

        Assert.Contains(created.Errors, error => error.Code == ScheduledTaskErrors.NotSchedulable.Code && error.Type == ErrorType.Validation);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_SeveralTargets_NonSelectable_IsRefused()
    {
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder2);

        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(
            manager, tree.North, Create(new TouchOneProbeCommand(new(Guid.Empty), new ProbeNote("x")), first, second));

        Assert.Contains(created.Errors, error => error.Code == ScheduledTaskErrors.TooManyTargets.Code);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_DuplicateTargets_AreRefused()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        var root = Guid.CreateVersion7();
        TouchProbeChildCommand template = new(new(Guid.Empty), new(Guid.Empty), new ProbeNote("x"));

        // The targets' key is (task_id, target_id): the same id twice collides whatever its root.
        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(
            manager,
            tree.North,
            new CreateScheduledTaskCommand(Schema, template, [new(probe.Id.Value, null), new(probe.Id.Value, root)], Cron, "UTC"));

        Assert.Contains(created.Errors, error => error.Code == nameof(CreateScheduledTaskCommand.Targets));
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_ChildCommandWithoutRootId_IsRefused_AndNoTargets_IsRefused()
    {
        TouchProbeChildCommand template = new(new(Guid.Empty), new(Guid.Empty), new ProbeNote("x"));

        ErrorOr<CreateScheduledTaskCommandResponse> noRoot = await SendAsync(
            manager, tree.North, new CreateScheduledTaskCommand(Schema, template, [new(Guid.CreateVersion7(), null)], Cron, "UTC"));
        ErrorOr<CreateScheduledTaskCommandResponse> none = await SendAsync(
            manager, tree.North, new CreateScheduledTaskCommand(Schema, Touch("x"), [], Cron, "UTC"));

        Assert.Contains(noRoot.Errors, error => error.Code == nameof(CreateScheduledTaskCommand.Targets));
        Assert.Contains(none.Errors, error => error.Code == nameof(CreateScheduledTaskCommand.Targets));
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_BadCron_IsRefused()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(
            manager, tree.North, Create(Touch("x"), probe) with { Cron = "every hour" });

        Error error = Assert.Single(created.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(CreateScheduledTaskCommand.Cron), error.Code);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_BadTimeZone_IsRefused()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(
            manager, tree.North, Create(Touch("x"), probe) with { TimeZoneId = "Mars/Olympus_Mons" });

        Error error = Assert.Single(created.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(CreateScheduledTaskCommand.TimeZoneId), error.Code);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_CallerLacksCommandPermission_IsForbidden()
    {
        TestCaller manageOnly = await tree.CreateGrantedCallerAsync(tree.North, ScheduledTaskPermissions.Manage.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(manageOnly, tree.North, Create(Touch("x"), probe));

        Assert.Equal(ErrorType.Forbidden, created.FirstError.Type);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_CallerLacksManage_IsForbidden()
    {
        TestCaller touchOnly = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(touchOnly, tree.North, Create(Touch("x"), probe));

        Assert.Equal(ErrorType.Forbidden, created.FirstError.Type);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Create_EngineFails_SavesNothing()
    {
        await using WebApplicationFactory<Program> failing = CreateHost(services =>
        {
            services.AddSingleton<FailingScheduledTaskEngine>();
            services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(provider => provider.GetRequiredService<FailingScheduledTaskEngine>()));
        });
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(failing, manager, tree.North, Create(Touch("x"), probe));

        Assert.Equal(ErrorType.Unexpected, created.FirstError.Type);
        Assert.Equal(ScheduledTaskErrors.EngineUnavailable.Code, created.FirstError.Code);
        Assert.Equal(1, failing.Services.GetRequiredService<FailingScheduledTaskEngine>().Calls);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task Update_BySecondUser_MakesThemDelegating()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTaskId id = await CreateTaskAsync(manager, probe);
        TestCaller second = await ManagerAsync(tree.North);

        ErrorOr<UpdateScheduledTaskCommandResponse> updated =
            await SendAtCurrentVersionAsync(second, id, Update(id, Touch("edited"), probe) with { Cron = "30 * * * *" });

        Assert.False(updated.IsError, updated.IsError ? updated.FirstError.Code : string.Empty);
        (ScheduledTask task, _, _) = await StoredAsync(id);
        Assert.Equal(second.Id, task.DelegatingUserId);
        Assert.Equal("30 * * * *", Assert.Single(Engine.Triggers).Value.Trigger.Cron);

        await FireAsync(id);

        ScheduledTaskRun run = Assert.Single(await RunsAsync(id));
        Assert.Equal(second.Id, run.ActingUserId);
        Assert.Contains("edited", run.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_WithoutVersion_Is428()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTaskId id = await CreateTaskAsync(manager, probe);

        ErrorOr<UpdateScheduledTaskCommandResponse> updated = await SendAsync(manager, tree.North, Update(id, Touch("edited"), probe));

        Assert.Equal(VersionErrors.Required.Code, updated.FirstError.Code);
        Assert.DoesNotContain("edited", (await StoredAsync(id)).Task.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_StaleVersion_Is412_AndTriggerIsRestored()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTaskId id = await CreateTaskAsync(manager, probe);

        ErrorOr<Versioned<UpdateScheduledTaskCommandResponse>> updated = await SendVersionedAsync(
            manager, tree.North, Update(id, Touch("edited"), probe) with { Cron = "30 * * * *" }, RowVersion.Create());

        Assert.True(VersionErrors.IsStale(updated.FirstError));
        Assert.DoesNotContain("edited", (await StoredAsync(id)).Task.Arguments, StringComparison.Ordinal);
        Assert.Equal(Cron, Assert.Single(Engine.Triggers).Value.Trigger.Cron);
    }

    [Fact]
    public async Task Pause_ThenFire_DoesNothing()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTaskId id = await CreateTaskAsync(manager, probe);

        ErrorOr<Success> paused = await SendAtCurrentVersionAsync(manager, id, new PauseScheduledTaskCommand(Schema, id));

        Assert.False(paused.IsError, paused.IsError ? paused.FirstError.Code : string.Empty);
        Assert.Equal(ScheduledTaskState.Paused, (await StoredAsync(id)).Task.State);
        Assert.False(Assert.Single(Engine.Triggers).Value.Enabled);

        await FireAsync(id);

        Assert.Empty(await RunsAsync(id));
    }

    [Fact]
    public async Task Resume_ThenFire_Runs()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTaskId id = await CreateTaskAsync(manager, probe);
        Assert.False((await SendAtCurrentVersionAsync(manager, id, new PauseScheduledTaskCommand(Schema, id))).IsError);

        ErrorOr<Success> resumed = await SendAtCurrentVersionAsync(manager, id, new ResumeScheduledTaskCommand(Schema, id));

        Assert.False(resumed.IsError, resumed.IsError ? resumed.FirstError.Code : string.Empty);
        Assert.True(Assert.Single(Engine.Triggers).Value.Enabled);

        await FireAsync(id);

        Assert.Equal(ScheduledRunTrigger.Scheduled, Assert.Single(await RunsAsync(id)).Trigger);
    }

    [Fact]
    public async Task Delete_SoftDeletes_RemovesTrigger_KeepsRuns()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTaskId id = await CreateTaskAsync(manager, probe);
        await FireAsync(id);

        ErrorOr<Success> deleted = await SendAtCurrentVersionAsync(manager, id, new DeleteScheduledTaskCommand(Schema, id));

        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Code : string.Empty);
        Assert.Empty(Engine.Triggers);
        Assert.Single(await RunsAsync(id));

        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();
        Assert.False(await context.ScheduledTasks.IgnoreQueryFilters([ScopeColumns.QueryFilterName]).AnyAsync(task => task.Id == id));
        Assert.True(await context.ScheduledTasks.IgnoreQueryFilters().AnyAsync(task => task.Id == id));
    }

    [Fact]
    public async Task Delete_WithoutVersion_Is428()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTaskId id = await CreateTaskAsync(manager, probe);

        ErrorOr<Success> deleted = await SendAsync(manager, tree.North, new DeleteScheduledTaskCommand(Schema, id));

        Assert.Equal(VersionErrors.Required.Code, deleted.FirstError.Code);
        Assert.Single(Engine.Triggers);
    }

    [Fact]
    public async Task RunNow_ActsForClicker_NotDelegatingUser()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTaskId id = await CreateTaskAsync(manager, probe);
        TestCaller clicker = await ManagerAsync(tree.North);

        ErrorOr<ScheduledRunStartedResponse> started = await SendAsync(clicker, tree.North, new RunScheduledTaskNowCommand(Schema, id));

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        ScheduledTaskRun run = Assert.Single(await RunsAsync(id));
        Assert.Equal(started.Value.RunId, run.Id);
        Assert.Equal(ScheduledRunTrigger.RunNow, run.Trigger);
        Assert.Equal(clicker.Id, run.ActingUserId);
        Assert.Equal(tree.North, run.ScopeId);
        Assert.Equal(manager.Id, (await StoredAsync(id)).Task.DelegatingUserId);
    }

    [Fact]
    public async Task RunNow_WhilePreviousUnfinished_RecordsSkipped()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTaskId id = await CreateTaskAsync(manager, probe);
        await FireAsync(id);

        ErrorOr<ScheduledRunStartedResponse> started = await SendAsync(manager, tree.North, new RunScheduledTaskNowCommand(Schema, id));

        Assert.Equal(ScheduledTaskErrors.Overlap.Code, started.FirstError.Code);
        IReadOnlyCollection<ScheduledTaskRun> runs = await RunsAsync(id);
        ScheduledTaskRun skipped = Assert.Single(runs, run => run.Status == ScheduledRunStatus.Skipped);
        Assert.Equal(ScheduledRunTrigger.RunNow, skipped.Trigger);
        Assert.Equal(manager.Id, skipped.ActingUserId);
    }

    [Fact]
    public async Task RunNow_CallerLacksCommandPermission_IsForbidden()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTaskId id = await CreateTaskAsync(manager, probe);
        TestCaller manageOnly = await tree.CreateGrantedCallerAsync(tree.North, ScheduledTaskPermissions.Manage.Permission);

        ErrorOr<ScheduledRunStartedResponse> started = await SendAsync(manageOnly, tree.North, new RunScheduledTaskNowCommand(Schema, id));

        Assert.Equal(ErrorType.Forbidden, started.FirstError.Type);
        Assert.Empty(await RunsAsync(id));
    }

    [Fact]
    public async Task RunNow_OutOfReach_IsNotFound()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTaskId id = await CreateTaskAsync(manager, probe);
        TestCaller southManager = await ManagerAsync(tree.South);

        ErrorOr<ScheduledRunStartedResponse> started = await SendAsync(southManager, tree.South, new RunScheduledTaskNowCommand(Schema, id));

        Assert.Equal(ErrorType.NotFound, started.FirstError.Type);
        Assert.Empty(await RunsAsync(id));
    }

    [Fact]
    public async Task RunOnce_LeavesNoTask_AndRunHasNoTaskId()
    {
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder2);

        ErrorOr<ScheduledRunStartedResponse> started = await SendAsync(
            manager,
            tree.North,
            new RunCommandOnceCommand(Schema, Touch("once"), [new(first.Id.Value, null), new(second.Id.Value, null)]));

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        await Engine.DrainAsync(host.Services);

        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();
        Assert.False(await context.ScheduledTasks.IgnoreQueryFilters().AnyAsync());
        ScheduledTaskRun run = await context.ScheduledTaskRuns.AsNoTracking().SingleAsync(one => one.Id == started.Value.RunId);
        Assert.Null(run.TaskId);
        Assert.Equal(ScheduledRunTrigger.OneOff, run.Trigger);
        Assert.Equal(manager.Id, run.ActingUserId);
        Assert.Equal(tree.North, run.ScopeId);
        Assert.Equal(ScheduledRunStatus.Completed, run.Status);
        Assert.Equal(2, run.Succeeded);
        Assert.All(
            await context.ScheduledTaskRunItems.AsNoTracking().Where(item => item.RunId == run.Id).ToListAsync(),
            item => Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status));
        Assert.Equal("once", (await context.Probes.AsNoTracking().IgnoreQueryFilters().SingleAsync(probe => probe.Id == first.Id)).LastNote);
    }

    [Fact]
    public async Task RunOnce_UnconfiguredCommand_IsRefused()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<ScheduledRunStartedResponse> started = await SendAsync(
            manager, tree.North, new RunCommandOnceCommand(Schema, new UnconfiguredProbeCommand(new(Guid.Empty), new ProbeNote("x")), [new(probe.Id.Value, null)]));

        Assert.Contains(started.Errors, error => error.Code == ScheduledTaskErrors.NotSchedulable.Code);
        Assert.Empty(Engine.QueuedItems);
    }

    [Fact]
    public void Registry_DerivesScopeAndVersion_FromTheSharedSource()
    {
        CommandConfigurationRegistry registry = host.Services.GetRequiredService<CommandConfigurationRegistry>();

        foreach (Type guarded in new[] { typeof(UpdateScheduledTaskCommand), typeof(PauseScheduledTaskCommand), typeof(ResumeScheduledTaskCommand), typeof(DeleteScheduledTaskCommand) })
        {
            CommandSettings settings = registry.For(guarded);
            Assert.True(settings.RequiresExpectedVersion, guarded.Name);
            Assert.Equal(PermissionScope.Current, settings.PermissionScope);
            Assert.Equal(typeof(ScheduledTask), settings.AggregateRoot);
            Assert.Equal(new[] { ScheduledTaskPermissions.Manage }, settings.RequiredPermissions);
        }

        foreach (Type unguarded in new[] { typeof(CreateScheduledTaskCommand), typeof(RunScheduledTaskNowCommand), typeof(RunCommandOnceCommand) })
        {
            CommandSettings settings = registry.For(unguarded);
            Assert.False(settings.RequiresExpectedVersion, unguarded.Name);
            Assert.Equal(PermissionScope.Current, settings.PermissionScope);
            Assert.Equal(new[] { ScheduledTaskPermissions.Manage }, settings.RequiredPermissions);
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
            typeof(CreateScheduledTaskCommand), typeof(UpdateScheduledTaskCommand), typeof(PauseScheduledTaskCommand),
            typeof(ResumeScheduledTaskCommand), typeof(DeleteScheduledTaskCommand),
        ];

        CommandTypeGuard.Check([.. typed, typeof(RunScheduledTaskNowCommand), typeof(RunCommandOnceCommand)], registry, aggregates, requireTyped: false);
        CommandTargetMappingGuard.EnsureMapped(typed, host.Services);
    }

    private static TouchProbeCommand Touch(string note) => new(new(Guid.Empty), new ProbeNote(note));

    private static CreateScheduledTaskCommand Create(Sergin.SharedKernel.Application.Commands.IBaseCommand template, params ScheduleProbe[] targets) =>
        new(Schema, template, [.. targets.Select(probe => new ScheduledTaskTarget(probe.Id.Value, null))], Cron, "UTC");

    private static UpdateScheduledTaskCommand Update(
        ScheduledTaskId id, Sergin.SharedKernel.Application.Commands.IBaseCommand template, params ScheduleProbe[] targets) =>
        new(Schema, id, template, [.. targets.Select(probe => new ScheduledTaskTarget(probe.Id.Value, null))], Cron, "UTC");

    private WebApplicationFactory<Program> CreateHost(Action<IServiceCollection> engine) =>
        ScheduledTaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            engine(services);
            services.AddSingleton<ProbeFailureSwitch>();
            services.AddSingleton<ProbeSendRecorder>();
            services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, TouchProbeCommandHandler>();
            services.AddScoped<FluentValidation.IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
        }));

    private Task<TestCaller> ManagerAsync(Guid scopeId) =>
        tree.CreateGrantedCallerAsync(scopeId, ScheduledTaskPermissions.Manage.Permission, ScheduleProbePermissions.Touch.Permission);

    private async Task<ScheduledTaskId> CreateTaskAsync(TestCaller caller, params ScheduleProbe[] targets)
    {
        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(caller, tree.North, Create(Touch("hello"), targets));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Code : string.Empty);

        return new ScheduledTaskId(created.Value.Id);
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
    private async Task<ErrorOr<T>> SendAtCurrentVersionAsync<T>(IUserContext caller, ScheduledTaskId id, IRequest<ErrorOr<T>> command)
    {
        using IServiceScope circuit = Circuit(host, caller, tree.North);
        ISerginDispatcher dispatcher = circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<Versioned<ScheduledTaskResponse>> loaded = await dispatcher.SendVersionedAsync(new GetScheduledTaskByIdQuery(Schema, id));
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

    private async Task FireAsync(ScheduledTaskId id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IScheduledTaskRunner>().FireAsync(Schema, id, CancellationToken.None);
    }

    private async Task<(ScheduledTask Task, Guid ScopeId, Guid CreatedBy)> StoredAsync(ScheduledTaskId id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        ScheduledTask task = await context.ScheduledTasks.IgnoreQueryFilters().Include(one => one.Targets).SingleAsync(one => one.Id == id);

        return (
            task,
            (Guid)context.Entry(task).Property(ScopeColumns.ScopeId).CurrentValue!,
            (Guid)context.Entry(task).Property(AuditColumns.CreatedBy).CurrentValue!);
    }

    private async Task AssertNothingSavedAsync()
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        Assert.False(await context.ScheduledTasks.IgnoreQueryFilters().AnyAsync());
        Assert.Empty(Engine.Triggers);
    }

    private async Task<IReadOnlyCollection<ScheduledTaskRun>> RunsAsync(ScheduledTaskId id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.ScheduledTaskRuns.AsNoTracking().Where(run => run.TaskId == id).OrderBy(run => run.StartedAtUtc).ToListAsync();
    }

    private async Task<ScheduleProbe> CreateProbeAsync(Guid scopeId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        var probe = ScheduleProbe.Create();
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        return probe;
    }
}
