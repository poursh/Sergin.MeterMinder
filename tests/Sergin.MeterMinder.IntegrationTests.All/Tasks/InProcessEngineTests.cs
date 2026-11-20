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
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Hosts.Tasks;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.Infrastructure.Tasks.TickerQ;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// The engine a host gets with no TickerQ licence: a blank licence path registers the in-process stand-in, which runs
/// run-now and one-off items in a background worker (retries included) and never fires a saved schedule. A non-blank path
/// that names no file still refuses start, so a typo never falls back silently.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class InProcessEngineTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Schema = TasksDbContext.Schema;
    private const string TouchName = "tst.probes.touch";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;
    private TestCaller manager = default!;

    private ProbeFailureSwitch Failures => host.Services.GetRequiredService<ProbeFailureSwitch>();

    private ProbeSendRecorder Recorder => host.Services.GetRequiredService<ProbeSendRecorder>();

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(
            factory,
            builder =>
            {
                builder.UseSetting("Sergin:Tasks:RetryIntervalsSeconds:0", "1");
                builder.UseSetting("Sergin:Tasks:MaxAttempts", "2");
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

        await TaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
        manager = await tree.CreateGrantedCallerAsync(
            tree.North, TaskPermissions.Manage.Permission, ScheduleProbePermissions.Touch.Permission);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public void BlankLicensePath_RegistersTheStandIn()
    {
        ITaskEngine engine = host.Services.GetRequiredService<ITaskEngine>();

        Assert.IsType<InProcessTaskEngine>(engine);
        Assert.False(engine.FiresTriggers);
        Assert.Null(host.Services.GetService<TickerQTaskEngine>());
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

        ErrorOr<TaskScheduleRunStartedResponse> started = await SendAsync(
            new RunTaskOnceCommand(Schema, Touch("in process"), [new(probe.Id.Value, null)]));
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        TaskRun run = await EventuallyAsync(async context => await context.TaskRuns.AsNoTracking()
            .FirstOrDefaultAsync(one => one.Id == started.Value.RunId && one.Status == TaskRunStatus.Completed));

        Assert.Equal(1, run.Succeeded);
        TaskRunItem item = Assert.Single(await ItemsAsync(run.Id));
        Assert.Equal(TaskRunItemStatus.Succeeded, item.Status);
        Assert.Equal("in process", (await ProbeAsync(probe.Id)).LastNote);

        (IUserContext caller, Guid _) = Assert.Single(Recorder.Sends);
        Assert.Equal(manager.Id, Assert.IsAssignableFrom<IDelegatedUserContext>(caller).Id);
    }

    [Fact]
    public async Task Retry_ThroughTheStandIn_EndsExhausted()
    {
        ScheduleProbe probe = await CreateProbeAsync();
        Failures.ThrowTimes = int.MaxValue;

        ErrorOr<TaskScheduleRunStartedResponse> started = await SendAsync(
            new RunTaskOnceCommand(Schema, Touch("never"), [new(probe.Id.Value, null)]));
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        TaskRunItem item = await EventuallyAsync(async context => await context.TaskRunItems.AsNoTracking()
            .FirstOrDefaultAsync(one => one.RunId == started.Value.RunId && one.Status == TaskRunItemStatus.Failed));

        Assert.Equal(TaskErrors.ExhaustedCode, item.ErrorCode);
        Assert.Equal(2, item.Attempts);
    }

    [Fact]
    public async Task SavedCron_IsStoredAndNeverFires()
    {
        ScheduleProbe probe = await CreateProbeAsync();

        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(
            new CreateTaskScheduleCommand(Schema, Touch("every second"), [new(probe.Id.Value, null)], "* * * * * *", "UTC"));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Code : string.Empty);
        var id = new TaskScheduleId(created.Value.Id);

        await Task.Delay(TimeSpan.FromSeconds(3));

        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        TaskSchedule task = await context.TaskSchedules.AsNoTracking().IgnoreQueryFilters().SingleAsync(one => one.Id == id);
        Assert.Equal(TaskScheduleState.Active, task.State);
        Assert.False(await context.TaskRuns.AnyAsync(run => run.ScheduleId == id));
        Assert.Empty(Recorder.Sends);
    }

    [Fact]
    public async Task TriggerRebuild_WithTheStandIn_LeavesSavedTasksAlone()
    {
        ScheduleProbe probe = await CreateProbeAsync();
        ErrorOr<CreateTaskScheduleCommandResponse> created = await SendAsync(
            new CreateTaskScheduleCommand(Schema, Touch("kept"), [new(probe.Id.Value, null)], "0 3 * * *", "UTC"));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Code : string.Empty);
        var id = new TaskScheduleId(created.Value.Id);
        (Guid? triggerBefore, object versionBefore) = await TriggerAndVersionAsync(id);

        // One whole rebuild pass, as a host start runs it (the shared test host leaves the service out).
        using (TaskTriggerRebuildService rebuild = ActivatorUtilities.CreateInstance<TaskTriggerRebuildService>(host.Services))
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
        ITaskEngine engine = host.Services.GetRequiredService<ITaskEngine>();
        var after = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var tehran = new TimeSpan(3, 30, 0);

        // What TickerQTaskEngine answers for the same pair: 03:00 local, Tehran keeping +03:30 all year.
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

        using (TaskRunItemRecoveryService recovery = ActivatorUtilities.CreateInstance<TaskRunItemRecoveryService>(host.Services))
        {
            await recovery.RecoverOnceAsync(CancellationToken.None);
        }

        TaskRunItem done = await EventuallyAsync(async context => await context.TaskRunItems.AsNoTracking()
            .FirstOrDefaultAsync(one => one.Id == item && one.Status == TaskRunItemStatus.Succeeded));

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
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        var probe = ScheduleProbe.Create();
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        return probe;
    }

    /// <summary>A one-off run whose one item was saved long ago and never handed to the engine.</summary>
    private async Task<Guid> StaleItemAsync(ScheduleProbe probe)
    {
        using IServiceScope scope = host.Services.CreateScope();
        DateTime longAgo = DateTime.UtcNow - new TaskOptions().StaleAfter - TimeSpan.FromMinutes(1);
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(tree.North))!;
        string arguments = TaskCommandFactory.ToArguments(Touch("recovered"));

        var run = TaskRun.Start(
            new TaskRunDefinition(null, TaskRunTrigger.Once, manager.Id, tree.North, path, TouchName, arguments), 1, longAgo);
        var item = TaskRunItem.Create(run.Id, probe.Id.Value, rootId: null, longAgo);

        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Schema);
        store.AddRun(run, [item]);
        await store.SaveAsync(CancellationToken.None);

        return item.Id;
    }

    private async Task<(Guid? TriggerId, object Version)> TriggerAndVersionAsync(TaskScheduleId id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        TaskSchedule task = await context.TaskSchedules.AsNoTracking().IgnoreQueryFilters().SingleAsync(one => one.Id == id);

        return (task.EngineTriggerId, context.Entry(task).Property(RowVersionColumns.RowVersion).CurrentValue!);
    }

    private async Task<IReadOnlyCollection<TaskRunItem>> ItemsAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
            .TaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
    }

    private async Task<ScheduleProbe> ProbeAsync(ScheduleProbeId id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
            .Probes.AsNoTracking().IgnoreQueryFilters().SingleAsync(probe => probe.Id == id);
    }

    private async Task<T> EventuallyAsync<T>(Func<TasksDbContext, Task<T?>> probe)
        where T : class
    {
        DateTime deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline)
        {
            using IServiceScope scope = host.Services.CreateScope();

            if (await probe(scope.ServiceProvider.GetRequiredService<TasksDbContext>()) is { } found)
            {
                return found;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Nothing matched within {Patience}.");
    }
}
