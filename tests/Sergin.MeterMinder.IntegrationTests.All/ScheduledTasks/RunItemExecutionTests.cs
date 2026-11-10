using System.Text.Json.Nodes;
using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// One run item, executed: a fresh scope seeded with the run's user as a delegated (not a system) identity and the
/// run's scope, the command built from the stored arguments with the target's id, and one ISender.Send through the
/// whole pipeline. Grants are checked live, a target out of reach fails alone, and only an exception is retried.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RunItemExecutionTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string TouchName = "tst.probes.touch";

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeScheduledTaskEngine Engine => host.Services.GetRequiredService<FakeScheduledTaskEngine>();

    private ProbeFailureSwitch Failures => host.Services.GetRequiredService<ProbeFailureSwitch>();

    private ProbeSendRecorder Recorder => host.Services.GetRequiredService<ProbeSendRecorder>();

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<FakeScheduledTaskEngine>();
            services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(provider => provider.GetRequiredService<FakeScheduledTaskEngine>()));
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
    public async Task Success_SendsThroughThePipeline()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe other = await CreateProbeAsync(tree.Feeder1);

        Guid run = await StartRunAsync(user, tree.North, "hello", probe.Id.Value);
        Guid invalidRun = await StartRunAsync(user, tree.North, "invalid", other.Id.Value);
        await Engine.DrainAsync(host.Services);

        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status);
        Assert.Equal("hello", (await ProbeAsync(probe.Id)).LastNote);

        ScheduledTaskRunItem refused = Assert.Single(await ItemsAsync(invalidRun));
        Assert.Equal(ScheduledRunItemStatus.Failed, refused.Status);
        Assert.Equal(nameof(TouchProbeCommand.Note), refused.ErrorCode);
        Assert.Equal(1, refused.Attempts);
        Assert.Null((await ProbeAsync(other.Id)).LastNote);
    }

    [Fact]
    public async Task ActsForTheRunsUser_InTheRunsScope()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        Guid run = await StartRunAsync(user, tree.North, "hello", probe.Id.Value);
        await Engine.DrainAsync(host.Services);

        (IUserContext caller, Guid scopeId) = Assert.Single(Recorder.Sends);
        IDelegatedUserContext delegated = Assert.IsAssignableFrom<IDelegatedUserContext>(caller);
        Assert.IsNotAssignableFrom<ISystemUserContext>(caller);
        Assert.Equal(user.Id, delegated.Id);
        Assert.Equal(run, delegated.RunId);
        Assert.Equal(tree.North, scopeId);
    }

    [Fact]
    public async Task NoGrant_IsForbidden_NotRetried()
    {
        TestCaller user = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Read.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        Guid run = await StartRunAsync(user, tree.North, "hello", probe.Id.Value);
        await Engine.DrainAsync(host.Services);

        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Failed, item.Status);
        Assert.Equal(Error.Forbidden().Code, item.ErrorCode);
        Assert.Equal(1, item.Attempts);
        Assert.Null((await ProbeAsync(probe.Id)).LastNote);
    }

    [Fact]
    public async Task DeactivatedUser_IsForbidden()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        await tree.SendAsync(new DeactivateUserCommand(new(user.Id.Value)));

        Guid run = await StartRunAsync(user, tree.North, "hello", probe.Id.Value);
        await Engine.DrainAsync(host.Services);

        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Failed, item.Status);
        Assert.Equal(Error.Forbidden().Code, item.ErrorCode);
    }

    [Fact]
    public async Task TargetOutOfReach_IsNotFound_OthersSucceed()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder2);
        ScheduleProbe south = await CreateProbeAsync(tree.South);

        Guid run = await StartRunAsync(user, tree.North, "hello", first.Id.Value, south.Id.Value, second.Id.Value);
        await Engine.DrainAsync(host.Services);

        IReadOnlyCollection<ScheduledTaskRunItem> items = await ItemsAsync(run);
        ScheduledTaskRunItem outOfReach = items.Single(item => item.TargetId == south.Id.Value);
        Assert.Equal(ScheduledRunItemStatus.Failed, outOfReach.Status);
        Assert.Equal(Error.NotFound().Code, outOfReach.ErrorCode);
        Assert.All(items.Where(item => item != outOfReach), item => Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status));
        Assert.Null((await ProbeAsync(south.Id)).LastNote);

        ScheduledTaskRun completed = await RunAsync(run);
        Assert.Equal(ScheduledRunStatus.Completed, completed.Status);
        Assert.Equal(2, completed.Succeeded);
        Assert.Equal(1, completed.Failed);
        Assert.NotNull(completed.CompletedAtUtc);
    }

    [Fact]
    public async Task Exception_IsRetried_ThenSucceeds()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        Failures.ThrowTimes = 1;

        Guid run = await StartRunAsync(user, tree.North, "hello", probe.Id.Value);
        await Engine.DrainAsync(host.Services);

        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status);
        Assert.Equal(2, item.Attempts);
        Assert.Null(item.ErrorCode);
        Assert.Equal("hello", (await ProbeAsync(probe.Id)).LastNote);
    }

    [Fact]
    public async Task Exception_Exhausts_AtMaxAttempts()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        Failures.ThrowTimes = int.MaxValue;

        Guid run = await StartRunAsync(user, tree.North, "hello", probe.Id.Value);
        await Engine.DrainAsync(host.Services);

        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Failed, item.Status);
        Assert.Equal("ScheduledTasks.Exhausted", item.ErrorCode);
        Assert.Equal(3, item.Attempts);
        Assert.Equal(ScheduledRunStatus.Completed, (await RunAsync(run)).Status);
    }

    [Fact]
    public void Arguments_RoundTrip_ValueObjects()
    {
        ScheduledCommand command = Catalog().Commands.Single(one => one.Name == TouchName);
        var targetId = Guid.CreateVersion7();

        string arguments = ScheduledCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("hello")));
        ErrorOr<IBaseCommand> built = ScheduledCommandFactory.Build(command, arguments, targetId, rootId: null);

        Assert.False(JsonNode.Parse(arguments)!.AsObject().ContainsKey("Id"));
        TouchProbeCommand touch = Assert.IsType<TouchProbeCommand>(built.Value);
        Assert.Equal("hello", touch.Note.Value);
        Assert.Equal(targetId, touch.Id.Value);
    }

    [Fact]
    public void ChildCommand_GetsRootIdAndId()
    {
        ScheduledCommand command = Catalog().Commands.Single(one => one.Name == "tst.probes.touch-child");
        var rootId = Guid.CreateVersion7();
        var targetId = Guid.CreateVersion7();

        string arguments = ScheduledCommandFactory.ToArguments(
            new TouchProbeChildCommand(new(Guid.Empty), new(Guid.Empty), new ProbeNote("child")));
        ErrorOr<IBaseCommand> built = ScheduledCommandFactory.Build(command, arguments, targetId, rootId);

        JsonObject stored = JsonNode.Parse(arguments)!.AsObject();
        Assert.False(stored.ContainsKey("Id"));
        Assert.False(stored.ContainsKey("RootId"));
        TouchProbeChildCommand touch = Assert.IsType<TouchProbeChildCommand>(built.Value);
        Assert.Equal(rootId, touch.RootId.Value);
        Assert.Equal(targetId, touch.Id.Value);
        Assert.Equal("child", touch.Note.Value);
    }

    [Fact]
    public void Arguments_ThatNoLongerFit_AreArgumentsInvalid()
    {
        ScheduledCommand command = Catalog().Commands.Single(one => one.Name == TouchName);

        ErrorOr<IBaseCommand> built = ScheduledCommandFactory.Build(command, """{"Note":42}""", Guid.CreateVersion7(), rootId: null);

        Assert.Equal(ScheduledTaskErrors.ArgumentsInvalid.Code, built.FirstError.Code);
    }

    private ScheduledCommandCatalog Catalog() => host.Services.GetRequiredService<ScheduledCommandCatalog>();

    private Task<TestCaller> TouchCallerAsync(Guid scopeId) =>
        tree.CreateGrantedCallerAsync(scopeId, ScheduleProbePermissions.Touch.Permission);

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

    /// <summary>A one-off run of <c>tst.probes.touch</c> for <paramref name="user"/>, one item per target, enqueued.</summary>
    private async Task<Guid> StartRunAsync(TestCaller user, Guid scopeId, string note, params Guid[] targets)
    {
        using IServiceScope scope = host.Services.CreateScope();
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(scopeId))!;
        string arguments = ScheduledCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote(note)));

        var run = ScheduledTaskRun.Start(
            new ScheduledRunDefinition(null, ScheduledRunTrigger.OneOff, user.Id, scopeId, path, TouchName, arguments),
            targets.Length,
            DateTime.UtcNow);
        ScheduledTaskRunItem[] items = [.. targets.Select(target => ScheduledTaskRunItem.Create(run.Id, target, rootId: null))];

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

    private async Task<ScheduledTaskRun> RunAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.ScheduledTaskRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
    }

    private async Task<ScheduleProbe> ProbeAsync(ScheduleProbeId id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.Probes.AsNoTracking().IgnoreQueryFilters().SingleAsync(probe => probe.Id == id);
    }
}

/// <summary>Makes the probe handler throw the next <see cref="ThrowTimes"/> sends.</summary>
internal sealed class ProbeFailureSwitch
{
    private int throwTimes;

    public int ThrowTimes
    {
        get => Volatile.Read(ref throwTimes);
        set => Volatile.Write(ref throwTimes, value);
    }

    public bool ShouldThrow()
    {
        int current;

        do
        {
            current = ThrowTimes;

            if (current <= 0)
            {
                return false;
            }
        }
        while (Interlocked.CompareExchange(ref throwTimes, current - 1, current) != current);

        return true;
    }
}

/// <summary>Who sent each probe command, and in which scope, as the pipeline saw it.</summary>
internal sealed class ProbeSendRecorder
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(IUserContext Caller, Guid ScopeId)> sends = new();

    public IReadOnlyCollection<(IUserContext Caller, Guid ScopeId)> Sends => [.. sends];

    public void Record(IUserContext caller, Guid scopeId) => sends.Enqueue((caller, scopeId));
}

/// <summary>Records the caller and scope inside the pipeline, after the platform's behaviors.</summary>
internal sealed class RecordingProbeBehavior(IUserContext caller, IAuthorizationScopeContext scope, ProbeSendRecorder recorder)
    : IPipelineBehavior<TouchProbeCommand, ErrorOr<TouchProbeResponse>>
{
    public Task<ErrorOr<TouchProbeResponse>> Handle(
        TouchProbeCommand request, RequestHandlerDelegate<ErrorOr<TouchProbeResponse>> next, CancellationToken cancellationToken)
    {
        recorder.Record(caller, scope.ScopeId);
        return next(cancellationToken);
    }
}

/// <summary>Loads the probe through EF (so the scope filter applies), leaves the note and saves.</summary>
internal sealed class TouchProbeCommandHandler(ScheduledTasksDbContext context, ProbeFailureSwitch failures)
    : ICommandHandler<TouchProbeCommand, TouchProbeResponse>
{
    public async Task<ErrorOr<TouchProbeResponse>> Handle(TouchProbeCommand request, CancellationToken cancellationToken)
    {
        if (failures.ShouldThrow())
        {
            throw new InvalidOperationException("The probe handler was told to throw.");
        }

        ScheduleProbe? probe = await context.Probes.SingleOrDefaultAsync(one => one.Id == request.Id, cancellationToken);

        if (probe is null)
        {
            return Error.NotFound();
        }

        probe.Touch(request.Note);
        await context.SaveChangesAsync(cancellationToken);

        return new TouchProbeResponse(request.Note.Value);
    }
}
