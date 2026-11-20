using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// A collection task (<c>ITaskCommand&lt;TRoot&gt;</c>): one item with no target per run, its permissions checked on the
/// run's scope, and at most one Running run per task name and scope — the start's check is advisory, the partial unique
/// index <c>ix_task_runs_collection_running</c> is the guarantee, and a cron fire that loses is a Skipped run.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CollectionTaskTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string SyncName = "tst.schedule-probe.sync-probes";

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeTaskEngine Engine => host.Services.GetRequiredService<FakeTaskEngine>();

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<FakeTaskEngine>();
                services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(provider => provider.GetRequiredService<FakeTaskEngine>()));
                services.AddSingleton(ModuleConfigurationSource.FromTypesInSchema(TasksDbContext.Schema, typeof(SyncProbesCommandConfiguration)));
                services.AddTransient<IRequestHandler<SyncProbesCommand, ErrorOr<Success>>, SyncProbesCommandHandler>();
            }));

        await TaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task CollectionTask_StartsOneItemWithNoTarget()
    {
        TestCaller caller = await SyncCallerAsync(tree.Company);

        ErrorOr<TaskRunStartedResponse> started = await StartAsync(caller, tree.North);

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        TaskRun run = await RunAsync(started.Value.Run.RunId);
        Assert.Equal(TaskRunTargetKind.Collection, run.TargetKind);
        Assert.Equal(SyncName, run.CommandName);
        Assert.Equal(tree.North, run.ScopeId);
        Assert.Equal(1, run.Total);
        TaskRunItem item = Assert.Single(await ItemsAsync(run.Id));
        Assert.Null(item.TargetId);
        Assert.Equal("Collection", await TargetKindColumnAsync(run.Id));

        await Engine.DrainAsync(host.Services);

        Assert.Equal(TaskRunItemStatus.Succeeded, Assert.Single(await ItemsAsync(run.Id)).Status);
        TaskRun completed = await RunAsync(run.Id);
        Assert.Equal((TaskRunStatus.Completed, 1, 0), (completed.Status, completed.Succeeded, completed.Failed));
    }

    [Fact]
    public async Task CollectionTask_WithATarget_IsRefused()
    {
        TestCaller caller = await SyncCallerAsync(tree.Company);

        ErrorOr<TaskRunStartedResponse> started = await StartAsync(caller, tree.North, [new TaskTarget(Guid.CreateVersion7())]);

        Assert.True(started.IsError);
        Error error = Assert.Single(started.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(StartTaskCommand.Targets), error.Code);
        Assert.Equal(TaskTexts.CollectionTargets.Default, error.Description);
        await AssertNoRunAsync();
    }

    [Fact]
    public async Task CollectionTask_PermissionCheckedOnRunScope()
    {
        TestCaller caller = await SyncCallerAsync(tree.North);

        ErrorOr<TaskRunStartedResponse> started = await StartAsync(caller, tree.South);

        Assert.True(started.IsError);
        Assert.Equal(ErrorType.Forbidden, started.FirstError.Type);
        await AssertNoRunAsync();
    }

    [Fact]
    public async Task SecondStart_SameScope_IsRefused()
    {
        TestCaller caller = await SyncCallerAsync(tree.Company);
        Assert.False((await StartAsync(caller, tree.North)).IsError);

        ErrorOr<TaskRunStartedResponse> second = await StartAsync(caller, tree.North);

        Assert.True(second.IsError);
        Error error = Assert.Single(second.Errors);
        Assert.Equal("Tasks.AlreadyRunning", error.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Contains(SyncName, error.Description, StringComparison.Ordinal);
        Assert.Single(await RunsAsync());
        Assert.Single(Engine.QueuedItems);
    }

    [Fact]
    public async Task SameTask_OtherScope_Starts()
    {
        TestCaller caller = await SyncCallerAsync(tree.Company);
        Assert.False((await StartAsync(caller, tree.North)).IsError);

        ErrorOr<TaskRunStartedResponse> other = await StartAsync(caller, tree.South);

        Assert.False(other.IsError, other.IsError ? other.FirstError.Code : string.Empty);
        Assert.Equal(2, (await RunsAsync()).Count(run => run.Status == TaskRunStatus.Running));
    }

    [Fact]
    public async Task SecondStart_AfterTheFirstFinished_Starts()
    {
        TestCaller caller = await SyncCallerAsync(tree.Company);
        Assert.False((await StartAsync(caller, tree.North)).IsError);
        await Engine.DrainAsync(host.Services);

        ErrorOr<TaskRunStartedResponse> again = await StartAsync(caller, tree.North);

        Assert.False(again.IsError, again.IsError ? again.FirstError.Code : string.Empty);
    }

    // Review focus 3: both may pass the advisory check; the index lets one insert win, and the other maps to AlreadyRunning.
    [Fact]
    public async Task RacingStarts_OneWinsOneAlreadyRunning()
    {
        TestCaller caller = await SyncCallerAsync(tree.Company);

        for (int round = 0; round < 5; round++)
        {
            ErrorOr<TaskRunStartedResponse>[] results =
                await Task.WhenAll(StartAsync(caller, tree.North), StartAsync(caller, tree.North));

            Assert.Equal(1, results.Count(result => !result.IsError));
            ErrorOr<TaskRunStartedResponse> loser = Assert.Single(results, result => result.IsError);
            Assert.Equal("Tasks.AlreadyRunning", loser.FirstError.Code);
            Assert.Equal(1, (await RunsAsync()).Count(run => run.Status == TaskRunStatus.Running));

            await Engine.DrainAsync(host.Services);
        }

        Assert.Equal(5, (await RunsAsync()).Count);
    }

    [Fact]
    public async Task CronFire_WhileRunning_IsSkipped()
    {
        TestCaller caller = await SyncCallerAsync(tree.Company);
        Assert.False((await StartAsync(caller, tree.North)).IsError);
        TaskSchedule schedule = await CreateScheduleAsync(caller, tree.North);

        await FireAsync(schedule);

        TaskRun skipped = Assert.Single(await RunsAsync(), run => run.ScheduleId == schedule.Id);
        Assert.Equal(TaskRunStatus.Skipped, skipped.Status);
        Assert.Equal(TaskRunTargetKind.Collection, skipped.TargetKind);
        Assert.Empty(await ItemsAsync(skipped.Id));
        Assert.Single(Engine.QueuedItems);

        // Once the manual run finishes, the next firing starts a run of its own.
        await Engine.DrainAsync(host.Services);
        await FireAsync(schedule);

        TaskRun fired = Assert.Single(await RunsAsync(), run => run.ScheduleId == schedule.Id && run.Status == TaskRunStatus.Running);
        Assert.Null(Assert.Single(await ItemsAsync(fired.Id)).TargetId);
    }

    [Fact]
    public async Task Index_RefusesDuplicateWhenCheckBypassed()
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        TestCaller caller = await SyncCallerAsync(tree.Company);

        context.TaskRuns.Add(CollectionRun(caller));
        await context.SaveChangesAsync();
        context.TaskRuns.Add(CollectionRun(caller));

        DbUpdateException refused = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        PostgresException postgres = Assert.IsType<PostgresException>(refused.InnerException);
        Assert.Equal("23505", postgres.SqlState);
        Assert.Equal("ix_task_runs_collection_running", postgres.ConstraintName);
    }

    // The starter's mapping of the index's refusal, with the advisory check out of the way: the store names which index refused.
    [Fact]
    public async Task StoreSave_OfASecondRunningCollectionRun_AnswersCollectionRunning_AndDiscardsIt()
    {
        TestCaller caller = await SyncCallerAsync(tree.Company);

        using (IServiceScope scope = host.Services.CreateScope())
        {
            ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(TasksDbContext.Schema);
            store.AddRun(CollectionRun(caller), []);
            Assert.Equal(StartedRunSave.Saved, await store.TrySaveStartedRunAsync(CancellationToken.None));
        }

        using (IServiceScope scope = host.Services.CreateScope())
        {
            ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(TasksDbContext.Schema);
            store.AddRun(CollectionRun(caller), []);
            Assert.Equal(StartedRunSave.CollectionRunning, await store.TrySaveStartedRunAsync(CancellationToken.None));

            // The refused run was discarded, so the scope's next save writes nothing more.
            await store.SaveAsync(CancellationToken.None);
        }

        Assert.Single(await RunsAsync());
    }

    private TaskRun CollectionRun(TestCaller caller) =>
        TaskRun.Start(
            new TaskRunDefinition(
                null, TaskRunTrigger.Manual, caller.Id, tree.North, "company.north", SyncName, "{}", TaskRunTargetKind.Collection),
            total: 1,
            startedAtUtc: DateTime.UtcNow);

    private Task<TestCaller> SyncCallerAsync(Guid scopeId) =>
        tree.CreateGrantedCallerAsync(scopeId, ScheduleProbePermissions.Touch.Permission);

    private async Task<ErrorOr<TaskRunStartedResponse>> StartAsync(IUserContext caller, Guid scopeId, IReadOnlyCollection<TaskTarget>? targets = null)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().StartTaskAsync(new SyncProbesCommand(), targets ?? []);
    }

    private async Task<TaskSchedule> CreateScheduleAsync(TestCaller user, Guid scopeId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);

        var schedule = TaskSchedule.Create(
            SyncName, TaskCommandFactory.ToArguments(new SyncProbesCommand()), [], "0 * * * *", "UTC", user.Id);
        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(TasksDbContext.Schema);
        store.AddTask(schedule);
        await store.SaveAsync(CancellationToken.None);

        return schedule;
    }

    private async Task FireAsync(TaskSchedule schedule)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITaskScheduleRunner>()
            .FireAsync(TasksDbContext.Schema, schedule.Id, CancellationToken.None);
    }

    private async Task<TaskRun> RunAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.TaskRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
    }

    private async Task<IReadOnlyCollection<TaskRun>> RunsAsync()
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.TaskRuns.AsNoTracking().OrderBy(run => run.StartedAtUtc).ToListAsync();
    }

    private async Task<IReadOnlyCollection<TaskRunItem>> ItemsAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.TaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
    }

    private async Task<string> TargetKindColumnAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.Database
            .SqlQuery<string>($"SELECT target_kind AS \"Value\" FROM tst.task_runs WHERE id = {runId}")
            .SingleAsync();
    }

    private async Task AssertNoRunAsync()
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        Assert.False(await context.TaskRuns.AnyAsync());
        Assert.False(await context.TaskRunItems.AnyAsync());
        Assert.Empty(Engine.QueuedItems);
    }
}
