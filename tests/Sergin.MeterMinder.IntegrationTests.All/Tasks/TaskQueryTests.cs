using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Events.Integration;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.Administration.UserAccess.Application.Contracts.Scopes.Commands.Move;
using Sergin.Administration.UserAccess.Domain.Scopes;
using ErrorOrType = ErrorOr.ErrorType;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// The schedule reads: a record's schedules with their last run, one schedule, its runs newest first and paged, a
/// run's items with their errors, and the one-off runs over a record. Every read is limited to live tasks inside the
/// caller's reach for the manage permission, and refused without it.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TaskQueryTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string TouchName = "tst.probes.touch";
    private const string Schema = TasksDbContext.Schema;

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;
    private TestCaller manager = default!;

    private FakeTaskEngine Engine => host.Services.GetRequiredService<FakeTaskEngine>();

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<FakeTaskEngine>();
            services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(provider => provider.GetRequiredService<FakeTaskEngine>()));
            services.AddSingleton<ProbeFailureSwitch>();
            services.AddSingleton<ProbeSendRecorder>();
            services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<Success>>, TouchProbeCommandHandler>();
            services.AddScoped<FluentValidation.IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
            services.AddTransient<IPipelineBehavior<TouchProbeCommand, ErrorOr<Success>>, RecordingProbeBehavior>();
        }));

        await TaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
        manager = await tree.CreateGrantedCallerAsync(tree.North, TaskPermissions.Manage.Permission);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task TasksForTarget_ListsOnlyTasksTargetingIt_WithTheLastRun()
    {
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder2);
        TaskSchedule ofFirst = await CreateTaskAsync(tree.North, TouchName, first);
        await CreateTaskAsync(tree.North, TouchName, second);
        TaskSchedule ofBoth = await CreateTaskAsync(tree.North, TouchName, first, second);

        // Before any run, the last-run columns are empty.
        TaskScheduleListItem unrun = Assert.Single(
            (await SendAsync(manager, tree.North, new GetTaskSchedulesForTargetQuery(Schema, second.Id.Value))).Value,
            item => item.Id == ofBoth.Id.Value);
        Assert.Null(unrun.LastRunAtUtc);
        Assert.Null(unrun.LastRunStatus);
        Assert.Null(unrun.LastRunFailed);

        await FireAsync(ofFirst);
        await Engine.DrainAsync(host.Services);

        IReadOnlyCollection<TaskScheduleListItem> items =
            (await SendAsync(manager, tree.North, new GetTaskSchedulesForTargetQuery(Schema, first.Id.Value))).Value;

        Assert.Equal(2, items.Count);
        TaskScheduleListItem ran = Assert.Single(items, item => item.Id == ofFirst.Id.Value);
        Assert.Equal(TouchName, ran.CommandName);
        Assert.Equal("0 * * * *", ran.Cron);
        Assert.Equal("UTC", ran.TimeZoneId);
        Assert.Equal(TaskScheduleState.Active, ran.State);
        Assert.Equal(TaskRunStatus.Completed, ran.LastRunStatus);
        Assert.Equal(0, ran.LastRunFailed);
        Assert.NotNull(ran.LastRunAtUtc);
        Assert.Equal(1, ran.TargetCount);
        Assert.Equal(2, Assert.Single(items, item => item.Id == ofBoth.Id.Value).TargetCount);
    }

    [Fact]
    public async Task TasksForTarget_HidesDeleted()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskSchedule task = await CreateTaskAsync(tree.North, TouchName, probe);
        await DeleteTaskAsync(task);

        Assert.Empty((await SendAsync(manager, tree.North, new GetTaskSchedulesForTargetQuery(Schema, probe.Id.Value))).Value);
        Assert.Equal(
            ErrorOrType.NotFound,
            (await SendVersionedAsync(manager, tree.North, new GetTaskScheduleByIdQuery(Schema, task.Id))).FirstError.Type);
    }

    [Fact]
    public async Task TasksForTarget_HidesOutOfReach()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.South);
        TaskSchedule southern = await CreateTaskAsync(tree.South, TouchName, probe);

        // A North holder sees nothing of South; asking with South as the current scope is refused outright.
        Assert.Empty((await SendAsync(manager, tree.North, new GetTaskSchedulesForTargetQuery(Schema, probe.Id.Value))).Value);
        Assert.Equal(
            ErrorOrType.Forbidden,
            (await SendAsync(manager, tree.South, new GetTaskSchedulesForTargetQuery(Schema, probe.Id.Value))).FirstError.Type);
        Assert.Equal(
            ErrorOrType.NotFound,
            (await SendVersionedAsync(manager, tree.North, new GetTaskScheduleByIdQuery(Schema, southern.Id))).FirstError.Type);

        // Its runs are as hidden as the task.
        await FireAsync(southern);
        Assert.Empty((await SendAsync(manager, tree.North, new GetTaskScheduleRunsQuery(Schema, southern.Id, Paggination.Default))).Value.Data);

        TestCaller southManager = await tree.CreateGrantedCallerAsync(tree.South, TaskPermissions.Manage.Permission);
        Assert.Single((await SendAsync(southManager, tree.South, new GetTaskSchedulesForTargetQuery(Schema, probe.Id.Value))).Value);
    }

    [Fact]
    public async Task GetById_ReturnsTheTaskWithItsTargetsAndVersion()
    {
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder2);
        TaskSchedule task = await CreateTaskAsync(tree.North, TouchName, first, second);

        ErrorOr<Versioned<TaskScheduleResponse>> result =
            await SendVersionedAsync(manager, tree.North, new GetTaskScheduleByIdQuery(Schema, task.Id));

        Assert.False(result.IsError);
        TaskScheduleResponse response = result.Value.Value;
        Assert.Equal(task.Id.Value, response.Id);
        Assert.Equal(TouchName, response.CommandName);
        Assert.Equal(task.DelegatingUserId.Value, response.DelegatingUserId);
        Assert.Equal(TaskScheduleState.Active, response.State);
        Assert.Equal(
            new[] { first.Id.Value, second.Id.Value }.Order(),
            response.Targets.Select(target => target.TargetId).Order());
        Assert.NotEqual(Guid.Empty, result.Value.Version.Value);
    }

    [Fact]
    public async Task Runs_NewestFirst_Paged()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskSchedule task = await CreateTaskAsync(tree.North, TouchName, probe);

        for (int fired = 0; fired < 3; fired++)
        {
            await FireAsync(task);

            if (fired < 2)
            {
                await Engine.DrainAsync(host.Services);
            }
        }

        ListQueryResponse<TaskRunListItem> firstPage =
            (await SendAsync(manager, tree.North, new GetTaskScheduleRunsQuery(Schema, task.Id, Paggination.Create(2, 1)))).Value;
        ListQueryResponse<TaskRunListItem> secondPage =
            (await SendAsync(manager, tree.North, new GetTaskScheduleRunsQuery(Schema, task.Id, Paggination.Create(2, 2)))).Value;

        Assert.Equal(3, firstPage.Total);
        Assert.Equal(3, secondPage.Total);
        Assert.Equal(2, firstPage.Data.Count);
        TaskRunListItem oldest = Assert.Single(secondPage.Data);

        TaskRunListItem[] all = [.. firstPage.Data, oldest];
        Assert.Equal(all.OrderByDescending(run => run.StartedAtUtc), all);
        Assert.Equal(TaskRunStatus.Running, all[0].Status);
        Assert.Equal(TaskRunStatus.Completed, oldest.Status);
        Assert.All(all, run => Assert.Equal(TaskRunTrigger.Schedule, run.Trigger));
        Assert.Equal(task.DelegatingUserId.Value, all[0].ActingUserId);
        Assert.Equal(1, all[0].Total);
        Assert.Equal(1, oldest.Succeeded);
        Assert.NotNull(oldest.CompletedAtUtc);
    }

    [Fact]
    public async Task RunItems_CarryErrors()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskSchedule task = await CreateTaskAsync(tree.North, "tst.probes.gone", probe);
        await FireAsync(task);
        await Engine.DrainAsync(host.Services);

        Guid runId = (await SendAsync(manager, tree.North, new GetTaskScheduleRunsQuery(Schema, task.Id, Paggination.Default))).Value.Data.Single().Id;

        TaskRunItemListItem item =
            Assert.Single((await SendAsync(manager, tree.North, new GetTaskRunItemsQuery(Schema, runId))).Value);

        Assert.Equal(probe.Id.Value, item.TargetId);
        Assert.Equal(TaskRunItemStatus.Failed, item.Status);
        Assert.Equal(TaskErrors.UnknownCommand.Code, item.ErrorCode);
        Assert.Equal(TaskErrors.UnknownCommand.Description, item.ErrorDescription);
        Assert.Equal(1, item.Attempts);
        Assert.NotNull(item.CompletedAtUtc);

        // Out of reach, the same run is empty.
        TestCaller other = await tree.CreateGrantedCallerAsync(tree.South, TaskPermissions.Manage.Permission);
        Assert.Empty((await SendAsync(other, tree.South, new GetTaskRunItemsQuery(Schema, runId))).Value);
    }

    [Fact]
    public async Task RunItems_CarryTheStoredTargetLabel()
    {
        Guid labelled = await AddOneOffRunAsync(tree.North, Guid.CreateVersion7(), DateTime.UtcNow.AddMinutes(-20), "Probe A");
        Guid unlabelled = await AddOneOffRunAsync(tree.North, Guid.CreateVersion7(), DateTime.UtcNow.AddMinutes(-10));

        Assert.Equal("Probe A", Assert.Single((await SendAsync(manager, tree.North, new GetTaskRunItemsQuery(Schema, labelled))).Value).TargetLabel);
        Assert.Null(Assert.Single((await SendAsync(manager, tree.North, new GetTaskRunItemsQuery(Schema, unlabelled))).Value).TargetLabel);
    }

    [Fact]
    public async Task OneOffRuns_ForTarget_NewestFirst()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe other = await CreateProbeAsync(tree.Feeder2);
        DateTime now = DateTime.UtcNow;
        Guid oldest = await AddOneOffRunAsync(tree.North, probe.Id.Value, now.AddMinutes(-30));
        Guid newest = await AddOneOffRunAsync(tree.North, probe.Id.Value, now.AddMinutes(-10));
        await AddOneOffRunAsync(tree.North, other.Id.Value, now.AddMinutes(-5));
        await AddOneOffRunAsync(tree.South, probe.Id.Value, now.AddMinutes(-1));

        IReadOnlyCollection<TaskRunListItem> runs =
            (await SendAsync(manager, tree.North, new GetRecentRunsForTargetQuery(Schema, probe.Id.Value, 10))).Value;

        Assert.Equal([newest, oldest], runs.Select(run => run.Id));
        Assert.All(runs, run => Assert.Equal(TaskRunTrigger.Once, run.Trigger));
        Assert.All(runs, run => Assert.Equal(TouchName, run.CommandName));
        Assert.Equal(
            [newest],
            (await SendAsync(manager, tree.North, new GetRecentRunsForTargetQuery(Schema, probe.Id.Value, 1))).Value.Select(run => run.Id));
    }

    [Fact]
    public async Task RecentRuns_ForTarget_ListEveryTrigger_AndHideADeletedSchedulesRuns()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskSchedule task = await CreateTaskAsync(tree.North, TouchName, probe);
        await FireAsync(task);
        Guid oneOff = await AddOneOffRunAsync(tree.North, probe.Id.Value, DateTime.UtcNow.AddMinutes(-30));

        IReadOnlyCollection<TaskRunListItem> runs =
            (await SendAsync(manager, tree.North, new GetRecentRunsForTargetQuery(Schema, probe.Id.Value, 10))).Value;

        Assert.Equal([TaskRunTrigger.Schedule, TaskRunTrigger.Once], runs.Select(run => run.Trigger));

        // A schedule's run is reached through its live schedule, as in the runs dialog: deleting it hides its history.
        await DeleteTaskAsync(task);

        Assert.Equal([oneOff], await RecentRunIdsAsync(manager, tree.North, probe));
    }

    [Fact]
    public async Task OneOffRun_FollowsItsScope_WhenTheScopeMoves()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder2);
        Guid run = await AddOneOffRunAsync(tree.Feeder2, probe.Id.Value, DateTime.UtcNow);
        TestCaller southManager = await tree.CreateGrantedCallerAsync(tree.South, TaskPermissions.Manage.Permission);

        Assert.Equal([run], await RecentRunIdsAsync(manager, tree.North, probe));
        Assert.Empty(await RecentRunIdsAsync(southManager, tree.South, probe));

        ErrorOr<MoveScopeCommandResponse> moved = await tree.TrySendAsync(new MoveScopeCommand(new ScopeId(tree.Feeder2), new ScopeId(tree.South)));
        Assert.False(moved.IsError, moved.IsError ? moved.FirstError.Description : string.Empty);
        await PublishMovedAsync([tree.Feeder2]);

        // The run left the old path with its scope: the caller who reaches only North no longer sees it, South's does.
        Assert.Empty(await RecentRunIdsAsync(manager, tree.North, probe));
        Assert.Equal([run], await RecentRunIdsAsync(southManager, tree.South, probe));
        Assert.Empty((await SendAsync(manager, tree.North, new GetTaskRunItemsQuery(Schema, run))).Value);
        Assert.Single((await SendAsync(southManager, tree.South, new GetTaskRunItemsQuery(Schema, run))).Value);
    }

    [Fact]
    public async Task WithoutManage_IsForbidden()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskSchedule task = await CreateTaskAsync(tree.North, TouchName, probe);
        TestCaller toucher = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);

        Assert.Equal(ErrorOrType.Forbidden, (await SendAsync(toucher, tree.North, new GetTaskSchedulesForTargetQuery(Schema, probe.Id.Value))).FirstError.Type);
        Assert.Equal(ErrorOrType.Forbidden, (await SendVersionedAsync(toucher, tree.North, new GetTaskScheduleByIdQuery(Schema, task.Id))).FirstError.Type);
        Assert.Equal(ErrorOrType.Forbidden, (await SendAsync(toucher, tree.North, new GetTaskScheduleRunsQuery(Schema, task.Id, Paggination.Default))).FirstError.Type);
        Assert.Equal(ErrorOrType.Forbidden, (await SendAsync(toucher, tree.North, new GetTaskRunItemsQuery(Schema, Guid.NewGuid()))).FirstError.Type);
        Assert.Equal(ErrorOrType.Forbidden, (await SendAsync(toucher, tree.North, new GetRecentRunsForTargetQuery(Schema, probe.Id.Value, 5))).FirstError.Type);
    }

    [Fact]
    public async Task UnknownSchema_IsRefused_NotComposedIntoSql()
    {
        ErrorOr<IReadOnlyCollection<TaskScheduleListItem>> result =
            await SendAsync(manager, tree.North, new GetTaskSchedulesForTargetQuery("tst\"; DROP TABLE x; --", Guid.NewGuid()));

        Assert.True(result.IsError);
        Assert.Equal(ErrorOrType.Validation, result.FirstError.Type);
    }

    private async Task<ErrorOr<T>> SendAsync<T>(IUserContext caller, Guid currentScope, IRequest<ErrorOr<T>> request)
    {
        using IServiceScope circuit = Circuit(caller, currentScope);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendAsync(request);
    }

    private async Task<ErrorOr<Versioned<T>>> SendVersionedAsync<T>(IUserContext caller, Guid currentScope, IRequest<ErrorOr<T>> request)
    {
        using IServiceScope circuit = Circuit(caller, currentScope);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendVersionedAsync(request);
    }

    private async Task<IReadOnlyCollection<Guid>> RecentRunIdsAsync(IUserContext caller, Guid currentScope, ScheduleProbe probe)
    {
        ErrorOr<IReadOnlyCollection<TaskRunListItem>> runs = await SendAsync(caller, currentScope, new GetRecentRunsForTargetQuery(Schema, probe.Id.Value, 10));
        Assert.False(runs.IsError, runs.IsError ? runs.FirstError.Code : string.Empty);

        return [.. runs.Value.Select(one => one.Id)];
    }

    /// <summary>Delivers the move's integration event as the outbox relay would, so the path rewrite runs now.</summary>
    private async Task PublishMovedAsync(IReadOnlyCollection<Guid> scopeIds)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = new TestSystemCaller();

        ScopesMovedIntegrationEvent moved = new(Guid.CreateVersion7(), DateTime.UtcNow, scopeIds);

        await scope.ServiceProvider.GetRequiredService<IPublisher>()
            .Publish(new IntegrationEventNotification<ScopesMovedIntegrationEvent>(Guid.CreateVersion7(), "test", moved));
    }

    private IServiceScope Circuit(IUserContext caller, Guid currentScope)
    {
        IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(currentScope);

        return circuit;
    }

    private static ITaskStore Store(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Schema);

    private async Task FireAsync(TaskSchedule task)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITaskScheduleRunner>().FireAsync(Schema, task.Id, CancellationToken.None);
    }

    private async Task<TaskSchedule> CreateTaskAsync(Guid scopeId, string commandName, params ScheduleProbe[] targets)
    {
        TestCaller user = await tree.CreateGrantedCallerAsync(scopeId, ScheduleProbePermissions.Touch.Permission);
        string arguments = TaskCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("hello")));

        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);

        var task = TaskSchedule.Create(
            commandName, arguments, [.. targets.Select(probe => new TaskScheduleTarget(probe.Id.Value))], "0 * * * *", "UTC", user.Id);
        ITaskStore store = Store(scope);
        store.AddTask(task);
        await store.SaveAsync(CancellationToken.None);

        return task;
    }

    private async Task DeleteTaskAsync(TaskSchedule task)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ITaskStore store = Store(scope);
        store.RemoveTask((await store.GetTaskAsync(task.Id, ignoreScope: true, CancellationToken.None))!);
        await store.SaveAsync(CancellationToken.None);
    }

    private async Task<Guid> AddOneOffRunAsync(Guid scopeId, Guid targetId, DateTime startedAtUtc, string? targetLabel = null)
    {
        string arguments = TaskCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("once")));

        using IServiceScope scope = host.Services.CreateScope();
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(scopeId))!;
        var run = TaskRun.Start(
            new TaskRunDefinition(null, TaskRunTrigger.Once, manager.Id, scopeId, path, TouchName, arguments), 1, startedAtUtc);
        ITaskStore store = Store(scope);
        store.AddRun(run, [TaskRunItem.Create(run.Id, targetId, DateTime.UtcNow, targetLabel)]);
        await store.SaveAsync(CancellationToken.None);

        return run.Id;
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
