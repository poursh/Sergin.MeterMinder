using System.Collections.Concurrent;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Activity;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Times;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// A run item's progress: <see cref="IRunProgress"/> does nothing outside a run item; inside one it clamps the value to
/// 0–100, writes at most once per two seconds (the first report at once), publishes the change, and the last value
/// reported is kept on the finished item whatever the throttle held back. The host's clock stands still unless a test
/// moves it, so the throttle is decided by the clock, never by how fast the test runs.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RunProgressTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string ProgressName = "tst.probes.progress";

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private readonly TestClock clock = new();

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeScheduledTaskEngine Engine => host.Services.GetRequiredService<FakeScheduledTaskEngine>();

    private ProgressProbe Probe => host.Services.GetRequiredService<ProgressProbe>();

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder =>
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IDateTimeProvider>(clock));
                services.AddSingleton<FakeScheduledTaskEngine>();
                services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(provider => provider.GetRequiredService<FakeScheduledTaskEngine>()));
                services.AddSingleton<ProgressProbe>();
                services.AddTransient<IRequestHandler<ProgressProbeCommand, ErrorOr<TouchProbeResponse>>, ProgressProbeCommandHandler>();
            }));

        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync()
    {
        Probe.OpenGates();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Report_OutsideARunItem_DoesNothing()
    {
        TestCaller user = await TouchCallerAsync();
        var heard = new ConcurrentQueue<RunChanged>();
        using IDisposable subscription = host.Services.GetRequiredService<IRunNotifier>().Subscribe(user.Id, change =>
        {
            heard.Enqueue(change);
            return Task.CompletedTask;
        });

        using (IServiceScope scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRunProgress>().ReportAsync(50);
        }

        Assert.Empty(heard);
    }

    [Fact]
    public async Task Report_IsClampedAndStored()
    {
        Probe.Reports = [150];
        Probe.OpenGates();
        Guid run = await StartRunAsync(await TouchCallerAsync(), (await CreateProbeAsync()).Id.Value);

        await Engine.DrainAsync(host.Services);

        Assert.Equal(100, Probe.StoredAfterReports);
        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status);
        Assert.Equal(100, item.Progress);
    }

    [Fact]
    public async Task Reports_AreThrottled_AndTheLastIsKept()
    {
        Probe.Reports = [10, 20, 30];
        Probe.OpenGates();
        Guid run = await StartRunAsync(await TouchCallerAsync(), (await CreateProbeAsync()).Id.Value);

        await Engine.DrainAsync(host.Services);

        // The first report was written at once; the clock did not move, so the other two waited for the outcome's save.
        Assert.Equal(10, Probe.StoredAfterReports);
        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status);
        Assert.Equal(30, item.Progress);
    }

    [Fact]
    public async Task Report_AfterTheInterval_IsWrittenAgain()
    {
        Probe.Reports = [10, 40];
        Probe.AfterEachReport = () => clock.Advance(TimeSpan.FromSeconds(2));
        Probe.OpenGates();
        Guid run = await StartRunAsync(await TouchCallerAsync(), (await CreateProbeAsync()).Id.Value);

        await Engine.DrainAsync(host.Services);

        // Two seconds passed between the reports, so the second was written while the command still ran.
        Assert.Equal(40, Probe.StoredAfterReports);
        Assert.Equal(40, Assert.Single(await ItemsAsync(run)).Progress);
    }

    [Fact]
    public async Task Report_PublishesRunChanged()
    {
        TestCaller user = await TouchCallerAsync();
        Probe.Reports = [50];
        Guid run = await StartRunAsync(user, (await CreateProbeAsync()).Id.Value);

        Task drain = Engine.DrainAsync(host.Services);
        await Probe.Started.WaitAsync(WaitLimit);

        // Subscribed once the item is Running, so the claim's own change is not the one heard.
        var heard = new TaskCompletionSource<RunChanged>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = host.Services.GetRequiredService<IRunNotifier>().Subscribe(user.Id, change =>
        {
            heard.TrySetResult(change);
            return Task.CompletedTask;
        });

        Probe.Start.Release();
        RunChanged change = await heard.Task.WaitAsync(WaitLimit);

        Assert.Equal(new BackgroundRunRef(ScheduledTasksDbContext.Schema, run), change.Ref);
        Assert.Equal(user.Id, change.ActingUser);
        ScheduledTaskRunItem running = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Running, running.Status);
        Assert.Equal(50, running.Progress);

        Probe.Finish.Release();
        await drain;
    }

    private Task<TestCaller> TouchCallerAsync() =>
        tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);

    private async Task<ScheduleProbe> CreateProbeAsync()
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.Feeder1);
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        var probe = ScheduleProbe.Create();
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        return probe;
    }

    /// <summary>A one-off run of the progress probe for <paramref name="user"/>, one item per target, enqueued.</summary>
    private async Task<Guid> StartRunAsync(TestCaller user, params Guid[] targets)
    {
        using IServiceScope scope = host.Services.CreateScope();
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(tree.North))!;
        string arguments = ScheduledCommandFactory.ToArguments(new ProgressProbeCommand(new(Guid.Empty)));

        var run = ScheduledTaskRun.Start(
            new ScheduledRunDefinition(null, ScheduledRunTrigger.OneOff, user.Id, tree.North, path, ProgressName, arguments),
            targets.Length,
            clock.UtcNow);
        ScheduledTaskRunItem[] items = [.. targets.Select(target => ScheduledTaskRunItem.Create(run.Id, target, rootId: null, clock.UtcNow))];

        IScheduledTaskStore store = scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(ScheduledTasksDbContext.Schema);
        store.AddRun(run, items);
        await store.SaveAsync(CancellationToken.None);

        foreach (ScheduledTaskRunItem item in items)
        {
            await Engine.EnqueueRunItemAsync(new ScheduledRunItemReference(ScheduledTasksDbContext.Schema, item.Id), CancellationToken.None);
        }

        return run.Id;
    }

    private async Task<IReadOnlyCollection<ScheduledTaskRunItem>> ItemsAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.ScheduledTaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
    }

    /// <summary>A clock the test moves; it starts at the real time so stamps sit beside the database's own.</summary>
    private sealed class TestClock : IDateTimeProvider
    {
        private long ticks = DateTime.UtcNow.Ticks;

        public DateTime UtcNow => new(Interlocked.Read(ref ticks), DateTimeKind.Utc);

        public void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);
    }
}
