using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// A cancel reaches an item that is already running: the executor's heartbeat reads the run's cancel request (sent from
/// another scope, standing in for another process) and stops the command, which ends Cancelled, never retried, before
/// its timeout; a send that finished first keeps its outcome; a cancel that lands between the claim and the read sends
/// nothing; and a send that fails retryably once its run was cancelled ends Cancelled, not Retrying.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RunItemCancelTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private static readonly TimeSpan CancelReachesWithin = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeScheduledTaskEngine Engine => host.Services.GetRequiredService<FakeScheduledTaskEngine>();

    private ProbeGate Gate => host.Services.GetRequiredService<ProbeGate>();

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder =>
        {
            builder.UseSetting("Sergin:ScheduledTasks:HeartbeatInterval", "00:00:00.200");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<FakeScheduledTaskEngine>();
                services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(provider => provider.GetRequiredService<FakeScheduledTaskEngine>()));
                services.AddSingleton<ProbeGate>();
                services.AddSingleton<ProbeRunCanceller>();
                services.AddSingleton<CancelAfterClaim>();
                services.AddTransient<IRequestHandler<SlowProbeCommand, ErrorOr<TouchProbeResponse>>, SlowProbeCommandHandler>();
                services.AddTransient<IRequestHandler<TimedProbeCommand, ErrorOr<TouchProbeResponse>>, TimedProbeCommandHandler>();
                services.AddTransient<IRequestHandler<CancelThenThrowProbeCommand, ErrorOr<TouchProbeResponse>>, CancelThenThrowProbeCommandHandler>();
                DecorateStore(services);
            });
        });

        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync()
    {
        Gate.Release();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Cancel_ReachesARunningItem_OnItsNextHeartbeat()
    {
        TestCaller caller = await CallerAsync();
        BackgroundRunRef run = await StartAsync(caller, new SlowProbeCommand(new(Guid.Empty), "slow"));

        // The gate is never released: only the cancel ends the send.
        Task drain = Engine.DrainAsync(host.Services);
        await WaitUntilAsync(() => Gate.Entered == 1);

        ErrorOr<Success> cancelled = await CancelAsync(caller, run);
        Assert.False(cancelled.IsError, cancelled.IsError ? cancelled.FirstError.Code : string.Empty);

        await drain.WaitAsync(CancelReachesWithin);

        (ScheduledTaskRun stored, ScheduledTaskRunItem item) = await ReadAsync(run);
        Assert.Equal(ScheduledRunItemStatus.Cancelled, item.Status);
        Assert.NotNull(item.CompletedAtUtc);
        Assert.Equal(1, item.Attempts);
        Assert.Equal((ScheduledRunStatus.Completed, 1, 0, 0), (stored.Status, stored.Cancelled, stored.Succeeded, stored.Failed));
        Assert.Empty(Engine.QueuedItems);
    }

    [Fact]
    public async Task CancelBeatsTimeout()
    {
        TestCaller caller = await CallerAsync();
        // A one-second timeout around a thirty-second send.
        BackgroundRunRef run = await StartAsync(caller, new TimedProbeCommand(new(Guid.Empty)));
        DateTime startedAt = DateTime.UtcNow;

        Task drain = Engine.DrainAsync(host.Services);
        await WaitUntilAsync(async () => (await ReadAsync(run)).Item.Status == ScheduledRunItemStatus.Running);
        TimeSpan untilCancel = TimeSpan.FromMilliseconds(300) - (DateTime.UtcNow - startedAt);

        if (untilCancel > TimeSpan.Zero)
        {
            await Task.Delay(untilCancel);
        }

        ErrorOr<Success> cancelled = await CancelAsync(caller, run);
        Assert.False(cancelled.IsError, cancelled.IsError ? cancelled.FirstError.Code : string.Empty);
        await drain.WaitAsync(WaitLimit);

        (ScheduledTaskRun stored, ScheduledTaskRunItem item) = await ReadAsync(run);
        Assert.Equal(ScheduledRunItemStatus.Cancelled, item.Status);
        Assert.NotEqual(ScheduledTaskErrors.TimedOutCode, item.ErrorCode);
        Assert.Equal(1, item.Attempts);
        Assert.Equal((ScheduledRunStatus.Completed, 1, 0), (stored.Status, stored.Cancelled, stored.Failed));
    }

    [Fact]
    public async Task CancelAfterTheSendFinished_KeepsTheOutcome()
    {
        Gate.Release();
        TestCaller caller = await CallerAsync();
        BackgroundRunRef run = await StartAsync(caller, new SlowProbeCommand(new(Guid.Empty), "slow"));
        await Engine.DrainAsync(host.Services);
        Assert.Equal(ScheduledRunItemStatus.Succeeded, (await ReadAsync(run)).Item.Status);

        ErrorOr<Success> cancelled = await CancelAsync(caller, run);

        Assert.True(cancelled.IsError);
        Assert.Equal(ScheduledTaskErrors.RunFinished.Code, cancelled.FirstError.Code);
        (ScheduledTaskRun stored, ScheduledTaskRunItem item) = await ReadAsync(run);
        Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status);
        Assert.Equal((ScheduledRunStatus.Completed, 1, 0), (stored.Status, stored.Succeeded, stored.Cancelled));
        Assert.Null(stored.CancelRequestedAtUtc);
    }

    [Fact]
    public async Task CancelBetweenClaimAndRead_SendsNothing()
    {
        // Released first: were the item sent, the test would fail on the counter rather than hang on the gate.
        Gate.Release();
        TestCaller caller = await CallerAsync();
        BackgroundRunRef run = await StartAsync(caller, new SlowProbeCommand(new(Guid.Empty), "slow"));

        // The store stamps the cancel right after the claim succeeds, before the executor reads the run.
        host.Services.GetRequiredService<CancelAfterClaim>().RunId = run.RunId;
        await Engine.DrainAsync(host.Services);

        Assert.Equal(0, Gate.Entered);
        (ScheduledTaskRun stored, ScheduledTaskRunItem item) = await ReadAsync(run);
        Assert.Equal(ScheduledRunItemStatus.Cancelled, item.Status);
        Assert.NotNull(item.CompletedAtUtc);
        Assert.Equal(1, item.Attempts);
        Assert.Equal((ScheduledRunStatus.Completed, 1), (stored.Status, stored.Cancelled));
    }

    [Fact]
    public async Task RetryableFailureAfterCancel_EndsCancelled_NotRetrying()
    {
        TestCaller caller = await CallerAsync();
        BackgroundRunRef run = await StartAsync(caller, new CancelThenThrowProbeCommand(new(Guid.Empty)));
        host.Services.GetRequiredService<ProbeRunCanceller>().RunId = run.RunId;

        // The send cancels its own run (committed elsewhere, while the item is Running) and then throws: an Unexpected
        // error that would otherwise be retried, and whose retry the claim would then refuse, leaving the run open.
        await Engine.DrainAsync(host.Services);

        (ScheduledTaskRun stored, ScheduledTaskRunItem item) = await ReadAsync(run);
        Assert.Equal(ScheduledRunItemStatus.Cancelled, item.Status);
        Assert.Equal(1, item.Attempts);
        Assert.Equal((ScheduledRunStatus.Completed, 1, 0), (stored.Status, stored.Cancelled, stored.Failed));
        Assert.Empty(Engine.QueuedItems);
    }

    private static void DecorateStore(IServiceCollection services)
    {
        ServiceDescriptor inner = services.Last(descriptor =>
            descriptor.ServiceType == typeof(IScheduledTaskStore) && Equals(descriptor.ServiceKey, ScheduledTasksDbContext.Schema));
        services.Remove(inner);
        services.AddKeyedScoped<IScheduledTaskStore>(
            ScheduledTasksDbContext.Schema,
            (provider, key) => new CancelAfterClaimStore(
                (IScheduledTaskStore)inner.KeyedImplementationFactory!(provider, key),
                provider.GetRequiredService<CancelAfterClaim>()));
    }

    private static async Task WaitUntilAsync(Func<bool> condition) => await WaitUntilAsync(() => Task.FromResult(condition()));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        DateTime giveUpAt = DateTime.UtcNow + WaitLimit;

        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < giveUpAt, "The condition did not hold in time.");
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    private Task<TestCaller> CallerAsync() => tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);

    private async Task<BackgroundRunRef> StartAsync(TestCaller caller, IBaseCommand command)
    {
        var probe = ScheduleProbe.Create();

        using (IServiceScope scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.Feeder1);
            ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();
            context.Probes.Add(probe);
            await context.SaveChangesAsync();
        }

        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        ErrorOr<BackgroundRunStartedResponse> started = await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .StartAsync(command, [new BackgroundTarget(probe.Id.Value)]);
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        return started.Value.Run;
    }

    private async Task<ErrorOr<Success>> CancelAsync(IUserContext caller, BackgroundRunRef run)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendAsync(new CancelBackgroundRunCommand(run.Schema, run.RunId));
    }

    private async Task<(ScheduledTaskRun Run, ScheduledTaskRunItem Item)> ReadAsync(BackgroundRunRef run)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return (
            await context.ScheduledTaskRuns.AsNoTracking().SingleAsync(one => one.Id == run.RunId),
            await context.ScheduledTaskRunItems.AsNoTracking().SingleAsync(item => item.RunId == run.RunId));
    }

    /// <summary>The run whose cancel <see cref="CancelAfterClaimStore"/> stamps right after a claim; none while empty.</summary>
    private sealed class CancelAfterClaim
    {
        public Guid? RunId { get; set; }
    }

    /// <summary>
    /// The real store, except that a successful claim is followed at once by a cancel of the armed run: the cancel lands
    /// between the executor's claim and its read of the run, deterministically, with no race to win.
    /// </summary>
    private sealed class CancelAfterClaimStore(IScheduledTaskStore inner, CancelAfterClaim armed) : IScheduledTaskStore
    {
        public string Schema => inner.Schema;

        public async Task<bool> TryClaimRunItemAsync(Guid runItemId, DateTime now, DateTime staleBefore, CancellationToken ct)
        {
            bool claimed = await inner.TryClaimRunItemAsync(runItemId, now, staleBefore, ct);

            if (claimed && armed.RunId is { } runId)
            {
                await inner.CancelRunAsync(runId, now, ct);
            }

            return claimed;
        }

        public Task<ScheduledTask?> GetTaskAsync(ScheduledTaskId id, bool ignoreScope, CancellationToken ct) => inner.GetTaskAsync(id, ignoreScope, ct);

        public (Guid ScopeId, string ScopePath) ScopeOf(ScheduledTask task) => inner.ScopeOf(task);

        public void AddTask(ScheduledTask task) => inner.AddTask(task);

        public void RemoveTask(ScheduledTask task) => inner.RemoveTask(task);

        public void AddRun(ScheduledTaskRun run, IReadOnlyCollection<ScheduledTaskRunItem> items) => inner.AddRun(run, items);

        public Task<bool> HasUnfinishedRunAsync(ScheduledTaskId id, CancellationToken ct) => inner.HasUnfinishedRunAsync(id, ct);

        public Task<(ScheduledTaskRun Run, ScheduledTaskRunItem Item)?> GetRunItemAsync(Guid runItemId, CancellationToken ct) =>
            inner.GetRunItemAsync(runItemId, ct);

        public Task<ScheduledTaskRun?> GetRunAsync(Guid runId, CancellationToken ct) => inner.GetRunAsync(runId, ct);

        public Task CompleteRunIfFinishedAsync(Guid runId, DateTime at, CancellationToken ct) => inner.CompleteRunIfFinishedAsync(runId, at, ct);

        public Task CancelRunAsync(Guid runId, DateTime at, CancellationToken ct) => inner.CancelRunAsync(runId, at, ct);

        public Task<IReadOnlyCollection<ScheduledTaskTriggerState>> ListTriggersAsync(CancellationToken ct) => inner.ListTriggersAsync(ct);

        public Task<RunItemHeartbeat> TouchRunItemAsync(Guid runItemId, DateTime now, CancellationToken ct) => inner.TouchRunItemAsync(runItemId, now, ct);

        public Task SetRunItemProgressAsync(Guid runItemId, int percent, DateTime now, CancellationToken ct) =>
            inner.SetRunItemProgressAsync(runItemId, percent, now, ct);

        public Task<IReadOnlyCollection<Guid>> ListStaleRunItemsAsync(DateTime quietSince, CancellationToken ct) => inner.ListStaleRunItemsAsync(quietSince, ct);

        public Task<IReadOnlyCollection<Guid>> CancelAbandonedRunItemsAsync(DateTime quietSince, DateTime at, CancellationToken ct) =>
            inner.CancelAbandonedRunItemsAsync(quietSince, at, ct);

        public Task<IReadOnlyCollection<Guid>> ListRunsToCompleteAsync(DateTime quietSinceUtc, CancellationToken ct) =>
            inner.ListRunsToCompleteAsync(quietSinceUtc, ct);

        public Task<bool> RelinkStaleRunItemAsync(Guid runItemId, Guid engineJobId, DateTime now, DateTime quietSince, CancellationToken ct) =>
            inner.RelinkStaleRunItemAsync(runItemId, engineJobId, now, quietSince, ct);

        public Task<int> PurgeAsync(DateTime olderThanUtc, CancellationToken ct) => inner.PurgeAsync(olderThanUtc, ct);

        public Task<bool> TrySaveStartedRunAsync(CancellationToken ct) => inner.TrySaveStartedRunAsync(ct);

        public Task SaveAsync(CancellationToken ct) => inner.SaveAsync(ct);
    }
}
