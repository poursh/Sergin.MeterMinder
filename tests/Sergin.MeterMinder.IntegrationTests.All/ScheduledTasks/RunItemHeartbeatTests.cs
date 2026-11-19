using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Hosts.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// A run item that takes longer than the stale period keeps its activity fresh while it runs (the heartbeat), so the
/// recovery never takes it for lost and sends it again; a background command that outruns its declared timeout is failed
/// as <c>ScheduledTasks.TimedOut</c>, once, and the run completes; a host shutdown is not a timeout.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RunItemHeartbeatTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string SlowName = "tst.probes.slow";
    private const string TimedName = "tst.probes.timed";

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeScheduledTaskEngine Engine => host.Services.GetRequiredService<FakeScheduledTaskEngine>();

    private ProbeGate Gate => host.Services.GetRequiredService<ProbeGate>();

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder =>
        {
            builder.UseSetting("Sergin:ScheduledTasks:StaleAfter", "00:00:03");
            builder.UseSetting("Sergin:ScheduledTasks:RecoveryInterval", "00:00:01");
            builder.UseSetting("Sergin:ScheduledTasks:RetryIntervalsSeconds:0", "1");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<FakeScheduledTaskEngine>();
                services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(provider => provider.GetRequiredService<FakeScheduledTaskEngine>()));
                services.AddSingleton<ProbeGate>();
                services.AddTransient<IRequestHandler<SlowProbeCommand, ErrorOr<TouchProbeResponse>>, SlowProbeCommandHandler>();
                services.AddTransient<IRequestHandler<TimedProbeCommand, ErrorOr<TouchProbeResponse>>, TimedProbeCommandHandler>();
                services.AddTransient<IRequestHandler<GatedTimedProbeCommand, ErrorOr<TouchProbeResponse>>, GatedTimedProbeCommandHandler>();
                services.AddTransient<IRequestHandler<WrappingTimedProbeCommand, ErrorOr<TouchProbeResponse>>, WrappingTimedProbeCommandHandler>();
                services.AddScoped<IValidator<SlowProbeCommand>, SlowProbeCommandValidator>();
                services.AddTransient<IRequestHandler<TouchOneProbeCommand, ErrorOr<TouchProbeResponse>>, SelfCancelingProbeCommandHandler>();
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
    public async Task LongItem_IsNotResentByRecovery()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        Guid run = await StartRunAsync(user, tree.North, SlowName, SlowArguments(), probe.Id.Value);

        Task drain = Engine.DrainAsync(host.Services);
        await WaitUntilAsync(() => Gate.Entered == 1);

        // Longer than StaleAfter: without the heartbeat the Running item would look lost.
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(0, await RecoverAsync());

        Gate.Release();
        await drain;

        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status);
        Assert.Equal(1, item.Attempts);
        Assert.Equal(1, Gate.Entered);
    }

    [Fact]
    public async Task Timeout_FailsTheItemWithoutRetry()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        string arguments = ScheduledCommandFactory.ToArguments(new TimedProbeCommand(new(Guid.Empty)));
        Guid run = await StartRunAsync(user, tree.North, TimedName, arguments, probe.Id.Value);

        await Engine.DrainAsync(host.Services);

        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Failed, item.Status);
        Assert.Equal("ScheduledTasks.TimedOut", item.ErrorCode);
        Assert.Equal(1, item.Attempts);
        Assert.Equal(ScheduledRunStatus.Completed, (await RunAsync(run)).Status);
    }

    [Fact]
    public async Task Timeout_WrappedInAnotherException_IsStillATimeout()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        string arguments = ScheduledCommandFactory.ToArguments(new WrappingTimedProbeCommand(new(Guid.Empty)));
        Guid run = await StartRunAsync(user, tree.North, "tst.probes.wrapping-timed", arguments, probe.Id.Value);

        await Engine.DrainAsync(host.Services);

        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Failed, item.Status);
        Assert.Equal("ScheduledTasks.TimedOut", item.ErrorCode);
        Assert.Equal(1, item.Attempts);
        Assert.Equal(ScheduledRunStatus.Completed, (await RunAsync(run)).Status);
    }

    [Fact]
    public async Task Touch_OnAFinishedItem_ChangesNothing()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        Guid run = await StartRunAsync(user, tree.North, SlowName, SlowArguments(), probe.Id.Value);
        Gate.Release();
        await Engine.DrainAsync(host.Services);

        ScheduledTaskRunItem finished = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Succeeded, finished.Status);

        RunItemHeartbeat touched;

        using (IServiceScope scope = host.Services.CreateScope())
        {
            IScheduledTaskStore store = scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(ScheduledTasksDbContext.Schema);
            touched = await store.TouchRunItemAsync(finished.Id, finished.LastActivityAtUtc.AddHours(1), CancellationToken.None);
        }

        Assert.Equal(new RunItemHeartbeat(Touched: false, CancelRequested: false), touched);
        Assert.Equal(finished.LastActivityAtUtc, Assert.Single(await ItemsAsync(run)).LastActivityAtUtc);
    }

    [Fact]
    public async Task HostShutdown_IsNotATimeout()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        // A command with a timeout (far longer than the test), so a shutdown could be mistaken for one.
        string arguments = ScheduledCommandFactory.ToArguments(new GatedTimedProbeCommand(new(Guid.Empty)));
        Guid run = await StartRunAsync(user, tree.North, "tst.probes.gated-timed", arguments, probe.Id.Value);
        Guid itemId = Assert.Single(await ItemsAsync(run)).Id;

        using IServiceScope scope = host.Services.CreateScope();
        ScheduledRunItemExecutor executor = scope.ServiceProvider.GetRequiredService<ScheduledRunItemExecutor>();
        using var shutdown = new CancellationTokenSource();

        Task<bool> execution = executor.ExecuteAsync(ScheduledTasksDbContext.Schema, itemId, shutdown.Token);
        await WaitUntilAsync(() => Gate.Entered == 1);
        await shutdown.CancelAsync();

        // The send's own cancellation, unchanged: nothing was attempted after it (a save on the cancelled token would
        // throw a plain OperationCanceledException instead).
        await Assert.ThrowsAsync<ProbeCanceledException>(() => execution);

        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Running, item.Status);
        Assert.Null(item.ErrorCode);
    }

    [Fact]
    public async Task ACancellationNoTokenCaused_IsRetried_NotThrown()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        string arguments = ScheduledCommandFactory.ToArguments(new TouchOneProbeCommand(new(Guid.Empty), new ProbeNote("x")));
        Guid run = await StartRunAsync(user, tree.North, "tst.probes.touch-one", arguments, probe.Id.Value);
        Guid itemId = Assert.Single(await ItemsAsync(run)).Id;

        using IServiceScope scope = host.Services.CreateScope();
        ScheduledRunItemExecutor executor = scope.ServiceProvider.GetRequiredService<ScheduledRunItemExecutor>();

        bool retry = await executor.ExecuteAsync(ScheduledTasksDbContext.Schema, itemId, CancellationToken.None);

        Assert.True(retry);
        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Retrying, item.Status);
        Assert.Equal(typeof(TaskCanceledException).FullName, item.ErrorCode);
    }

    private static string SlowArguments() => ScheduledCommandFactory.ToArguments(new SlowProbeCommand(new(Guid.Empty), "slow"));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime giveUpAt = DateTime.UtcNow + WaitLimit;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < giveUpAt, "The condition did not hold in time.");
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private Task<TestCaller> TouchCallerAsync(Guid scopeId) =>
        tree.CreateGrantedCallerAsync(scopeId, ScheduleProbePermissions.Touch.Permission);

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

    /// <summary>A one-off run of <paramref name="commandName"/> for <paramref name="user"/>, one item per target, enqueued.</summary>
    private async Task<Guid> StartRunAsync(TestCaller user, Guid scopeId, string commandName, string arguments, params Guid[] targets)
    {
        using IServiceScope scope = host.Services.CreateScope();
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(scopeId))!;

        var run = ScheduledTaskRun.Start(
            new ScheduledRunDefinition(null, ScheduledRunTrigger.OneOff, user.Id, scopeId, path, commandName, arguments),
            targets.Length,
            DateTime.UtcNow);
        ScheduledTaskRunItem[] items = [.. targets.Select(target => ScheduledTaskRunItem.Create(run.Id, target, rootId: null, DateTime.UtcNow))];

        IScheduledTaskStore store = scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(ScheduledTasksDbContext.Schema);
        store.AddRun(run, items);
        await store.SaveAsync(CancellationToken.None);

        foreach (ScheduledTaskRunItem item in items)
        {
            await Engine.EnqueueRunItemAsync(new ScheduledRunItemReference(ScheduledTasksDbContext.Schema, item.Id), CancellationToken.None);
        }

        return run.Id;
    }

    /// <summary>One recovery pass by hand: the shared test host leaves the background loop out.</summary>
    private async Task<int> RecoverAsync()
    {
        using ScheduledRunItemRecoveryService recovery = ActivatorUtilities.CreateInstance<ScheduledRunItemRecoveryService>(host.Services);

        return await recovery.RecoverOnceAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyCollection<ScheduledTaskRunItem>> ItemsAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.ScheduledTaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
    }

    private async Task<ScheduledTaskRun> RunAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.ScheduledTaskRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
    }
}
