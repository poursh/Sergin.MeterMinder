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
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using ErrorOrType = ErrorOr.ErrorType;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The schedule reads: a record's schedules with their last run, one schedule, its runs newest first and paged, a
/// run's items with their errors, and the one-off runs over a record. Every read is limited to live tasks inside the
/// caller's reach for the manage permission, and refused without it.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScheduledTaskQueryTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string TouchName = "tst.probes.touch";
    private const string Schema = ScheduledTasksDbContext.Schema;

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;
    private TestCaller manager = default!;

    private FakeScheduledTaskEngine Engine => host.Services.GetRequiredService<FakeScheduledTaskEngine>();

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<FakeScheduledTaskEngine>();
            services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(provider => provider.GetRequiredService<FakeScheduledTaskEngine>()));
            services.AddSingleton<ProbeFailureSwitch>();
            services.AddSingleton<ProbeSendRecorder>();
            services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, TouchProbeCommandHandler>();
            services.AddScoped<FluentValidation.IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
            services.AddTransient<IPipelineBehavior<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, RecordingProbeBehavior>();
        }));

        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
        manager = await tree.CreateGrantedCallerAsync(tree.North, ScheduledTaskPermissions.Manage.Permission);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task TasksForTarget_ListsOnlyTasksTargetingIt_WithTheLastRun()
    {
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder2);
        ScheduledTask ofFirst = await CreateTaskAsync(tree.North, TouchName, first);
        await CreateTaskAsync(tree.North, TouchName, second);
        ScheduledTask ofBoth = await CreateTaskAsync(tree.North, TouchName, first, second);

        // Before any run, the last-run columns are empty.
        ScheduledTaskListItem unrun = Assert.Single(
            (await SendAsync(manager, tree.North, new GetScheduledTasksForTargetQuery(Schema, second.Id.Value))).Value,
            item => item.Id == ofBoth.Id.Value);
        Assert.Null(unrun.LastRunAtUtc);
        Assert.Null(unrun.LastRunStatus);
        Assert.Null(unrun.LastRunFailed);

        await FireAsync(ofFirst);
        await Engine.DrainAsync(host.Services);

        IReadOnlyCollection<ScheduledTaskListItem> items =
            (await SendAsync(manager, tree.North, new GetScheduledTasksForTargetQuery(Schema, first.Id.Value))).Value;

        Assert.Equal(2, items.Count);
        ScheduledTaskListItem ran = Assert.Single(items, item => item.Id == ofFirst.Id.Value);
        Assert.Equal(TouchName, ran.CommandName);
        Assert.Equal("0 * * * *", ran.Cron);
        Assert.Equal("UTC", ran.TimeZoneId);
        Assert.Equal(ScheduledTaskState.Active, ran.State);
        Assert.Equal(ScheduledRunStatus.Completed, ran.LastRunStatus);
        Assert.Equal(0, ran.LastRunFailed);
        Assert.NotNull(ran.LastRunAtUtc);
        Assert.Equal(1, ran.TargetCount);
        Assert.Equal(2, Assert.Single(items, item => item.Id == ofBoth.Id.Value).TargetCount);
    }

    [Fact]
    public async Task TasksForTarget_HidesDeleted()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTask task = await CreateTaskAsync(tree.North, TouchName, probe);
        await DeleteTaskAsync(task);

        Assert.Empty((await SendAsync(manager, tree.North, new GetScheduledTasksForTargetQuery(Schema, probe.Id.Value))).Value);
        Assert.Equal(
            ErrorOrType.NotFound,
            (await SendVersionedAsync(manager, tree.North, new GetScheduledTaskByIdQuery(Schema, task.Id))).FirstError.Type);
    }

    [Fact]
    public async Task TasksForTarget_HidesOutOfReach()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.South);
        ScheduledTask southern = await CreateTaskAsync(tree.South, TouchName, probe);

        // A North holder sees nothing of South; asking with South as the current scope is refused outright.
        Assert.Empty((await SendAsync(manager, tree.North, new GetScheduledTasksForTargetQuery(Schema, probe.Id.Value))).Value);
        Assert.Equal(
            ErrorOrType.Forbidden,
            (await SendAsync(manager, tree.South, new GetScheduledTasksForTargetQuery(Schema, probe.Id.Value))).FirstError.Type);
        Assert.Equal(
            ErrorOrType.NotFound,
            (await SendVersionedAsync(manager, tree.North, new GetScheduledTaskByIdQuery(Schema, southern.Id))).FirstError.Type);

        // Its runs are as hidden as the task.
        await FireAsync(southern);
        Assert.Empty((await SendAsync(manager, tree.North, new GetScheduledTaskRunsQuery(Schema, southern.Id, Paggination.Default))).Value.Data);

        TestCaller southManager = await tree.CreateGrantedCallerAsync(tree.South, ScheduledTaskPermissions.Manage.Permission);
        Assert.Single((await SendAsync(southManager, tree.South, new GetScheduledTasksForTargetQuery(Schema, probe.Id.Value))).Value);
    }

    [Fact]
    public async Task GetById_ReturnsTheTaskWithItsTargetsAndVersion()
    {
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder2);
        ScheduledTask task = await CreateTaskAsync(tree.North, TouchName, first, second);

        ErrorOr<Versioned<ScheduledTaskResponse>> result =
            await SendVersionedAsync(manager, tree.North, new GetScheduledTaskByIdQuery(Schema, task.Id));

        Assert.False(result.IsError);
        ScheduledTaskResponse response = result.Value.Value;
        Assert.Equal(task.Id.Value, response.Id);
        Assert.Equal(TouchName, response.CommandName);
        Assert.Equal(task.DelegatingUserId.Value, response.DelegatingUserId);
        Assert.Equal(ScheduledTaskState.Active, response.State);
        Assert.Equal(
            new[] { first.Id.Value, second.Id.Value }.Order(),
            response.Targets.Select(target => target.TargetId).Order());
        Assert.NotEqual(Guid.Empty, result.Value.Version.Value);
    }

    [Fact]
    public async Task Runs_NewestFirst_Paged()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTask task = await CreateTaskAsync(tree.North, TouchName, probe);

        for (int fired = 0; fired < 3; fired++)
        {
            await FireAsync(task);

            if (fired < 2)
            {
                await Engine.DrainAsync(host.Services);
            }
        }

        ListQueryResponse<ScheduledRunListItem> firstPage =
            (await SendAsync(manager, tree.North, new GetScheduledTaskRunsQuery(Schema, task.Id, Paggination.Create(2, 1)))).Value;
        ListQueryResponse<ScheduledRunListItem> secondPage =
            (await SendAsync(manager, tree.North, new GetScheduledTaskRunsQuery(Schema, task.Id, Paggination.Create(2, 2)))).Value;

        Assert.Equal(3, firstPage.Total);
        Assert.Equal(3, secondPage.Total);
        Assert.Equal(2, firstPage.Data.Count);
        ScheduledRunListItem oldest = Assert.Single(secondPage.Data);

        ScheduledRunListItem[] all = [.. firstPage.Data, oldest];
        Assert.Equal(all.OrderByDescending(run => run.StartedAtUtc), all);
        Assert.Equal(ScheduledRunStatus.Running, all[0].Status);
        Assert.Equal(ScheduledRunStatus.Completed, oldest.Status);
        Assert.All(all, run => Assert.Equal(ScheduledRunTrigger.Scheduled, run.Trigger));
        Assert.Equal(task.DelegatingUserId.Value, all[0].ActingUserId);
        Assert.Equal(1, all[0].Total);
        Assert.Equal(1, oldest.Succeeded);
        Assert.NotNull(oldest.CompletedAtUtc);
    }

    [Fact]
    public async Task RunItems_CarryErrors()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTask task = await CreateTaskAsync(tree.North, "tst.probes.gone", probe);
        await FireAsync(task);
        await Engine.DrainAsync(host.Services);

        Guid runId = (await SendAsync(manager, tree.North, new GetScheduledTaskRunsQuery(Schema, task.Id, Paggination.Default))).Value.Data.Single().Id;

        ScheduledRunItemListItem item =
            Assert.Single((await SendAsync(manager, tree.North, new GetScheduledTaskRunItemsQuery(Schema, runId))).Value);

        Assert.Equal(probe.Id.Value, item.TargetId);
        Assert.Equal(ScheduledRunItemStatus.Failed, item.Status);
        Assert.Equal(ScheduledTaskErrors.UnknownCommand.Code, item.ErrorCode);
        Assert.Equal(ScheduledTaskErrors.UnknownCommand.Description, item.ErrorDescription);
        Assert.Equal(1, item.Attempts);
        Assert.NotNull(item.CompletedAtUtc);

        // Out of reach, the same run is empty.
        TestCaller other = await tree.CreateGrantedCallerAsync(tree.South, ScheduledTaskPermissions.Manage.Permission);
        Assert.Empty((await SendAsync(other, tree.South, new GetScheduledTaskRunItemsQuery(Schema, runId))).Value);
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

        IReadOnlyCollection<ScheduledRunListItem> runs =
            (await SendAsync(manager, tree.North, new GetOneOffRunsForTargetQuery(Schema, probe.Id.Value, 10))).Value;

        Assert.Equal([newest, oldest], runs.Select(run => run.Id));
        Assert.All(runs, run => Assert.Equal(ScheduledRunTrigger.OneOff, run.Trigger));
        Assert.Equal(
            [newest],
            (await SendAsync(manager, tree.North, new GetOneOffRunsForTargetQuery(Schema, probe.Id.Value, 1))).Value.Select(run => run.Id));
    }

    [Fact]
    public async Task WithoutManage_IsForbidden()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTask task = await CreateTaskAsync(tree.North, TouchName, probe);
        TestCaller toucher = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);

        Assert.Equal(ErrorOrType.Forbidden, (await SendAsync(toucher, tree.North, new GetScheduledTasksForTargetQuery(Schema, probe.Id.Value))).FirstError.Type);
        Assert.Equal(ErrorOrType.Forbidden, (await SendVersionedAsync(toucher, tree.North, new GetScheduledTaskByIdQuery(Schema, task.Id))).FirstError.Type);
        Assert.Equal(ErrorOrType.Forbidden, (await SendAsync(toucher, tree.North, new GetScheduledTaskRunsQuery(Schema, task.Id, Paggination.Default))).FirstError.Type);
        Assert.Equal(ErrorOrType.Forbidden, (await SendAsync(toucher, tree.North, new GetScheduledTaskRunItemsQuery(Schema, Guid.NewGuid()))).FirstError.Type);
        Assert.Equal(ErrorOrType.Forbidden, (await SendAsync(toucher, tree.North, new GetOneOffRunsForTargetQuery(Schema, probe.Id.Value, 5))).FirstError.Type);
    }

    [Fact]
    public async Task UnknownSchema_IsRefused_NotComposedIntoSql()
    {
        ErrorOr<IReadOnlyCollection<ScheduledTaskListItem>> result =
            await SendAsync(manager, tree.North, new GetScheduledTasksForTargetQuery("tst\"; DROP TABLE x; --", Guid.NewGuid()));

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

    private IServiceScope Circuit(IUserContext caller, Guid currentScope)
    {
        IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(currentScope);

        return circuit;
    }

    private static IScheduledTaskStore Store(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(Schema);

    private async Task FireAsync(ScheduledTask task)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IScheduledTaskRunner>().FireAsync(Schema, task.Id, CancellationToken.None);
    }

    private async Task<ScheduledTask> CreateTaskAsync(Guid scopeId, string commandName, params ScheduleProbe[] targets)
    {
        TestCaller user = await tree.CreateGrantedCallerAsync(scopeId, ScheduleProbePermissions.Touch.Permission);
        string arguments = ScheduledCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("hello")));

        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);

        var task = ScheduledTask.Create(
            commandName, arguments, [.. targets.Select(probe => new ScheduledTaskTarget(probe.Id.Value, null))], "0 * * * *", "UTC", user.Id);
        IScheduledTaskStore store = Store(scope);
        store.AddTask(task);
        await store.SaveAsync(CancellationToken.None);

        return task;
    }

    private async Task DeleteTaskAsync(ScheduledTask task)
    {
        using IServiceScope scope = host.Services.CreateScope();
        IScheduledTaskStore store = Store(scope);
        store.RemoveTask((await store.GetTaskAsync(task.Id, ignoreScope: true, CancellationToken.None))!);
        await store.SaveAsync(CancellationToken.None);
    }

    private async Task<Guid> AddOneOffRunAsync(Guid scopeId, Guid targetId, DateTime startedAtUtc)
    {
        string arguments = ScheduledCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("once")));

        using IServiceScope scope = host.Services.CreateScope();
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(scopeId))!;
        var run = ScheduledTaskRun.Start(
            new ScheduledRunDefinition(null, ScheduledRunTrigger.OneOff, manager.Id, scopeId, path, TouchName, arguments), 1, startedAtUtc);
        IScheduledTaskStore store = Store(scope);
        store.AddRun(run, [ScheduledTaskRunItem.Create(run.Id, targetId, null)]);
        await store.SaveAsync(CancellationToken.None);

        return run.Id;
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
