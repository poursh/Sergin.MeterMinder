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
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// Starting a background command: no Manage permission needed, only the command's own; refused at the click for a bad
/// request (the command's own validator, a command that is not background, too many records); one run with one item per
/// record, acting for the caller, each item checked live when it runs.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TaskStartTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string SlowName = "tst.probes.slow";

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeTaskEngine Engine => host.Services.GetRequiredService<FakeTaskEngine>();

    private ProbeGate Gate => host.Services.GetRequiredService<ProbeGate>();

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<FakeTaskEngine>();
                services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(provider => provider.GetRequiredService<FakeTaskEngine>()));
                services.AddSingleton<ProbeGate>();
                services.AddTransient<IRequestHandler<SlowProbeCommand, ErrorOr<Success>>, ReachingSlowProbeCommandHandler>();
                services.AddScoped<IValidator<SlowProbeCommand>, SlowProbeCommandValidator>();
                services.AddScoped<IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
            }));

        await TaskTestHost.ResetSchemaAsync(host);
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

        ErrorOr<TaskRunStartedResponse> started = await StartAsync(host, caller, Slow("slow"), probe);

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        Assert.Equal(TasksDbContext.Schema, started.Value.Run.Schema);
        TaskRun run = await RunAsync(started.Value.Run.RunId);
        Assert.Null(run.ScheduleId);
        Assert.Equal(TaskRunTrigger.Manual, run.Trigger);
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

        ErrorOr<TaskRunStartedResponse> started = await StartAsync(host, caller, Slow("slow"), probe);

        Assert.True(started.IsError);
        Assert.Equal(ErrorType.Forbidden, started.FirstError.Type);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task ValidatorError_RefusesAtStart_AndWritesNothing()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<TaskRunStartedResponse> started = await StartAsync(host, caller, Slow("invalid"), probe);

        Assert.True(started.IsError);
        Error error = Assert.Single(started.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(SlowProbeCommand.Note), error.Code);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task NonTask_IsRefused()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<TaskRunStartedResponse> started =
            await StartAsync(host, caller, new CheckedProbeCommand(new(Guid.Empty), new CheckedNote("inline")), probe);

        Assert.True(started.IsError);
        Error refused = Assert.Single(started.Errors, error => error.Code == TaskErrors.NotBackground.Code);
        Assert.Equal(ErrorType.Validation, refused.Type);
        Assert.Equal(TaskTexts.NotBackground.Default, refused.Description);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Labels_AreStoredPerItem()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder1);
        string longLabel = new('x', 250);

        ErrorOr<TaskRunStartedResponse> started = await StartLabelledAsync(
            caller, [new TaskTarget(first.Id.Value, null, "P-1"), new TaskTarget(second.Id.Value, null, longLabel)]);

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        Dictionary<Guid, string?> labels = await LabelsAsync(started.Value.Run.RunId);
        Assert.Equal("P-1", labels[first.Id.Value]);
        Assert.Equal(longLabel[..200], labels[second.Id.Value]);
    }

    [Fact]
    public void ALongLabel_IsNeverCutInsideASurrogatePair()
    {
        // 199 chars, then an emoji (a surrogate pair) straddling the 200-char cut.
        string straddling = new string('x', 199) + "\U0001F600" + "tail";
        string fitting = new string('x', 198) + "\U0001F600" + "tail";

        Assert.Equal(new string('x', 199), TaskRunStarter.CutLabel(straddling));
        Assert.Equal(new string('x', 198) + "\U0001F600", TaskRunStarter.CutLabel(fitting));
        Assert.Equal("short", TaskRunStarter.CutLabel("short"));
    }

    [Fact]
    public async Task NoLabel_StoresNull()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        ErrorOr<TaskRunStartedResponse> started = await StartLabelledAsync(caller, [new TaskTarget(probe.Id.Value)]);

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        Assert.Null(Assert.Single(await LabelsAsync(started.Value.Run.RunId)).Value);
    }

    [Fact]
    public async Task SeveralTargets_OneRun_NItems()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe[] probes = [await CreateProbeAsync(tree.Feeder1), await CreateProbeAsync(tree.Feeder1), await CreateProbeAsync(tree.Feeder2)];

        ErrorOr<TaskRunStartedResponse> started = await StartAsync(host, caller, Slow("slow"), probes);

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        TaskRun run = Assert.Single(await context.TaskRuns.AsNoTracking().ToListAsync());
        Assert.Equal(started.Value.Run.RunId, run.Id);
        Assert.Equal(3, run.Total);
        List<TaskRunItem> items = await context.TaskRunItems.AsNoTracking().Where(item => item.RunId == run.Id).ToListAsync();
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal(TaskRunItemStatus.Pending, item.Status));
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

        ErrorOr<TaskRunStartedResponse> started = await StartAsync(host, caller, Slow("slow"), reachable, unreachable);

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        await Engine.DrainAsync(host.Services);

        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        List<TaskRunItem> items =
            await context.TaskRunItems.AsNoTracking().Where(item => item.RunId == started.Value.Run.RunId).ToListAsync();
        Assert.Equal(TaskRunItemStatus.Succeeded, items.Single(item => item.TargetId == reachable.Id.Value).Status);
        TaskRunItem outOfReach = items.Single(item => item.TargetId == unreachable.Id.Value);
        Assert.Equal(TaskRunItemStatus.Failed, outOfReach.Status);
        Assert.Equal(Error.NotFound().Code, outOfReach.ErrorCode);
        Assert.Equal(TaskRunStatus.Completed, (await RunAsync(started.Value.Run.RunId)).Status);
    }

    [Fact]
    public async Task TooManyTargets_IsRefused()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe[] probes = [await CreateProbeAsync(tree.Feeder1), await CreateProbeAsync(tree.Feeder1), await CreateProbeAsync(tree.Feeder1)];
        await using WebApplicationFactory<Program> capped =
            host.WithWebHostBuilder(builder => builder.UseSetting("Sergin:Tasks:MaxTargets", "2"));

        ErrorOr<TaskRunStartedResponse> started = await StartAsync(capped, caller, Slow("slow"), probes);

        Assert.True(started.IsError);
        Assert.Contains(
            started.Errors,
            error => error.Type == ErrorType.Validation && error.Code == TaskErrors.TooManyTargets.Code
                && error.Description == TaskTexts.TooManyTargets.Default);
        await AssertNothingWrittenAsync(capped);
    }

    [Fact]
    public async Task TemplateValidator_ReadsTheScopedAggregate_WithinTheCommandsReach()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe reachable = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe unreachable = await CreateProbeAsync(tree.South);
        await using WebApplicationFactory<Program> reading = host.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IValidator<SlowProbeCommand>, ExistingSlowProbeCommandValidator>())));

        ErrorOr<TaskRunStartedResponse> outOfReach = await StartAsync(reading, caller, Slow("slow"), unreachable);

        // Refused as the pipeline's validator would refuse it for that caller: the probe is not visible to them.
        Assert.True(outOfReach.IsError);
        Error error = Assert.Single(outOfReach.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(SlowProbeCommand.Id), error.Code);
        await AssertNothingWrittenAsync(reading);

        ErrorOr<TaskRunStartedResponse> inReach = await StartAsync(reading, caller, Slow("slow"), reachable);

        Assert.False(inReach.IsError, inReach.IsError ? inReach.FirstError.Code : string.Empty);
        Assert.Single(reading.Services.GetRequiredService<FakeTaskEngine>().QueuedItems);
    }

    [Fact]
    public void MaxTargets_BelowOne_IsRefusedNamingTheKey()
    {
        var options = new TaskOptions { RetryIntervalsSeconds = TaskOptions.DefaultRetryIntervalsSeconds, MaxTargets = 0 };

        Assert.False(options.Validate(out string failure));
        Assert.Equal("Sergin:Tasks:MaxTargets must be at least 1.", failure);
    }

    private static SlowProbeCommand Slow(string note) => new(new(Guid.Empty), note);

    private async Task<ErrorOr<TaskRunStartedResponse>> StartLabelledAsync(IUserContext caller, IReadOnlyCollection<TaskTarget> targets)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().StartTaskAsync(Slow("slow"), targets);
    }

    private async Task<Dictionary<Guid, string?>> LabelsAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.TaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToDictionaryAsync(item => item.TargetId, item => item.TargetLabel);
    }

    private async Task<ErrorOr<TaskRunStartedResponse>> StartAsync(
        WebApplicationFactory<Program> on, IUserContext caller, IBaseCommand template, params ScheduleProbe[] probes)
    {
        using IServiceScope circuit = on.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .StartTaskAsync(template, [.. probes.Select(probe => new TaskTarget(probe.Id.Value))]);
    }

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

    private async Task<TaskRun> RunAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.TaskRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
    }

    private Task AssertNothingWrittenAsync() => AssertNothingWrittenAsync(host);

    /// <summary>No run, no item, and nothing queued on <paramref name="on"/>'s engine: the host the start was sent to.</summary>
    private static async Task AssertNothingWrittenAsync(WebApplicationFactory<Program> on)
    {
        using IServiceScope scope = on.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        Assert.False(await context.TaskRuns.AnyAsync());
        Assert.False(await context.TaskRunItems.AnyAsync());
        Assert.Empty(on.Services.GetRequiredService<FakeTaskEngine>().QueuedItems);
    }
}

/// <summary>
/// A validator that reads the scoped probe its command targets, as <c>MustExistIn</c> on a <c>Scoped()</c> root does: it
/// sees the probe only under the reach the command's permissions publish.
/// </summary>
internal sealed class ExistingSlowProbeCommandValidator : AbstractValidator<SlowProbeCommand>
{
    public ExistingSlowProbeCommandValidator(TasksDbContext context) =>
        RuleFor(command => command.Id).MustAsync((id, ct) => context.Probes.AnyAsync(probe => probe.Id == id, ct));
}

/// <summary>
/// <see cref="SlowProbeCommandHandler"/> that also loads its probe, as a real handler would, so a probe outside the run's
/// reach is <see cref="Error.NotFound"/>: the live reach check the start relies on.
/// </summary>
internal sealed class ReachingSlowProbeCommandHandler(ProbeGate gate, TasksDbContext context)
    : ICommandHandler<SlowProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(SlowProbeCommand request, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        return await context.Probes.AnyAsync(probe => probe.Id == request.Id, cancellationToken)
            ? Result.Success
            : Error.NotFound();
    }
}
