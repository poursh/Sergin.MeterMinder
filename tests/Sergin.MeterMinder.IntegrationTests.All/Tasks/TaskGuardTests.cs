using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// A task is started, never sent: sent inline it is refused, by its type and before anything else runs, whether or not it
/// is configured; inside a run item the same send passes; a command that is not a task still sends inline.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TaskGuardTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
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
                services.AddTransient<IRequestHandler<SlowProbeCommand, ErrorOr<Success>>, SlowProbeCommandHandler>();
                services.AddScoped<IValidator<SlowProbeCommand>, SlowProbeCommandValidator>();
                services.AddTransient<IRequestHandler<NoteProbeCommand, ErrorOr<Success>>, NoteProbeCommandHandler>();
                services.AddSingleton<InlineSendCounter>();
                services.AddTransient<IRequestHandler<ReadDeviceProfileCommand, ErrorOr<Success>>, CountingReadProfileHandler>();
                services.AddTransient<IRequestHandler<UnconfiguredProbeCommand, ErrorOr<Success>>, CountingUnconfiguredProbeHandler>();
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
    public async Task TaskSentInline_IsRefused()
    {
        ErrorOr<Success> result = await SendInlineAsync(new ReadDeviceProfileCommand(new DeviceIntenralId(Guid.NewGuid())));

        AssertRefused(result);
    }

    [Fact]
    public async Task UnconfiguredTask_SentInline_IsRefused()
    {
        // No source declares it, so the registry holds no task settings for it: only its type makes it a task.
        ErrorOr<Success> result = await SendInlineAsync(new UnconfiguredProbeCommand(new(Guid.NewGuid()), new ProbeNote("inline")));

        AssertRefused(result);
    }

    [Fact]
    public async Task ConfiguredTask_SentInline_IsRefused_BeforeItsHandler()
    {
        ErrorOr<Success> result = await tree.TrySendAsync(new SlowProbeCommand(new(Guid.NewGuid()), "inline"));

        AssertRefused(result);
        Assert.Equal(0, Gate.Entered);
    }

    [Fact]
    public async Task InsideARunItem_Passes()
    {
        TestCaller user = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        Gate.Release();
        string arguments = TaskCommandFactory.ToArguments(new SlowProbeCommand(new(Guid.Empty), "slow"));
        Guid run = await StartRunAsync(user, tree.North, SlowName, arguments, probe.Id.Value);

        await Engine.DrainAsync(host.Services);

        TaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(TaskRunItemStatus.Succeeded, item.Status);
        Assert.Equal(1, Gate.Entered);
    }

    [Fact]
    public async Task NonTask_SentInline_Passes()
    {
        // The scenario's default caller is the administrator at the root, and the probe command requires no permission.
        ErrorOr<Success> result = await tree.TrySendAsync(new NoteProbeCommand(new(Guid.NewGuid()), new ProbeNote("inline")));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
    }

    /// <summary>Sends <paramref name="request"/> straight through <see cref="ISender"/>, outside any run item.</summary>
    private async Task<ErrorOr<Success>> SendInlineAsync(IRequest<ErrorOr<Success>> request)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private void AssertRefused(ErrorOr<Success> result)
    {
        Error error = Assert.Single(result.Errors);
        Assert.Equal(TaskErrors.MustRunAsTaskCode, error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(0, host.Services.GetRequiredService<InlineSendCounter>().Count);
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

    /// <summary>A one-off run of <paramref name="commandName"/> for <paramref name="user"/>, one item per target, enqueued.</summary>
    private async Task<Guid> StartRunAsync(TestCaller user, Guid scopeId, string commandName, string arguments, params Guid[] targets)
    {
        using IServiceScope scope = host.Services.CreateScope();
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(scopeId))!;

        var run = TaskRun.Start(
            new TaskRunDefinition(null, TaskRunTrigger.Once, user.Id, scopeId, path, commandName, arguments),
            targets.Length,
            DateTime.UtcNow);
        TaskRunItem[] items = [.. targets.Select(target => TaskRunItem.Create(run.Id, target, rootId: null, DateTime.UtcNow))];

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
}

/// <summary>Counts the handler calls the guard must prevent.</summary>
internal sealed class InlineSendCounter
{
    private int count;

    public int Count => Volatile.Read(ref count);

    public void Hit() => Interlocked.Increment(ref count);
}

internal sealed class CountingReadProfileHandler(InlineSendCounter counter) : ICommandHandler<ReadDeviceProfileCommand>
{
    public Task<ErrorOr<Success>> Handle(ReadDeviceProfileCommand request, CancellationToken cancellationToken)
    {
        counter.Hit();
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }
}

internal sealed class CountingUnconfiguredProbeHandler(InlineSendCounter counter) : ICommandHandler<UnconfiguredProbeCommand>
{
    public Task<ErrorOr<Success>> Handle(UnconfiguredProbeCommand request, CancellationToken cancellationToken)
    {
        counter.Hit();
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }
}
