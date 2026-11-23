using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// A task's timeouts. The inactivity timeout (one second on the probes) is reset by every progress report, a report the
/// throttle holds back and a keep-alive included, so a task that reports keeps running past it, and a silent one fails as
/// <c>Tasks.TimedOut</c>, final, never retried. Once it fired a late report does not revive the task, and a handler that
/// ignores its token is timed out all the same when it returns. <c>MaxDuration</c> stops a task that keeps reporting, as
/// <c>Tasks.MaxDurationExceeded</c>. The executor's heartbeat (every 200 ms here) never resets the inactivity timer. A
/// cancel beats the timeout; a host shutdown is not one.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TaskTimeoutTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private static readonly TimeSpan Every400 = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan Every300 = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeTaskEngine Engine => host.Services.GetRequiredService<FakeTaskEngine>();

    private PacedProbe Probe => host.Services.GetRequiredService<PacedProbe>();

    private ProbeGate Gate => host.Services.GetRequiredService<ProbeGate>();

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder =>
        {
            // A heartbeat far more often than the inactivity timeout: if it reset the timer, a silent task never timed out.
            builder.UseSetting("Sergin:Tasks:HeartbeatInterval", "00:00:00.200");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<FakeTaskEngine>();
                services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(provider => provider.GetRequiredService<FakeTaskEngine>()));
                services.AddSingleton<PacedProbe>();
                services.AddSingleton<ProbeGate>();
                services.AddTransient<IRequestHandler<PacedProbeCommand, ErrorOr<Success>>, PacedProbeCommandHandler>();
                services.AddTransient<IRequestHandler<CappedProbeCommand, ErrorOr<Success>>, CappedProbeCommandHandler>();
                services.AddTransient<IRequestHandler<LongTimedProbeCommand, ErrorOr<Success>>, LongTimedProbeCommandHandler>();
                services.AddTransient<IRequestHandler<GatedTimedProbeCommand, ErrorOr<Success>>, GatedTimedProbeCommandHandler>();
                services.AddTransient<IRequestHandler<WrappingTimedProbeCommand, ErrorOr<Success>>, WrappingTimedProbeCommandHandler>();
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
    public async Task ReportingTask_RunsPastOneInactivityTimeout()
    {
        Probe.Script = PacedProbe.Every(Every400, TimeSpan.FromSeconds(3), (progress, step, ct) => progress.ReportAsync(step, ct));

        TaskRunItem item = await RunToEndAsync(new PacedProbeCommand(new(Guid.Empty)));

        Assert.Equal(TaskRunItemStatus.Succeeded, item.Status);
        Assert.Null(item.ErrorCode);
    }

    [Fact]
    public async Task SilentTask_TimesOut()
    {
        Probe.Script = (_, ct) => Task.Delay(TimeSpan.FromSeconds(3), ct);

        TaskRunRef run = await StartAsync(await CallerAsync(), new PacedProbeCommand(new(Guid.Empty)));
        await Engine.DrainAsync(host.Services);

        (TaskRun stored, TaskRunItem item) = await ReadAsync(run);
        Assert.Equal(TaskRunItemStatus.Failed, item.Status);
        Assert.Equal(TaskErrors.TimedOutCode, item.ErrorCode);
        Assert.Contains("without progress", item.ErrorDescription, StringComparison.Ordinal);
        Assert.Contains("00:00:01", item.ErrorDescription, StringComparison.Ordinal);
        Assert.Equal(1, item.Attempts);
        Assert.Equal(TaskRunStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task KeepAlive_ResetsTheTimer()
    {
        Probe.Script = PacedProbe.Every(Every400, TimeSpan.FromSeconds(3), (progress, _, ct) => progress.KeepAliveAsync(ct));

        TaskRunItem item = await RunToEndAsync(new PacedProbeCommand(new(Guid.Empty)));

        Assert.Equal(TaskRunItemStatus.Succeeded, item.Status);
        Assert.Null(item.Progress);
    }

    [Fact]
    public async Task ThrottledReports_StillResetTheTimer()
    {
        // Every 300 ms is well under TaskProgress.WriteInterval: most reports are held back, and each still counts.
        Assert.True(Every300 < TaskProgress.WriteInterval);
        Probe.Script = PacedProbe.Every(Every300, TimeSpan.FromSeconds(3), (progress, step, ct) => progress.ReportAsync(step, ct));

        TaskRunItem item = await RunToEndAsync(new PacedProbeCommand(new(Guid.Empty)));

        Assert.Equal(TaskRunItemStatus.Succeeded, item.Status);
    }

    [Fact]
    public async Task MaxDuration_StopsATaskThatKeepsReporting()
    {
        Probe.Script = PacedProbe.Every(Every300, TimeSpan.FromSeconds(5), (progress, step, ct) => progress.ReportAsync(step, ct));

        TaskRunItem item = await RunToEndAsync(new CappedProbeCommand(new(Guid.Empty)));

        Assert.Equal(TaskRunItemStatus.Failed, item.Status);
        Assert.Equal(TaskErrors.MaxDurationExceededCode, item.ErrorCode);
        Assert.Contains("00:00:02", item.ErrorDescription, StringComparison.Ordinal);
        Assert.Equal(1, item.Attempts);
    }

    [Fact]
    public async Task ReportAfterExpiry_DoesNotRevive()
    {
        bool reportedAfterExpiry = false;
        Probe.Script = async (progress, ct) =>
        {
            // Past the timeout without looking at the token, then a report and a keep-alive (on a token that is not the
            // send's, which is cancelled by now and would make them throw) and a normal return.
            await Task.Delay(TimeSpan.FromSeconds(1.5), CancellationToken.None);
            bool expired = ct.IsCancellationRequested;
            await progress.ReportAsync(50, CancellationToken.None);
            await progress.KeepAliveAsync(CancellationToken.None);
            reportedAfterExpiry = expired;
        };

        TaskRunItem item = await RunToEndAsync(new PacedProbeCommand(new(Guid.Empty)));

        // The handler reached its normal return, the reports made after the timeout had fired.
        Assert.True(reportedAfterExpiry);

        Assert.Equal(TaskRunItemStatus.Failed, item.Status);
        Assert.Equal(TaskErrors.TimedOutCode, item.ErrorCode);
    }

    [Fact]
    public async Task TaskIgnoringToken_IsStillTimedOut()
    {
        Probe.Script = (_, _) => Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);

        TaskRunItem item = await RunToEndAsync(new PacedProbeCommand(new(Guid.Empty)));

        Assert.Equal(TaskRunItemStatus.Failed, item.Status);
        Assert.Equal(TaskErrors.TimedOutCode, item.ErrorCode);
    }

    [Fact]
    public async Task Timeout_WrappedInAnotherException_IsStillATimeout()
    {
        TaskRunItem item = await RunToEndAsync(new WrappingTimedProbeCommand(new(Guid.Empty)));

        Assert.Equal(TaskRunItemStatus.Failed, item.Status);
        Assert.Equal(TaskErrors.TimedOutCode, item.ErrorCode);
        Assert.Equal(1, item.Attempts);
    }

    [Fact]
    public async Task Cancel_BeatsInactivityTimeout()
    {
        TestCaller caller = await CallerAsync();
        // A three-second inactivity timeout around a silent thirty-second send, cancelled at about 300 ms: ample slack.
        TaskRunRef run = await StartAsync(caller, new LongTimedProbeCommand(new(Guid.Empty)));
        DateTime startedAt = DateTime.UtcNow;

        Task drain = Engine.DrainAsync(host.Services);
        await WaitUntilAsync(async () => (await ReadAsync(run)).Item.Status == TaskRunItemStatus.Running);
        TimeSpan untilCancel = TimeSpan.FromMilliseconds(300) - (DateTime.UtcNow - startedAt);

        if (untilCancel > TimeSpan.Zero)
        {
            await Task.Delay(untilCancel);
        }

        ErrorOr<Success> cancelled = await CancelAsync(caller, run);
        Assert.False(cancelled.IsError, cancelled.IsError ? cancelled.FirstError.Code : string.Empty);
        await drain.WaitAsync(WaitLimit);

        (TaskRun stored, TaskRunItem item) = await ReadAsync(run);
        Assert.Equal(TaskRunItemStatus.Cancelled, item.Status);
        Assert.NotEqual(TaskErrors.TimedOutCode, item.ErrorCode);
        Assert.Equal(1, item.Attempts);
        Assert.Equal((TaskRunStatus.Completed, 1, 0), (stored.Status, stored.Cancelled, stored.Failed));
    }

    [Fact]
    public async Task HostShutdown_IsNotATimeout()
    {
        // A task with a timeout (far longer than the test), so a shutdown could be mistaken for one.
        TaskRunRef run = await StartAsync(await CallerAsync(), new GatedTimedProbeCommand(new(Guid.Empty)));
        Guid itemId = (await ReadAsync(run)).Item.Id;

        using IServiceScope scope = host.Services.CreateScope();
        TaskRunItemExecutor executor = scope.ServiceProvider.GetRequiredService<TaskRunItemExecutor>();
        using var shutdown = new CancellationTokenSource();

        Task<bool> execution = executor.ExecuteAsync(TasksDbContext.Schema, itemId, shutdown.Token);
        await WaitUntilAsync(() => Task.FromResult(Gate.Entered == 1));
        await shutdown.CancelAsync();

        // The send's own cancellation, unchanged: nothing was attempted after it (a save on the cancelled token would
        // throw a plain OperationCanceledException instead).
        await Assert.ThrowsAsync<ProbeCanceledException>(() => execution);

        TaskRunItem item = (await ReadAsync(run)).Item;
        Assert.Equal(TaskRunItemStatus.Running, item.Status);
        Assert.Null(item.ErrorCode);
    }

    private async Task<TaskRunItem> RunToEndAsync(ITaskCommand command)
    {
        TaskRunRef run = await StartAsync(await CallerAsync(), command);
        await Engine.DrainAsync(host.Services);

        return (await ReadAsync(run)).Item;
    }

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

    private async Task<TaskRunRef> StartAsync(TestCaller caller, ITaskCommand command)
    {
        var probe = ScheduleProbe.Create();

        using (IServiceScope scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.Feeder1);
            TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            context.Probes.Add(probe);
            await context.SaveChangesAsync();
        }

        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        ErrorOr<TaskRunStartedResponse> started = await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .StartTaskAsync(command, [new TaskTarget(probe.Id.Value)]);
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        return started.Value.Run;
    }

    private async Task<ErrorOr<Success>> CancelAsync(IUserContext caller, TaskRunRef run)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendAsync(new CancelTaskRunCommand(run.Schema, run.RunId));
    }

    private async Task<(TaskRun Run, TaskRunItem Item)> ReadAsync(TaskRunRef run)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return (
            await context.TaskRuns.AsNoTracking().SingleAsync(one => one.Id == run.RunId),
            await context.TaskRunItems.AsNoTracking().SingleAsync(item => item.RunId == run.RunId));
    }
}
