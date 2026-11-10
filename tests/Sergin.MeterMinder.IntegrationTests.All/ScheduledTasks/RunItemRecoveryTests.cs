using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Times;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Hosts.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The one recovery for run items: an unfinished item quiet for longer than StaleAfter is handed to the engine again,
/// whatever lost its job, and the executor's claim in the database makes every extra delivery harmless. The clock is the
/// test's, so "quiet for fifteen minutes" is a step of the clock, not a wait.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RunItemRecoveryTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Schema = ScheduledTasksDbContext.Schema;
    private const string TouchName = "tst.probes.touch";

    private static readonly TimeSpan PastStale = new ScheduledTaskOptions().StaleAfter + TimeSpan.FromSeconds(1);

    private readonly TestClock clock = new();
    private readonly FakeScheduledTaskEngine engine = new();

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private ProbeFailureSwitch Failures => host.Services.GetRequiredService<ProbeFailureSwitch>();

    private ProbeSendRecorder Recorder => host.Services.GetRequiredService<ProbeSendRecorder>();

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(engine));
            services.Replace(ServiceDescriptor.Singleton<IDateTimeProvider>(clock));
            services.AddSingleton<ProbeFailureSwitch>();
            services.AddSingleton<ProbeSendRecorder>();
            services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, TouchProbeCommandHandler>();
            services.AddScoped<IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
            services.AddTransient<IPipelineBehavior<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, RecordingProbeBehavior>();
        }));

        await ScheduledTaskTestHost.ResetSchemaAsync(host);
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
        Assert.Equal(ScheduledRunItemStatus.Retrying, (await ItemAsync(item)).Status);

        Assert.Equal(0, await RecoverAsync());
        clock.Advance(PastStale);
        Assert.Equal(1, await RecoverAsync());
        Assert.Equal(new ScheduledRunItemReference(Schema, item), Assert.Single(engine.QueuedItems));

        await engine.DrainAsync(host.Services);

        ScheduledTaskRunItem done = await ItemAsync(item);
        Assert.Equal(ScheduledRunItemStatus.Succeeded, done.Status);
        Assert.Equal(2, done.Attempts);
        Assert.Equal(ScheduledRunStatus.Completed, (await RunAsync(run)).Status);
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
        Assert.Equal(ScheduledRunItemStatus.Running, (await ItemAsync(item)).Status);

        clock.Advance(PastStale);
        Assert.Equal(1, await RecoverAsync());
        await engine.DrainAsync(host.Services);

        ScheduledTaskRunItem done = await ItemAsync(item);
        Assert.Equal(ScheduledRunItemStatus.Succeeded, done.Status);
        Assert.Equal(2, done.Attempts);
        Assert.Single(Recorder.Sends);
        Assert.Equal(ScheduledRunStatus.Completed, (await RunAsync(run)).Status);
    }

    [Fact]
    public async Task TwoConcurrentDeliveries_SendTheCommandOnce()
    {
        (Guid run, Guid item) = await StartRunAsync();

        bool[] retries = await Task.WhenAll(ExecuteAsync(item), ExecuteAsync(item), ExecuteAsync(item));

        Assert.All(retries, Assert.False);
        Assert.Single(Recorder.Sends);
        ScheduledTaskRunItem done = await ItemAsync(item);
        Assert.Equal(ScheduledRunItemStatus.Succeeded, done.Status);
        Assert.Equal(1, done.Attempts);
        Assert.Equal(ScheduledRunStatus.Completed, (await RunAsync(run)).Status);
    }

    [Fact]
    public async Task ItemWhoseEveryAttemptIsLost_EndsExhausted_AndTheRunCompletes()
    {
        (Guid run, Guid item) = await StartRunAsync();
        int allowed = new ScheduledTaskOptions().MaxAttempts;

        for (int attempt = 0; attempt < allowed; attempt++)
        {
            Assert.True(await ClaimAsync(item));
            clock.Advance(PastStale);
        }

        Assert.Equal(1, await RecoverAsync());
        await engine.DrainAsync(host.Services);

        ScheduledTaskRunItem done = await ItemAsync(item);
        Assert.Equal(ScheduledRunItemStatus.Failed, done.Status);
        Assert.Equal(ScheduledTaskErrors.ExhaustedCode, done.ErrorCode);
        Assert.Empty(Recorder.Sends);
        ScheduledTaskRun completed = await RunAsync(run);
        Assert.Equal(ScheduledRunStatus.Completed, completed.Status);
        Assert.Equal(1, completed.Failed);
    }

    [Theory]
    [InlineData("00:00:00", "00:01:00", "StaleAfter must be a positive")]
    [InlineData("00:15:00", "00:00:00", "RecoveryInterval must be a positive")]
    [InlineData("00:10:30", "00:01:00", "StaleAfter must exceed the longest")]
    public void Options_RefuseARecoveryThatWouldDuplicateHealthyItems(string staleAfter, string recoveryInterval, string expected)
    {
        var options = new ScheduledTaskOptions
        {
            RetryIntervalsSeconds = [30, 600],
            StaleAfter = TimeSpan.Parse(staleAfter, System.Globalization.CultureInfo.InvariantCulture),
            RecoveryInterval = TimeSpan.Parse(recoveryInterval, System.Globalization.CultureInfo.InvariantCulture),
        };

        Assert.False(options.Validate(out string failure));
        Assert.Contains(expected, failure, StringComparison.Ordinal);
        Assert.StartsWith("Sergin:ScheduledTasks:", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_AcceptTheDefaults_AndAStalePeriodJustLongEnough()
    {
        var defaults = new ScheduledTaskOptions { RetryIntervalsSeconds = ScheduledTaskOptions.DefaultRetryIntervalsSeconds };
        var tight = new ScheduledTaskOptions
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
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();
        var probe = ScheduleProbe.Create();
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(tree.North))!;
        string arguments = ScheduledCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("recovered")));
        var run = ScheduledTaskRun.Start(
            new ScheduledRunDefinition(null, ScheduledRunTrigger.OneOff, user.Id, tree.North, path, TouchName, arguments), 1, clock.UtcNow);
        var item = ScheduledTaskRunItem.Create(run.Id, probe.Id.Value, null, clock.UtcNow);

        IScheduledTaskStore store = scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(Schema);
        store.AddRun(run, [item]);
        await store.SaveAsync(CancellationToken.None);

        return (run.Id, item.Id);
    }

    private async Task<bool> ExecuteAsync(Guid item)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IScheduledTaskRunner>().ExecuteRunItemAsync(Schema, item, CancellationToken.None);
    }

    private async Task<bool> ClaimAsync(Guid item)
    {
        using IServiceScope scope = host.Services.CreateScope();
        IScheduledTaskStore store = scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(Schema);

        return await store.TryClaimRunItemAsync(item, clock.UtcNow, clock.UtcNow - new ScheduledTaskOptions().StaleAfter, CancellationToken.None);
    }

    /// <summary>One recovery pass by hand: the shared test host leaves the background loop out.</summary>
    private async Task<int> RecoverAsync()
    {
        using ScheduledRunItemRecoveryService recovery = ActivatorUtilities.CreateInstance<ScheduledRunItemRecoveryService>(host.Services);

        return await recovery.RecoverOnceAsync(CancellationToken.None);
    }

    private async Task<ScheduledTaskRunItem> ItemAsync(Guid id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>().ScheduledTaskRunItems.AsNoTracking().SingleAsync(item => item.Id == id);
    }

    private async Task<ScheduledTaskRun> RunAsync(Guid id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>().ScheduledTaskRuns.AsNoTracking().SingleAsync(run => run.Id == id);
    }

    /// <summary>A clock the test moves; it starts at the real time so stamps sit beside the database's own.</summary>
    private sealed class TestClock : IDateTimeProvider
    {
        private long ticks = DateTime.UtcNow.Ticks;

        public DateTime UtcNow => new(Interlocked.Read(ref ticks), DateTimeKind.Utc);

        public void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);
    }
}
