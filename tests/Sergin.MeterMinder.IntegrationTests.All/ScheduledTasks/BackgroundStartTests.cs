using ErrorOr;
using FluentValidation;
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
/// Starting a background command: no Manage permission needed, only the command's own; refused at the click for a bad
/// request (the command's own validator, a command that is not background, too many records); one run with one item per
/// record, acting for the caller, each item checked live when it runs.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class BackgroundStartTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string SlowName = "tst.probes.slow";

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeScheduledTaskEngine Engine => host.Services.GetRequiredService<FakeScheduledTaskEngine>();

    private ProbeGate Gate => host.Services.GetRequiredService<ProbeGate>();

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
        Gate.Release();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task HolderOfTheCommandPermission_WithoutManage_Starts()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<BackgroundRunStartedResponse> started = await StartAsync(host, caller, Slow("slow"), probe);

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        Assert.Equal(ScheduledTasksDbContext.Schema, started.Value.Run.Schema);
        ScheduledTaskRun run = await RunAsync(started.Value.Run.RunId);
        Assert.Null(run.TaskId);
        Assert.Equal(ScheduledRunTrigger.Background, run.Trigger);
        Assert.Equal(caller.Id, run.ActingUserId);
        Assert.Equal(tree.North, run.ScopeId);
        Assert.Equal(SlowName, run.CommandName);
        Assert.Single(Engine.QueuedItems);
    }

    [Fact]
    public async Task WithoutTheCommandPermission_IsForbidden_AndWritesNothing()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Read.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<BackgroundRunStartedResponse> started = await StartAsync(host, caller, Slow("slow"), probe);

        Assert.True(started.IsError);
        Assert.Equal(ErrorType.Forbidden, started.FirstError.Type);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task ValidatorError_RefusesAtStart_AndWritesNothing()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<BackgroundRunStartedResponse> started = await StartAsync(host, caller, Slow("invalid"), probe);

        Assert.True(started.IsError);
        Error error = Assert.Single(started.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(SlowProbeCommand.Note), error.Code);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task NotBackground_IsRefused()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<BackgroundRunStartedResponse> started =
            await StartAsync(host, caller, new TouchProbeCommand(new(Guid.Empty), new ProbeNote("inline")), probe);

        Assert.True(started.IsError);
        Assert.Equal(nameof(StartBackgroundCommand.Template), started.FirstError.Code);
        Assert.Equal(ScheduledTaskTexts.NotBackground.Default, started.FirstError.Description);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task SeveralTargets_OneRun_NItems()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe[] probes = [await CreateProbeAsync(tree.Feeder1), await CreateProbeAsync(tree.Feeder1), await CreateProbeAsync(tree.Feeder2)];

        ErrorOr<BackgroundRunStartedResponse> started = await StartAsync(host, caller, Slow("slow"), probes);

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();
        ScheduledTaskRun run = Assert.Single(await context.ScheduledTaskRuns.AsNoTracking().ToListAsync());
        Assert.Equal(started.Value.Run.RunId, run.Id);
        Assert.Equal(3, run.Total);
        List<ScheduledTaskRunItem> items = await context.ScheduledTaskRunItems.AsNoTracking().Where(item => item.RunId == run.Id).ToListAsync();
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal(ScheduledRunItemStatus.Pending, item.Status));
        Assert.Equal(probes.Select(probe => probe.Id.Value).Order(), items.Select(item => item.TargetId).Order());
        Assert.Equal(3, Engine.QueuedItems.Count);
    }

    [Fact]
    public async Task UnreachableTargets_FailAlone()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe reachable = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe unreachable = await CreateProbeAsync(tree.South);
        Gate.Release();

        ErrorOr<BackgroundRunStartedResponse> started = await StartAsync(host, caller, Slow("slow"), reachable, unreachable);

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        await Engine.DrainAsync(host.Services);

        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();
        List<ScheduledTaskRunItem> items =
            await context.ScheduledTaskRunItems.AsNoTracking().Where(item => item.RunId == started.Value.Run.RunId).ToListAsync();
        Assert.Equal(ScheduledRunItemStatus.Succeeded, items.Single(item => item.TargetId == reachable.Id.Value).Status);
        ScheduledTaskRunItem outOfReach = items.Single(item => item.TargetId == unreachable.Id.Value);
        Assert.Equal(ScheduledRunItemStatus.Failed, outOfReach.Status);
        Assert.Equal(Error.NotFound().Code, outOfReach.ErrorCode);
        Assert.Equal(ScheduledRunStatus.Completed, (await RunAsync(started.Value.Run.RunId)).Status);
    }

    [Fact]
    public async Task TooManyTargets_IsRefused()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe[] probes = [await CreateProbeAsync(tree.Feeder1), await CreateProbeAsync(tree.Feeder1), await CreateProbeAsync(tree.Feeder1)];
        await using WebApplicationFactory<Program> capped =
            host.WithWebHostBuilder(builder => builder.UseSetting("Sergin:ScheduledTasks:MaxBackgroundTargets", "2"));

        ErrorOr<BackgroundRunStartedResponse> started = await StartAsync(capped, caller, Slow("slow"), probes);

        Assert.True(started.IsError);
        Assert.Contains(
            started.Errors,
            error => error.Type == ErrorType.Validation && error.Code == nameof(StartBackgroundCommand.Targets)
                && error.Description == ScheduledTaskTexts.TooManyTargets.Default);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public void MaxBackgroundTargets_BelowOne_IsRefusedNamingTheKey()
    {
        var options = new ScheduledTaskOptions { RetryIntervalsSeconds = ScheduledTaskOptions.DefaultRetryIntervalsSeconds, MaxBackgroundTargets = 0 };

        Assert.False(options.Validate(out string failure));
        Assert.Equal("Sergin:ScheduledTasks:MaxBackgroundTargets must be at least 1.", failure);
    }

    private static SlowProbeCommand Slow(string note) => new(new(Guid.Empty), note);

    private async Task<ErrorOr<BackgroundRunStartedResponse>> StartAsync(
        WebApplicationFactory<Program> on, IUserContext caller, IBaseCommand template, params ScheduleProbe[] probes)
    {
        using IServiceScope circuit = on.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .StartAsync(template, [.. probes.Select(probe => new ScheduledTaskTarget(probe.Id.Value, null))]);
    }

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

    private async Task<ScheduledTaskRun> RunAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.ScheduledTaskRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
    }

    private async Task AssertNothingWrittenAsync()
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        Assert.False(await context.ScheduledTaskRuns.AnyAsync());
        Assert.False(await context.ScheduledTaskRunItems.AnyAsync());
        Assert.Empty(Engine.QueuedItems);
    }
}

/// <summary>
/// <see cref="SlowProbeCommandHandler"/> that also loads its probe, as a real handler would, so a probe outside the run's
/// reach is <see cref="Error.NotFound"/>: the live reach check the start relies on.
/// </summary>
internal sealed class ReachingSlowProbeCommandHandler(ProbeGate gate, ScheduledTasksDbContext context)
    : ICommandHandler<SlowProbeCommand, TouchProbeResponse>
{
    public async Task<ErrorOr<TouchProbeResponse>> Handle(SlowProbeCommand request, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        return await context.Probes.AnyAsync(probe => probe.Id == request.Id, cancellationToken)
            ? new TouchProbeResponse(request.Note)
            : Error.NotFound();
    }
}
