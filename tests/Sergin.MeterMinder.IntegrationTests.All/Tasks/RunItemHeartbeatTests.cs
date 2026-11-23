using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Hosts.Tasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// A run item that takes longer than the stale period keeps its activity fresh while it runs (the heartbeat), so the
/// recovery never takes it for lost and sends it again; a cancellation no token caused is retried. The timeouts, and a host
/// shutdown that is not one, are <see cref="TaskTimeoutTests"/>'.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RunItemHeartbeatTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string SlowName = "tst.probes.slow";

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeTaskEngine Engine => host.Services.GetRequiredService<FakeTaskEngine>();

    private ProbeGate Gate => host.Services.GetRequiredService<ProbeGate>();

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder =>
        {
            builder.UseSetting("Sergin:Tasks:StaleAfter", "00:00:03");
            builder.UseSetting("Sergin:Tasks:RecoveryInterval", "00:00:01");
            builder.UseSetting("Sergin:Tasks:RetryIntervalsSeconds:0", "1");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<FakeTaskEngine>();
                services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(provider => provider.GetRequiredService<FakeTaskEngine>()));
                services.AddSingleton<ProbeGate>();
                services.AddTransient<IRequestHandler<SlowProbeCommand, ErrorOr<Success>>, SlowProbeCommandHandler>();
                services.AddScoped<IValidator<SlowProbeCommand>, SlowProbeCommandValidator>();
                services.AddTransient<IRequestHandler<TouchOneProbeCommand, ErrorOr<Success>>, SelfCancelingProbeCommandHandler>();
            });
        });

        await TaskTestHost.ResetSchemaAsync(host);
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

        TaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Succeeded, item.Status);
        Assert.Equal(1, item.Attempts);
        Assert.Equal(1, Gate.Entered);
    }

    [Fact]
    public async Task Touch_OnAFinishedItem_ChangesNothing()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        Guid run = await StartRunAsync(user, tree.North, SlowName, SlowArguments(), probe.Id.Value);
        Gate.Release();
        await Engine.DrainAsync(host.Services);

        TaskRunItem finished = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Succeeded, finished.Status);

        RunItemHeartbeat touched;

        using (IServiceScope scope = host.Services.CreateScope())
        {
            ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(TasksDbContext.Schema);
            touched = await store.TouchRunItemAsync(finished.Id, finished.LastActivityAtUtc.AddHours(1), CancellationToken.None);
        }

        Assert.Equal(new RunItemHeartbeat(Touched: false, CancelRequested: false), touched);
        Assert.Equal(finished.LastActivityAtUtc, Assert.Single(await ItemsAsync(run)).LastActivityAtUtc);
    }

    [Fact]
    public async Task ACancellationNoTokenCaused_IsRetried_NotThrown()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        string arguments = TaskCommandFactory.ToArguments(new TouchOneProbeCommand(new(Guid.Empty), new ProbeNote("x")));
        Guid run = await StartRunAsync(user, tree.North, "tst.probes.touch-one", arguments, probe.Id.Value);
        Guid itemId = Assert.Single(await ItemsAsync(run)).Id;

        using IServiceScope scope = host.Services.CreateScope();
        TaskRunItemExecutor executor = scope.ServiceProvider.GetRequiredService<TaskRunItemExecutor>();

        bool retry = await executor.ExecuteAsync(TasksDbContext.Schema, itemId, CancellationToken.None);

        Assert.True(retry);
        TaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Retrying, item.Status);
        Assert.Equal(typeof(TaskCanceledException).FullName, item.ErrorCode);
    }

    private static string SlowArguments() => TaskCommandFactory.ToArguments(new SlowProbeCommand(new(Guid.Empty), "slow"));

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
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

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

        var run = TaskRun.Start(
            new TaskRunDefinition(null, TaskRunTrigger.Once, user.Id, scopeId, path, commandName, arguments),
            targets.Length,
            DateTime.UtcNow);
        TaskRunItem[] items = [.. targets.Select(target => TaskRunItem.Create(run.Id, target, DateTime.UtcNow))];

        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(TasksDbContext.Schema);
        store.AddRun(run, items);
        await store.SaveAsync(CancellationToken.None);

        foreach (TaskRunItem item in items)
        {
            await Engine.EnqueueRunItemAsync(new TaskRunItemReference(TasksDbContext.Schema, item.Id), CancellationToken.None);
        }

        return run.Id;
    }

    /// <summary>One recovery pass by hand: the shared test host leaves the background loop out.</summary>
    private async Task<int> RecoverAsync()
    {
        using TaskRunItemRecoveryService recovery = ActivatorUtilities.CreateInstance<TaskRunItemRecoveryService>(host.Services);

        return await recovery.RecoverOnceAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyCollection<TaskRunItem>> ItemsAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.TaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
    }

    private async Task<TaskRun> RunAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.TaskRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
    }
}
