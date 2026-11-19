using System.Collections.Concurrent;
using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Activity;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The run notifier: a user's subscribers hear their own runs start, move and finish, nobody else's, never after they
/// unsubscribe, and a subscriber that throws breaks neither the run nor the other subscribers.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RunNotifierTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeScheduledTaskEngine Engine => host.Services.GetRequiredService<FakeScheduledTaskEngine>();

    private IRunNotifier Notifier => host.Services.GetRequiredService<IRunNotifier>();

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<FakeScheduledTaskEngine>();
                services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(provider => provider.GetRequiredService<FakeScheduledTaskEngine>()));
                services.AddSingleton<ProbeGate>();
                services.AddTransient<IRequestHandler<SlowProbeCommand, ErrorOr<TouchProbeResponse>>, ReachingSlowProbeCommandHandler>();
                services.AddScoped<IValidator<SlowProbeCommand>, SlowProbeCommandValidator>();
                services.AddScoped<IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
            }));

        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync()
    {
        host.Services.GetRequiredService<ProbeGate>().Release();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Subscriber_HearsStartAndFinish()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        var heard = new Recorder();
        using IDisposable subscription = Notifier.Subscribe(caller.Id, change => heard.Track(RecordWithStatusAsync(heard, change)));

        BackgroundRunRef run = await StartAndDrainAsync(caller);

        RunChanged[] mine = [.. heard.Changes.Where(change => change.Ref == run)];
        Assert.True(mine.Length >= 2, $"heard {mine.Length}");
        Assert.All(mine, change => Assert.Equal(caller.Id, change.ActingUser));
        Assert.Equal("Completed", (await BackgroundRuns.WaitAsync(host.Services, run, TimeSpan.FromSeconds(10))).Status);

        // Every handler's status read is fire-and-forget: wait for them all, so the last published one has its status.
        await heard.SettledAsync();

        // The last event published was published after the run was Completed.
        Assert.Equal("Completed", heard.StatusAtLastPublished);
    }

    [Fact]
    public async Task OtherUser_HearsNothing()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        var other = new Recorder();
        using IDisposable subscription = Notifier.Subscribe(new UserId(Guid.CreateVersion7()), other.HandleAsync);

        await StartAndDrainAsync(caller);

        Assert.Empty(other.Changes);
    }

    [Fact]
    public async Task DisposedSubscription_HearsNothing_AndPublishDoesNotThrow()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        var heard = new Recorder();
        IDisposable subscription = Notifier.Subscribe(caller.Id, heard.HandleAsync);
        subscription.Dispose();
        subscription.Dispose();

        Notifier.Publish(new RunChanged(new BackgroundRunRef("x", Guid.NewGuid()), caller.Id));
        await StartAndDrainAsync(caller);

        Assert.Empty(heard.Changes);
    }

    [Fact]
    public async Task ThrowingHandler_DoesNotBreakTheRunOrOtherHandlers()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        var heard = new Recorder();
        using IDisposable sync = Notifier.Subscribe(caller.Id, _ => throw new InvalidOperationException("sync"));
        using IDisposable faulted = Notifier.Subscribe(caller.Id, _ => Task.FromException(new InvalidOperationException("faulted")));
        using IDisposable good = Notifier.Subscribe(caller.Id, heard.HandleAsync);

        BackgroundRunRef run = await StartAndDrainAsync(caller);

        Assert.Equal("Completed", (await BackgroundRuns.WaitAsync(host.Services, run, TimeSpan.FromSeconds(10))).Status);
        Assert.True(heard.Changes.Count(change => change.Ref == run) >= 2);
    }

    private async Task RecordWithStatusAsync(Recorder heard, RunChanged change)
    {
        int sequence = heard.Next(change);
        (string status, _) = await BackgroundRuns.WaitAsync(host.Services, change.Ref, TimeSpan.FromMilliseconds(1)).ContinueWith(
            task => task.IsCompletedSuccessfully ? (task.Result.Status, 0) : ("Running", 0),
            TaskScheduler.Default);
        heard.SetStatus(sequence, status);
    }

    private async Task<BackgroundRunRef> StartAndDrainAsync(TestCaller caller)
    {
        host.Services.GetRequiredService<ProbeGate>().Release();

        ScheduleProbe probe;
        using (IServiceScope scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.Feeder1);
            ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();
            probe = ScheduleProbe.Create();
            context.Probes.Add(probe);
            await context.SaveChangesAsync();
        }

        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        ErrorOr<BackgroundRunStartedResponse> started = await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .StartAsync(new SlowProbeCommand(new(Guid.Empty), "slow"), [new BackgroundTarget(probe.Id.Value)]);
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        await Engine.DrainAsync(host.Services);

        return started.Value.Run;
    }

    private sealed class Recorder
    {
        private readonly ConcurrentQueue<RunChanged> changes = new();

        public IReadOnlyCollection<RunChanged> Changes => [.. changes];

        private readonly ConcurrentDictionary<int, string> statuses = new();
        private readonly ConcurrentQueue<Task> pending = new();
        private int sequence;

        public string? StatusAtLastPublished => statuses.IsEmpty ? null : statuses[statuses.Keys.Max()];

        public int Next(RunChanged change)
        {
            changes.Enqueue(change);
            return Interlocked.Increment(ref sequence);
        }

        public void SetStatus(int at, string status) => statuses[at] = status;

        public Task Track(Task handling)
        {
            pending.Enqueue(handling);
            return handling;
        }

        public Task SettledAsync() => Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10));

        public Task HandleAsync(RunChanged change)
        {
            changes.Enqueue(change);
            return Task.CompletedTask;
        }
    }
}
