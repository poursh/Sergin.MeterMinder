using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Hosts.ScheduledTasks;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The engine a host gets with no TickerQ licence: a blank licence path registers the in-process stand-in, which runs
/// run-now and one-off items in a background worker (retries included) and never fires a saved schedule. A non-blank path
/// that names no file still refuses start, so a typo never falls back silently.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class InProcessEngineTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Schema = ScheduledTasksDbContext.Schema;
    private const string TouchName = "tst.probes.touch";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;
    private TestCaller manager = default!;

    private ProbeFailureSwitch Failures => host.Services.GetRequiredService<ProbeFailureSwitch>();

    private ProbeSendRecorder Recorder => host.Services.GetRequiredService<ProbeSendRecorder>();

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(
            factory,
            builder =>
            {
                builder.UseSetting("Sergin:ScheduledTasks:RetryIntervalsSeconds:0", "1");
                builder.UseSetting("Sergin:ScheduledTasks:MaxAttempts", "2");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<ProbeFailureSwitch>();
                    services.AddSingleton<ProbeSendRecorder>();
                    services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, TouchProbeCommandHandler>();
                    services.AddScoped<IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
                    services.AddTransient<IPipelineBehavior<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, RecordingProbeBehavior>();
                });
            },
            standInEngine: true);

        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
        manager = await tree.CreateGrantedCallerAsync(
            tree.North, ScheduledTaskPermissions.Manage.Permission, ScheduleProbePermissions.Touch.Permission);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public void BlankLicensePath_RegistersTheStandIn()
    {
        IScheduledTaskEngine engine = host.Services.GetRequiredService<IScheduledTaskEngine>();

        Assert.IsType<InProcessScheduledTaskEngine>(engine);
        Assert.False(engine.FiresTriggers);
        Assert.Null(host.Services.GetService<TickerQScheduledTaskEngine>());
    }

    [Fact]
    public void MissingLicenseFile_StillRefusesStart()
    {
        using WebApplicationFactory<Program> licensed = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(TickerQTestStore.LicenseKey, @"C:\nope\x.tqlicense");
            builder.ConfigureServices((context, services) =>
            {
                IConfigurationSection sergin = context.Configuration.GetSection("Sergin");
                services.AddSerginTickerQ(sergin, sergin.GetSection("ConnectionStrings")["Database"]!);
            });
        });

        OptionsValidationException failure = Assert.Throws<OptionsValidationException>(() => licensed.CreateClient());

        Assert.Contains("LicensePath", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneOffRun_ThroughTheStandIn_Succeeds()
    {
        ScheduleProbe probe = await CreateProbeAsync();

        ErrorOr<ScheduledRunStartedResponse> started = await SendAsync(
            new RunCommandOnceCommand(Schema, Touch("in process"), [new(probe.Id.Value, null)]));
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        ScheduledTaskRun run = await EventuallyAsync(async context => await context.ScheduledTaskRuns.AsNoTracking()
            .FirstOrDefaultAsync(one => one.Id == started.Value.RunId && one.Status == ScheduledRunStatus.Completed));

        Assert.Equal(1, run.Succeeded);
        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run.Id));
        Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status);
        Assert.Equal("in process", (await ProbeAsync(probe.Id)).LastNote);

        (IUserContext caller, Guid _) = Assert.Single(Recorder.Sends);
        Assert.Equal(manager.Id, Assert.IsAssignableFrom<IDelegatedUserContext>(caller).Id);
    }

    [Fact]
    public async Task Retry_ThroughTheStandIn_EndsExhausted()
    {
        ScheduleProbe probe = await CreateProbeAsync();
        Failures.ThrowTimes = int.MaxValue;

        ErrorOr<ScheduledRunStartedResponse> started = await SendAsync(
            new RunCommandOnceCommand(Schema, Touch("never"), [new(probe.Id.Value, null)]));
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        ScheduledTaskRunItem item = await EventuallyAsync(async context => await context.ScheduledTaskRunItems.AsNoTracking()
            .FirstOrDefaultAsync(one => one.RunId == started.Value.RunId && one.Status == ScheduledRunItemStatus.Failed));

        Assert.Equal(ScheduledTaskErrors.ExhaustedCode, item.ErrorCode);
        Assert.Equal(2, item.Attempts);
    }

    [Fact]
    public async Task SavedCron_IsStoredAndNeverFires()
    {
        ScheduleProbe probe = await CreateProbeAsync();

        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(
            new CreateScheduledTaskCommand(Schema, Touch("every second"), [new(probe.Id.Value, null)], "* * * * * *", "UTC"));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Code : string.Empty);
        var id = new ScheduledTaskId(created.Value.Id);

        await Task.Delay(TimeSpan.FromSeconds(3));

        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();
        ScheduledTask task = await context.ScheduledTasks.AsNoTracking().IgnoreQueryFilters().SingleAsync(one => one.Id == id);
        Assert.Equal(ScheduledTaskState.Active, task.State);
        Assert.False(await context.ScheduledTaskRuns.AnyAsync(run => run.TaskId == id));
        Assert.Empty(Recorder.Sends);
    }

    [Fact]
    public async Task TriggerRebuild_WithTheStandIn_LeavesSavedTasksAlone()
    {
        ScheduleProbe probe = await CreateProbeAsync();
        ErrorOr<CreateScheduledTaskCommandResponse> created = await SendAsync(
            new CreateScheduledTaskCommand(Schema, Touch("kept"), [new(probe.Id.Value, null)], "0 3 * * *", "UTC"));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Code : string.Empty);
        var id = new ScheduledTaskId(created.Value.Id);
        (Guid? triggerBefore, object versionBefore) = await TriggerAndVersionAsync(id);

        // One whole rebuild pass, as a host start runs it (the shared test host leaves the service out).
        using (ScheduledTaskTriggerRebuildService rebuild = ActivatorUtilities.CreateInstance<ScheduledTaskTriggerRebuildService>(host.Services))
        {
            await rebuild.StartAsync(CancellationToken.None);
            await rebuild.ExecuteTask!;
            await rebuild.StopAsync(CancellationToken.None);
        }

        (Guid? triggerAfter, object versionAfter) = await TriggerAndVersionAsync(id);
        Assert.NotNull(triggerBefore);
        Assert.Equal(triggerBefore, triggerAfter);
        Assert.Equal(versionBefore, versionAfter);
    }

    [Fact]
    public void NextOccurrences_MatchesTheTickerQEngine()
    {
        IScheduledTaskEngine engine = host.Services.GetRequiredService<IScheduledTaskEngine>();
        var after = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var tehran = new TimeSpan(3, 30, 0);

        // What TickerQScheduledTaskEngine answers for the same pair: 03:00 local, Tehran keeping +03:30 all year.
        DateTimeOffset[] expected =
        [
            new(2026, 11, 11, 3, 0, 0, tehran),
            new(2026, 11, 12, 3, 0, 0, tehran),
            new(2026, 11, 13, 3, 0, 0, tehran),
        ];

        Assert.Equal(expected, engine.NextOccurrences("0 0 3 * * *", "Asia/Tehran", after, 3));
        Assert.True(engine.IsValidCron("0 0 3 * * *", "Asia/Tehran"));
        Assert.False(engine.IsValidCron("not a cron", "UTC"));
        Assert.False(engine.IsValidCron("0 2 * * *", "Nowhere/Atlantis"));
        Assert.Empty(engine.NextOccurrences("%cron%", "UTC", after, 3));
    }

    [Fact]
    public async Task RecoveryPass_ReEnqueuesAStaleItemOnce()
    {
        ScheduleProbe probe = await CreateProbeAsync();
        Guid item = await StaleItemAsync(probe);

        using (ScheduledRunItemRecoveryService recovery = ActivatorUtilities.CreateInstance<ScheduledRunItemRecoveryService>(host.Services))
        {
            await recovery.RecoverOnceAsync(CancellationToken.None);
        }

        ScheduledTaskRunItem done = await EventuallyAsync(async context => await context.ScheduledTaskRunItems.AsNoTracking()
            .FirstOrDefaultAsync(one => one.Id == item && one.Status == ScheduledRunItemStatus.Succeeded));

        // Nothing else will deliver it: a second send would show up within the worker's next beat.
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Equal(1, done.Attempts);
        Assert.Single(Recorder.Sends);
        Assert.Equal("recovered", (await ProbeAsync(probe.Id)).LastNote);
    }

    private static TouchProbeCommand Touch(string note) => new(new(Guid.Empty), new ProbeNote(note));

    private async Task<ErrorOr<T>> SendAsync<T>(IRequest<ErrorOr<T>> request)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = manager;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendAsync(request);
    }

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

    /// <summary>A one-off run whose one item was saved long ago and never handed to the engine.</summary>
    private async Task<Guid> StaleItemAsync(ScheduleProbe probe)
    {
        using IServiceScope scope = host.Services.CreateScope();
        DateTime longAgo = DateTime.UtcNow - new ScheduledTaskOptions().StaleAfter - TimeSpan.FromMinutes(1);
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(tree.North))!;
        string arguments = ScheduledCommandFactory.ToArguments(Touch("recovered"));

        var run = ScheduledTaskRun.Start(
            new ScheduledRunDefinition(null, ScheduledRunTrigger.OneOff, manager.Id, tree.North, path, TouchName, arguments), 1, longAgo);
        var item = ScheduledTaskRunItem.Create(run.Id, probe.Id.Value, rootId: null, longAgo);

        IScheduledTaskStore store = scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(Schema);
        store.AddRun(run, [item]);
        await store.SaveAsync(CancellationToken.None);

        return item.Id;
    }

    private async Task<(Guid? TriggerId, object Version)> TriggerAndVersionAsync(ScheduledTaskId id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();
        ScheduledTask task = await context.ScheduledTasks.AsNoTracking().IgnoreQueryFilters().SingleAsync(one => one.Id == id);

        return (task.EngineTriggerId, context.Entry(task).Property(RowVersionColumns.RowVersion).CurrentValue!);
    }

    private async Task<IReadOnlyCollection<ScheduledTaskRunItem>> ItemsAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>()
            .ScheduledTaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
    }

    private async Task<ScheduleProbe> ProbeAsync(ScheduleProbeId id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>()
            .Probes.AsNoTracking().IgnoreQueryFilters().SingleAsync(probe => probe.Id == id);
    }

    private async Task<T> EventuallyAsync<T>(Func<ScheduledTasksDbContext, Task<T?>> probe)
        where T : class
    {
        DateTime deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline)
        {
            using IServiceScope scope = host.Services.CreateScope();

            if (await probe(scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>()) is { } found)
            {
                return found;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Nothing matched within {Patience}.");
    }
}
