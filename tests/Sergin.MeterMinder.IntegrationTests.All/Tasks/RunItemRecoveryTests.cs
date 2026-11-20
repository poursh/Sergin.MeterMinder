using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Times;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Hosts.Tasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// The one recovery for run items: an unfinished item quiet for longer than StaleAfter is handed to the engine again,
/// whatever lost its job, and the executor's claim in the database makes every extra delivery harmless. The clock is the
/// test's, so "quiet for fifteen minutes" is a step of the clock, not a wait.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RunItemRecoveryTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Schema = TasksDbContext.Schema;
    private const string TouchName = "tst.probes.touch";

    private static readonly TimeSpan PastStale = new TaskOptions().StaleAfter + TimeSpan.FromSeconds(1);

    private readonly TestClock clock = new();
    private readonly FakeTaskEngine engine = new();

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private ProbeFailureSwitch Failures => host.Services.GetRequiredService<ProbeFailureSwitch>();

    private ProbeSendRecorder Recorder => host.Services.GetRequiredService<ProbeSendRecorder>();

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(engine));
            services.Replace(ServiceDescriptor.Singleton<IDateTimeProvider>(clock));
            services.AddSingleton<ProbeFailureSwitch>();
            services.AddSingleton<ProbeSendRecorder>();
            services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<Success>>, TouchProbeCommandHandler>();
            services.AddScoped<IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
            services.AddTransient<IPipelineBehavior<TouchProbeCommand, ErrorOr<Success>>, RecordingProbeBehavior>();
        }));

        await TaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task RetryingItem_WhoseJobIsLost_IsHandedOverAfterStaleAfter_AndTheRunCompletes()
    {
        (Guid run, Guid item) = await StartRunAsync();
        Failures.ThrowTimes = 1;

        // The attempt throws and asks for a retry, and the retry job is never made.
        Assert.True(await ExecuteAsync(item));
        Assert.Equal(TaskRunItemStatus.Retrying, (await ItemAsync(item)).Status);

        Assert.Equal(0, await RecoverAsync());
        clock.Advance(PastStale);
        Assert.Equal(1, await RecoverAsync());
        Assert.Equal(new TaskRunItemReference(Schema, item), Assert.Single(engine.QueuedItems));

        await engine.DrainAsync(host.Services);

        TaskRunItem done = await ItemAsync(item);
        Assert.Equal(TaskRunItemStatus.Succeeded, done.Status);
        Assert.Equal(2, done.Attempts);
        Assert.Equal(TaskRunStatus.Completed, (await RunAsync(run)).Status);
    }

    [Fact]
    public async Task RunningItem_LeftByACrash_IsReclaimed_AFreshOneIsNot()
    {
        (Guid run, Guid item) = await StartRunAsync();

        // A delivery claimed the item and its host died mid-send.
        Assert.True(await ClaimAsync(item));

        Assert.False(await ExecuteAsync(item));
        Assert.Equal(0, await RecoverAsync());
        Assert.Empty(Recorder.Sends);
        Assert.Equal(TaskRunItemStatus.Running, (await ItemAsync(item)).Status);

        clock.Advance(PastStale);
        Assert.Equal(1, await RecoverAsync());
        await engine.DrainAsync(host.Services);

        TaskRunItem done = await ItemAsync(item);
        Assert.Equal(TaskRunItemStatus.Succeeded, done.Status);
        Assert.Equal(2, done.Attempts);
        Assert.Single(Recorder.Sends);
        Assert.Equal(TaskRunStatus.Completed, (await RunAsync(run)).Status);
    }

    [Fact]
    public async Task TwoConcurrentDeliveries_SendTheCommandOnce()
    {
        (Guid run, Guid item) = await StartRunAsync();

        bool[] retries = await Task.WhenAll(ExecuteAsync(item), ExecuteAsync(item), ExecuteAsync(item));

        Assert.All(retries, Assert.False);
        Assert.Single(Recorder.Sends);
        TaskRunItem done = await ItemAsync(item);
        Assert.Equal(TaskRunItemStatus.Succeeded, done.Status);
        Assert.Equal(1, done.Attempts);
        Assert.Equal(TaskRunStatus.Completed, (await RunAsync(run)).Status);
    }

    [Fact]
    public async Task ItemWhoseEveryAttemptIsLost_EndsExhausted_AndTheRunCompletes()
    {
        (Guid run, Guid item) = await StartRunAsync();
        int allowed = new TaskOptions().MaxAttempts;

        for (int attempt = 0; attempt < allowed; attempt++)
        {
            Assert.True(await ClaimAsync(item));
            clock.Advance(PastStale);
        }

        Assert.Equal(1, await RecoverAsync());
        await engine.DrainAsync(host.Services);

        TaskRunItem done = await ItemAsync(item);
        Assert.Equal(TaskRunItemStatus.Failed, done.Status);
        Assert.Equal(TaskErrors.ExhaustedCode, done.ErrorCode);
        Assert.Empty(Recorder.Sends);
        TaskRun completed = await RunAsync(run);
        Assert.Equal(TaskRunStatus.Completed, completed.Status);
        Assert.Equal(1, completed.Failed);
    }

    [Fact]
    public async Task RunWhoseItemsFinished_ButWhoseCompletionWasLost_IsCompleted_AndTheNextFireStartsANewRun()
    {
        TestCaller user = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TaskScheduleId task = await CreateTaskAsync(user, await CreateProbeAsync(tree.Feeder1));
        await FireAsync(task);
        TaskRun run = Assert.Single(await RunsOfAsync(task));

        // The item's save landed and the run's completion was lost (a database fault, or shutdown, in between).
        await MarkItemsSucceededAsync(run.Id);

        Assert.Equal(0, await RecoverAsync());
        Assert.Equal(TaskRunStatus.Running, (await RunAsync(run.Id)).Status);

        clock.Advance(PastStale);
        Assert.Equal(0, await RecoverAsync());

        TaskRun completed = await RunAsync(run.Id);
        Assert.Equal(TaskRunStatus.Completed, completed.Status);
        Assert.Equal(1, completed.Succeeded);
        Assert.Equal(0, completed.Failed);

        await FireAsync(task);

        IReadOnlyCollection<TaskRun> runs = await RunsOfAsync(task);
        Assert.Equal(2, runs.Count);
        Assert.Single(runs, one => one.Status == TaskRunStatus.Running);
        Assert.DoesNotContain(runs, one => one.Status == TaskRunStatus.Skipped);
    }

    [Fact]
    public async Task Recovery_CancelsAnAbandonedItemOfACancelledRun()
    {
        (Guid run, Guid item) = await StartRunAsync();

        // A delivery claimed the item and its host died mid-send; then the run was cancelled, which leaves a Running item
        // to its delivery.
        Assert.True(await ClaimAsync(item));
        await CancelRunAsync(run);
        Assert.Equal(TaskRunItemStatus.Running, (await ItemAsync(item)).Status);

        clock.Advance(PastStale);
        Assert.Equal(0, await RecoverAsync());

        TaskRunItem done = await ItemAsync(item);
        Assert.Equal(TaskRunItemStatus.Cancelled, done.Status);
        Assert.NotNull(done.CompletedAtUtc);
        Assert.Equal(1, done.Attempts);
        Assert.Empty(engine.QueuedItems);
        Assert.Empty(Recorder.Sends);
        TaskRun completed = await RunAsync(run);
        Assert.Equal(TaskRunStatus.Completed, completed.Status);
        Assert.Equal((0, 0, 1), (completed.Succeeded, completed.Failed, completed.Cancelled));
    }

    [Theory]
    [InlineData("00:00:00", "00:01:00", "StaleAfter must be a positive")]
    [InlineData("00:15:00", "00:00:00", "RecoveryInterval must be a positive")]
    [InlineData("00:10:30", "00:01:00", "StaleAfter must exceed the longest")]
    public void Options_RefuseARecoveryThatWouldDuplicateHealthyItems(string staleAfter, string recoveryInterval, string expected)
    {
        var options = new TaskOptions
        {
            RetryIntervalsSeconds = [30, 600],
            StaleAfter = TimeSpan.Parse(staleAfter, System.Globalization.CultureInfo.InvariantCulture),
            RecoveryInterval = TimeSpan.Parse(recoveryInterval, System.Globalization.CultureInfo.InvariantCulture),
        };

        Assert.False(options.Validate(out string failure));
        Assert.Contains(expected, failure, StringComparison.Ordinal);
        Assert.StartsWith("Sergin:Tasks:", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_AcceptTheDefaults_AndAStalePeriodJustLongEnough()
    {
        var defaults = new TaskOptions { RetryIntervalsSeconds = TaskOptions.DefaultRetryIntervalsSeconds };
        var tight = new TaskOptions
        {
            RetryIntervalsSeconds = [600],
            StaleAfter = TimeSpan.FromMinutes(11),
            RecoveryInterval = TimeSpan.FromMinutes(1),
        };

        Assert.True(defaults.Validate(out _));
        Assert.True(tight.Validate(out _));
    }

    /// <summary>A one-off run of the touch probe on one fresh probe, its job queued in the fake engine and then lost.</summary>
    private async Task<(Guid Run, Guid Item)> StartRunAsync()
    {
        TestCaller user = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.Feeder1);
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var probe = ScheduleProbe.Create();
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(tree.North))!;
        string arguments = TaskCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("recovered")));
        var run = TaskRun.Start(
            new TaskRunDefinition(null, TaskRunTrigger.Once, user.Id, tree.North, path, TouchName, arguments), 1, clock.UtcNow);
        var item = TaskRunItem.Create(run.Id, probe.Id.Value, null, clock.UtcNow);

        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Schema);
        store.AddRun(run, [item]);
        await store.SaveAsync(CancellationToken.None);

        return (run.Id, item.Id);
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

    private async Task<TaskScheduleId> CreateTaskAsync(TestCaller user, ScheduleProbe target)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);
        string arguments = TaskCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("scheduled")));
        var task = TaskSchedule.Create(TouchName, arguments, [new TaskScheduleTarget(target.Id.Value, null)], "0 * * * *", "UTC", user.Id);

        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Schema);
        store.AddTask(task);
        await store.SaveAsync(CancellationToken.None);

        return task.Id;
    }

    private async Task FireAsync(TaskScheduleId task)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITaskScheduleRunner>().FireAsync(Schema, task, CancellationToken.None);
    }

    private async Task MarkItemsSucceededAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        foreach (TaskRunItem item in await context.TaskRunItems.Where(item => item.RunId == runId).ToListAsync())
        {
            item.MarkSucceeded(clock.UtcNow);
        }

        await context.SaveChangesAsync();
    }

    private async Task<IReadOnlyCollection<TaskRun>> RunsOfAsync(TaskScheduleId task)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<TasksDbContext>().TaskRuns
            .AsNoTracking().Where(run => run.ScheduleId == task).OrderBy(run => run.StartedAtUtc).ToListAsync();
    }

    private async Task<bool> ExecuteAsync(Guid item)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ITaskScheduleRunner>().ExecuteRunItemAsync(Schema, item, CancellationToken.None);
    }

    private async Task<bool> ClaimAsync(Guid item)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Schema);

        return await store.TryClaimRunItemAsync(item, clock.UtcNow, clock.UtcNow - new TaskOptions().StaleAfter, CancellationToken.None);
    }

    private async Task CancelRunAsync(Guid run)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Schema);

        await store.CancelRunAsync(run, clock.UtcNow, CancellationToken.None);
    }

    /// <summary>One recovery pass by hand: the shared test host leaves the background loop out.</summary>
    private async Task<int> RecoverAsync()
    {
        using TaskRunItemRecoveryService recovery = ActivatorUtilities.CreateInstance<TaskRunItemRecoveryService>(host.Services);

        return await recovery.RecoverOnceAsync(CancellationToken.None);
    }

    private async Task<TaskRunItem> ItemAsync(Guid id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<TasksDbContext>().TaskRunItems.AsNoTracking().SingleAsync(item => item.Id == id);
    }

    private async Task<TaskRun> RunAsync(Guid id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<TasksDbContext>().TaskRuns.AsNoTracking().SingleAsync(run => run.Id == id);
    }

    /// <summary>A clock the test moves; it starts at the real time so stamps sit beside the database's own.</summary>
    private sealed class TestClock : IDateTimeProvider
    {
        private long ticks = DateTime.UtcNow.Ticks;

        public DateTime UtcNow => new(Interlocked.Read(ref ticks), DateTimeKind.Utc);

        public void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);
    }
}
