using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// Cancelling a background run: its acting user, or a Manage holder on the run's scope, may cancel it while it runs;
/// every item not yet started becomes Cancelled and the run completes; a cancelled item delivered late sends nothing.
/// The fake engine holds every item, so they stay Pending until a test drains it.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CancelRunTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
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
    public async Task ActingUser_CancelsPendingItems()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TaskRunRef run = await StartAsync(caller, 3);

        ErrorOr<Success> cancelled = await CancelAsync(caller, run);

        Assert.False(cancelled.IsError, cancelled.IsError ? cancelled.FirstError.Code : string.Empty);
        (TaskRun stored, IReadOnlyList<TaskRunItem> items) = await ReadAsync(run);
        Assert.Equal(3, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal(TaskRunItemStatus.Cancelled, item.Status);
            Assert.NotNull(item.CompletedAtUtc);
        });
        Assert.Equal(TaskRunStatus.Completed, stored.Status);
        Assert.Equal((3, 0, 0, 3), (stored.Total, stored.Succeeded, stored.Failed, stored.Cancelled));
        Assert.NotNull(stored.CancelRequestedAtUtc);
    }

    [Fact]
    public async Task ManageHolderOnTheRunsScope_MayCancel()
    {
        TestCaller starter = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TestCaller manager = await tree.CreateGrantedCallerAsync(tree.North, TaskPermissions.Manage.Permission);
        TaskRunRef run = await StartAsync(starter, 2);

        ErrorOr<Success> cancelled = await CancelAsync(manager, run);

        Assert.False(cancelled.IsError, cancelled.IsError ? cancelled.FirstError.Code : string.Empty);
        (TaskRun stored, IReadOnlyList<TaskRunItem> items) = await ReadAsync(run);
        Assert.All(items, item => Assert.Equal(TaskRunItemStatus.Cancelled, item.Status));
        Assert.Equal(TaskRunStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task ManageHolderElsewhere_IsForbidden()
    {
        TestCaller starter = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TestCaller manager = await tree.CreateGrantedCallerAsync(tree.South, TaskPermissions.Manage.Permission);
        TaskRunRef run = await StartAsync(starter, 2);

        // Sent from the scope the holder does hold Manage on: what counts is the run's scope, not the caller's current one.
        ErrorOr<Success> cancelled = await CancelAsync(manager, run, tree.South);

        Assert.True(cancelled.IsError);
        Assert.Equal(ErrorType.Forbidden, cancelled.FirstError.Type);
        await AssertUntouchedAsync(run);
    }

    [Fact]
    public async Task AnotherUserWithoutManage_IsForbidden()
    {
        TestCaller starter = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TestCaller other = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TaskRunRef run = await StartAsync(starter, 2);

        ErrorOr<Success> cancelled = await CancelAsync(other, run);

        Assert.True(cancelled.IsError);
        Assert.Equal(ErrorType.Forbidden, cancelled.FirstError.Type);
        await AssertUntouchedAsync(run);
    }

    [Fact]
    public async Task UnknownRunOrSchema_IsNotFound()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TaskRunRef run = await StartAsync(caller, 1);

        ErrorOr<Success> unknownRun = await CancelAsync(caller, run with { RunId = Guid.CreateVersion7() });
        ErrorOr<Success> unknownSchema = await CancelAsync(caller, run with { Schema = "nope" });

        Assert.Equal(ErrorType.NotFound, unknownRun.FirstError.Type);
        Assert.Equal(ErrorType.NotFound, unknownSchema.FirstError.Type);
        await AssertUntouchedAsync(run);
    }

    [Fact]
    public async Task FinishedRun_IsRunFinished()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TaskRunRef run = await StartAsync(caller, 2);
        Gate.Release();
        await Engine.DrainAsync(host.Services);

        ErrorOr<Success> cancelled = await CancelAsync(caller, run);

        Assert.True(cancelled.IsError);
        Assert.Equal(ErrorType.Conflict, cancelled.FirstError.Type);
        Assert.Equal(TaskErrors.RunFinished.Code, cancelled.FirstError.Code);
        Assert.Equal("Tasks.RunFinished", cancelled.FirstError.Code);
        Assert.Equal(TaskTexts.RunFinished.Default, cancelled.FirstError.Description);
        (TaskRun stored, IReadOnlyList<TaskRunItem> items) = await ReadAsync(run);
        Assert.Equal((TaskRunStatus.Completed, 2, 2, 0, 0), (stored.Status, stored.Total, stored.Succeeded, stored.Failed, stored.Cancelled));
        Assert.Null(stored.CancelRequestedAtUtc);
        Assert.All(items, item => Assert.Equal(TaskRunItemStatus.Succeeded, item.Status));
    }

    [Fact]
    public async Task ALateDeliveryOfACancelledItem_SendsNothing()
    {
        // Released first: were the item sent, the test would fail on the counter rather than hang on the gate.
        Gate.Release();
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TaskRunRef run = await StartAsync(caller, 1);
        ErrorOr<Success> cancelled = await CancelAsync(caller, run);
        Assert.False(cancelled.IsError, cancelled.IsError ? cancelled.FirstError.Code : string.Empty);

        TaskRunItemReference queued = Assert.Single(Engine.QueuedItems);
        using (IServiceScope scope = host.Services.CreateScope())
        {
            bool retry = await scope.ServiceProvider.GetRequiredService<TaskRunItemExecutor>()
                .ExecuteAsync(queued.Schema, queued.RunItemId, CancellationToken.None);
            Assert.False(retry);
        }

        Assert.Equal(0, Gate.Entered);
        (TaskRun stored, IReadOnlyList<TaskRunItem> items) = await ReadAsync(run);
        Assert.Equal(TaskRunItemStatus.Cancelled, Assert.Single(items).Status);
        Assert.Equal(0, Assert.Single(items).Attempts);
        Assert.Equal((TaskRunStatus.Completed, 1), (stored.Status, stored.Cancelled));
    }

    private async Task<TaskRunRef> StartAsync(TestCaller caller, int count)
    {
        List<TaskTarget> targets = [];

        using (IServiceScope scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.Feeder1);
            TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

            for (int i = 0; i < count; i++)
            {
                var probe = ScheduleProbe.Create();
                context.Probes.Add(probe);
                targets.Add(new TaskTarget(probe.Id.Value));
            }

            await context.SaveChangesAsync();
        }

        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        ErrorOr<TaskRunStartedResponse> started = await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .StartTaskAsync(new SlowProbeCommand(new(Guid.Empty), "slow"), targets);
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        return started.Value.Run;
    }

    private Task<ErrorOr<Success>> CancelAsync(IUserContext caller, TaskRunRef run) => CancelAsync(caller, run, tree.North);

    private async Task<ErrorOr<Success>> CancelAsync(IUserContext caller, TaskRunRef run, Guid currentScope)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(currentScope);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendAsync(new CancelTaskRunCommand(run.Schema, run.RunId));
    }

    private async Task<(TaskRun Run, IReadOnlyList<TaskRunItem> Items)> ReadAsync(TaskRunRef run)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        return (
            await context.TaskRuns.AsNoTracking().SingleAsync(one => one.Id == run.RunId),
            await context.TaskRunItems.AsNoTracking().Where(item => item.RunId == run.RunId).ToListAsync());
    }

    private async Task AssertUntouchedAsync(TaskRunRef run)
    {
        (TaskRun stored, IReadOnlyList<TaskRunItem> items) = await ReadAsync(run);
        Assert.Equal(TaskRunStatus.Running, stored.Status);
        Assert.Null(stored.CancelRequestedAtUtc);
        Assert.Equal(0, stored.Cancelled);
        Assert.All(items, item => Assert.Equal(TaskRunItemStatus.Pending, item.Status));
    }
}
