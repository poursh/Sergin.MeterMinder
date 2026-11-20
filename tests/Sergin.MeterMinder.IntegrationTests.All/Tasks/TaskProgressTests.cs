using System.Collections.Concurrent;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Activity;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Times;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// A run item's progress: <see cref="ITaskProgress"/> does nothing outside a run item (a report or a keep-alive); inside
/// one it clamps the value to 0–100, writes at most once per two seconds (the first report at once), publishes the
/// change, and the last value reported is kept on the finished item whatever the throttle held back. The host's clock stands still unless a test
/// moves it, so the throttle is decided by the clock, never by how fast the test runs.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TaskProgressTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string ProgressName = "tst.probes.progress";

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private readonly TestClock clock = new();

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeTaskEngine Engine => host.Services.GetRequiredService<FakeTaskEngine>();

    private ProgressProbe Probe => host.Services.GetRequiredService<ProgressProbe>();

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder =>
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IDateTimeProvider>(clock));
                services.AddSingleton<FakeTaskEngine>();
                services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(provider => provider.GetRequiredService<FakeTaskEngine>()));
                services.AddSingleton<ProgressProbe>();
                services.AddTransient<IRequestHandler<ProgressProbeCommand, ErrorOr<Success>>, ProgressProbeCommandHandler>();
            }));

        await TaskTestHost.ResetSchemaAsync(host);
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
            await scope.ServiceProvider.GetRequiredService<ITaskProgress>().ReportAsync(50);
        }

        Assert.Empty(heard);
    }

    [Fact]
    public async Task KeepAlive_OutsideARunItem_DoesNothing()
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
            await scope.ServiceProvider.GetRequiredService<ITaskProgress>().KeepAliveAsync();
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
        TaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Succeeded, item.Status);
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
        TaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Succeeded, item.Status);
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

        Assert.Equal(new TaskRunRef(TasksDbContext.Schema, run), change.Ref);
        Assert.Equal(user.Id, change.ActingUser);
        TaskRunItem running = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Running, running.Status);
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
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

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
        string arguments = TaskCommandFactory.ToArguments(new ProgressProbeCommand(new(Guid.Empty)));

        var run = TaskRun.Start(
            new TaskRunDefinition(null, TaskRunTrigger.Once, user.Id, tree.North, path, ProgressName, arguments),
            targets.Length,
            clock.UtcNow);
        TaskRunItem[] items = [.. targets.Select(target => TaskRunItem.Create(run.Id, target, rootId: null, clock.UtcNow))];

        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(TasksDbContext.Schema);
        store.AddRun(run, items);
        await store.SaveAsync(CancellationToken.None);

        foreach (TaskRunItem item in items)
        {
            await Engine.EnqueueRunItemAsync(new TaskRunItemReference(TasksDbContext.Schema, item.Id), CancellationToken.None);
        }

        return run.Id;
    }

    private async Task<IReadOnlyCollection<TaskRunItem>> ItemsAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.TaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
    }

    /// <summary>A clock the test moves; it starts at the real time so stamps sit beside the database's own.</summary>
    private sealed class TestClock : IDateTimeProvider
    {
        private long ticks = DateTime.UtcNow.Ticks;

        public DateTime UtcNow => new(Interlocked.Read(ref ticks), DateTimeKind.Utc);

        public void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);
    }
}
