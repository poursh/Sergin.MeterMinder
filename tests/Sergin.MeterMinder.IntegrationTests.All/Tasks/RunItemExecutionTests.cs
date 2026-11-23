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
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// One run item, executed: a fresh scope seeded with the run's user as a delegated (not a system) identity and the
/// run's scope, the command built from the stored arguments with the target's id, and one ISender.Send through the
/// whole pipeline. Grants are checked live, a target out of reach fails alone, and only an exception is retried.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RunItemExecutionTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string TouchName = "tst.probes.touch";

    // What a hand-built descriptor declares: every default; only the factory reads it, and it reads none of this.
    private static readonly TaskSettings Settings =
        new("tst.probes.hand-built", TaskSettings.DefaultInactivityTimeout, MaxDuration: null, Cancelable: true, Schedulable: false);

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeTaskEngine Engine => host.Services.GetRequiredService<FakeTaskEngine>();

    private ProbeFailureSwitch Failures => host.Services.GetRequiredService<ProbeFailureSwitch>();

    private ProbeSendRecorder Recorder => host.Services.GetRequiredService<ProbeSendRecorder>();

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<FakeTaskEngine>();
            services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(provider => provider.GetRequiredService<FakeTaskEngine>()));
            services.AddSingleton<ProbeFailureSwitch>();
            services.AddSingleton<ProbeSendRecorder>();
            services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<Success>>, TouchProbeCommandHandler>();
            services.AddScoped<IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
            services.AddTransient<IPipelineBehavior<TouchProbeCommand, ErrorOr<Success>>, RecordingProbeBehavior>();
        }));

        await TaskTestHost.ResetSchemaAsync(host);
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

        TaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Succeeded, item.Status);
        Assert.Equal("hello", (await ProbeAsync(probe.Id)).LastNote);

        TaskRunItem refused = Assert.Single(await ItemsAsync(invalidRun));
        Assert.Equal(TaskRunItemStatus.Failed, refused.Status);
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

        TaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Failed, item.Status);
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

        TaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Failed, item.Status);
        Assert.Equal(Error.Forbidden().Code, item.ErrorCode);
        Assert.Equal(1, item.Attempts);
    }

    [Fact]
    public async Task AlreadyFinishedItem_CompletesItsOpenRun()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        Guid run = await StartRunAsync(user, tree.North, "hello", probe.Id.Value);

        // A delivery that saved the item's final status and stopped before completing the run.
        using (IServiceScope scope = host.Services.CreateScope())
        {
            ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(TasksDbContext.Schema);
            TaskRunItem item = Assert.Single(await ItemsAsync(run));
            (TaskRun _, TaskRunItem tracked) = (await store.GetRunItemAsync(item.Id, CancellationToken.None))!.Value;
            tracked.MarkRunning(DateTime.UtcNow);
            tracked.MarkSucceeded(DateTime.UtcNow);
            await store.SaveAsync(CancellationToken.None);
        }

        Assert.Equal(TaskRunStatus.Running, (await RunAsync(run)).Status);

        await Engine.DrainAsync(host.Services);

        TaskRun completed = await RunAsync(run);
        Assert.Equal(TaskRunStatus.Completed, completed.Status);
        Assert.Equal(1, completed.Succeeded);
        Assert.Equal(1, Assert.Single(await ItemsAsync(run)).Attempts);
        Assert.Null((await ProbeAsync(probe.Id)).LastNote);
    }

    [Fact]
    public void ValueObjectThatRefuses_IsArgumentsInvalid()
    {
        TaskDescriptor command = new(
            "tst.probes.checked", TasksDbContext.Schema, typeof(CheckedProbeCommand), typeof(ScheduleProbe),
            CommandTargetKind.Entity, typeof(ScheduleProbeId), Selectable: false, Settings);

        ErrorOr<IBaseCommand> refused = TaskCommandFactory.Build(command, """{"Note":{"Value":"refused"}}""", Guid.CreateVersion7());
        ErrorOr<IBaseCommand> accepted = TaskCommandFactory.Build(command, """{"Note":{"Value":"fine"}}""", Guid.CreateVersion7());

        Assert.Equal(TaskErrors.ArgumentsInvalid.Code, refused.FirstError.Code);
        Assert.Equal("fine", Assert.IsType<CheckedProbeCommand>(accepted.Value).Note.Value);
    }

    [Fact]
    public void IdTypeWithoutAGuidConstructor_IsArgumentsInvalid()
    {
        TaskDescriptor command = new(
            "tst.probes.bad-id", TasksDbContext.Schema, typeof(TouchProbeCommand), typeof(ScheduleProbe),
            CommandTargetKind.Entity, typeof(ProbeNote), Selectable: false, Settings);

        ErrorOr<IBaseCommand> built = TaskCommandFactory.Build(command, """{"Note":{"Value":"x"}}""", Guid.CreateVersion7());

        Assert.Equal(TaskErrors.ArgumentsInvalid.Code, built.FirstError.Code);
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

        IReadOnlyCollection<TaskRunItem> items = await ItemsAsync(run);
        TaskRunItem outOfReach = items.Single(item => item.TargetId == south.Id.Value);
        Assert.Equal(TaskRunItemStatus.Failed, outOfReach.Status);
        Assert.Equal(Error.NotFound().Code, outOfReach.ErrorCode);
        Assert.All(items.Where(item => item != outOfReach), item => Assert.Equal(TaskRunItemStatus.Succeeded, item.Status));
        Assert.Null((await ProbeAsync(south.Id)).LastNote);

        TaskRun completed = await RunAsync(run);
        Assert.Equal(TaskRunStatus.Completed, completed.Status);
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

        TaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Succeeded, item.Status);
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

        TaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Failed, item.Status);
        Assert.Equal("Tasks.Exhausted", item.ErrorCode);
        Assert.Equal(3, item.Attempts);
        Assert.Equal(TaskRunStatus.Completed, (await RunAsync(run)).Status);
    }

    [Fact]
    public void Arguments_RoundTrip_ValueObjects()
    {
        TaskDescriptor command = Catalog().Commands.Single(one => one.Name == TouchName);
        var targetId = Guid.CreateVersion7();

        string arguments = TaskCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("hello")));
        ErrorOr<IBaseCommand> built = TaskCommandFactory.Build(command, arguments, targetId);

        Assert.False(JsonNode.Parse(arguments)!.AsObject().ContainsKey("Id"));
        TouchProbeCommand touch = Assert.IsType<TouchProbeCommand>(built.Value);
        Assert.Equal("hello", touch.Note.Value);
        Assert.Equal(targetId, touch.Id.Value);
    }

    [Fact]
    public void Arguments_ThatNoLongerFit_AreArgumentsInvalid()
    {
        TaskDescriptor command = Catalog().Commands.Single(one => one.Name == TouchName);

        ErrorOr<IBaseCommand> built = TaskCommandFactory.Build(command, """{"Note":42}""", Guid.CreateVersion7());

        Assert.Equal(TaskErrors.ArgumentsInvalid.Code, built.FirstError.Code);
    }

    private TaskCatalog Catalog() => host.Services.GetRequiredService<TaskCatalog>();

    private Task<TestCaller> TouchCallerAsync(Guid scopeId) =>
        tree.CreateGrantedCallerAsync(scopeId, ScheduleProbePermissions.Touch.Permission);

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

    /// <summary>A one-off run of <c>tst.probes.touch</c> for <paramref name="user"/>, one item per target, enqueued.</summary>
    private async Task<Guid> StartRunAsync(TestCaller user, Guid scopeId, string note, params Guid[] targets)
    {
        using IServiceScope scope = host.Services.CreateScope();
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(scopeId))!;
        string arguments = TaskCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote(note)));

        var run = TaskRun.Start(
            new TaskRunDefinition(null, TaskRunTrigger.Once, user.Id, scopeId, path, TouchName, arguments),
            targets.Length,
            DateTime.UtcNow);
        TaskRunItem[] items = [.. targets.Select(target => TaskRunItem.Create(run.Id, target, DateTime.UtcNow))];

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

    private async Task<TaskRun> RunAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return await context.TaskRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
    }

    private async Task<ScheduleProbe> ProbeAsync(ScheduleProbeId id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

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
    : IPipelineBehavior<TouchProbeCommand, ErrorOr<Success>>
{
    public Task<ErrorOr<Success>> Handle(
        TouchProbeCommand request, RequestHandlerDelegate<ErrorOr<Success>> next, CancellationToken cancellationToken)
    {
        recorder.Record(caller, scope.ScopeId);
        return next(cancellationToken);
    }
}

/// <summary>Loads the probe through EF (so the scope filter applies), leaves the note and saves.</summary>
internal sealed class TouchProbeCommandHandler(TasksDbContext context, ProbeFailureSwitch failures)
    : ICommandHandler<TouchProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(TouchProbeCommand request, CancellationToken cancellationToken)
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

        return Result.Success;
    }
}
