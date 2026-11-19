using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

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
                services.AddTransient<IRequestHandler<SlowProbeCommand, ErrorOr<TouchProbeResponse>>, SlowProbeCommandHandler>();
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
    public async Task ActingUser_CancelsPendingItems()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        BackgroundRunRef run = await StartAsync(caller, 3);

        ErrorOr<Success> cancelled = await CancelAsync(caller, run);

        Assert.False(cancelled.IsError, cancelled.IsError ? cancelled.FirstError.Code : string.Empty);
        (ScheduledTaskRun stored, IReadOnlyList<ScheduledTaskRunItem> items) = await ReadAsync(run);
        Assert.Equal(3, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal(ScheduledRunItemStatus.Cancelled, item.Status);
            Assert.NotNull(item.CompletedAtUtc);
        });
        Assert.Equal(ScheduledRunStatus.Completed, stored.Status);
        Assert.Equal((3, 0, 0, 3), (stored.Total, stored.Succeeded, stored.Failed, stored.Cancelled));
        Assert.NotNull(stored.CancelRequestedAtUtc);
    }

    [Fact]
    public async Task ManageHolderOnTheRunsScope_MayCancel()
    {
        TestCaller starter = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TestCaller manager = await tree.CreateGrantedCallerAsync(tree.North, ScheduledTaskPermissions.Manage.Permission);
        BackgroundRunRef run = await StartAsync(starter, 2);

        ErrorOr<Success> cancelled = await CancelAsync(manager, run);

        Assert.False(cancelled.IsError, cancelled.IsError ? cancelled.FirstError.Code : string.Empty);
        (ScheduledTaskRun stored, IReadOnlyList<ScheduledTaskRunItem> items) = await ReadAsync(run);
        Assert.All(items, item => Assert.Equal(ScheduledRunItemStatus.Cancelled, item.Status));
        Assert.Equal(ScheduledRunStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task ManageHolderElsewhere_IsForbidden()
    {
        TestCaller starter = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TestCaller manager = await tree.CreateGrantedCallerAsync(tree.South, ScheduledTaskPermissions.Manage.Permission);
        BackgroundRunRef run = await StartAsync(starter, 2);

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
        BackgroundRunRef run = await StartAsync(starter, 2);

        ErrorOr<Success> cancelled = await CancelAsync(other, run);

        Assert.True(cancelled.IsError);
        Assert.Equal(ErrorType.Forbidden, cancelled.FirstError.Type);
        await AssertUntouchedAsync(run);
    }

    [Fact]
    public async Task UnknownRunOrSchema_IsNotFound()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        BackgroundRunRef run = await StartAsync(caller, 1);

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
        BackgroundRunRef run = await StartAsync(caller, 2);
        Gate.Release();
        await Engine.DrainAsync(host.Services);

        ErrorOr<Success> cancelled = await CancelAsync(caller, run);

        Assert.True(cancelled.IsError);
        Assert.Equal(ErrorType.Conflict, cancelled.FirstError.Type);
        Assert.Equal(ScheduledTaskErrors.RunFinished.Code, cancelled.FirstError.Code);
        Assert.Equal("ScheduledTasks.RunFinished", cancelled.FirstError.Code);
        Assert.Equal(ScheduledTaskTexts.RunFinished.Default, cancelled.FirstError.Description);
        (ScheduledTaskRun stored, IReadOnlyList<ScheduledTaskRunItem> items) = await ReadAsync(run);
        Assert.Equal((ScheduledRunStatus.Completed, 2, 2, 0, 0), (stored.Status, stored.Total, stored.Succeeded, stored.Failed, stored.Cancelled));
        Assert.Null(stored.CancelRequestedAtUtc);
        Assert.All(items, item => Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status));
    }

    [Fact]
    public async Task ALateDeliveryOfACancelledItem_SendsNothing()
    {
        // Released first: were the item sent, the test would fail on the counter rather than hang on the gate.
        Gate.Release();
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        BackgroundRunRef run = await StartAsync(caller, 1);
        ErrorOr<Success> cancelled = await CancelAsync(caller, run);
        Assert.False(cancelled.IsError, cancelled.IsError ? cancelled.FirstError.Code : string.Empty);

        ScheduledRunItemReference queued = Assert.Single(Engine.QueuedItems);
        using (IServiceScope scope = host.Services.CreateScope())
        {
            bool retry = await scope.ServiceProvider.GetRequiredService<ScheduledRunItemExecutor>()
                .ExecuteAsync(queued.Schema, queued.RunItemId, CancellationToken.None);
            Assert.False(retry);
        }

        Assert.Equal(0, Gate.Entered);
        (ScheduledTaskRun stored, IReadOnlyList<ScheduledTaskRunItem> items) = await ReadAsync(run);
        Assert.Equal(ScheduledRunItemStatus.Cancelled, Assert.Single(items).Status);
        Assert.Equal(0, Assert.Single(items).Attempts);
        Assert.Equal((ScheduledRunStatus.Completed, 1), (stored.Status, stored.Cancelled));
    }

    private async Task<BackgroundRunRef> StartAsync(TestCaller caller, int count)
    {
        List<BackgroundTarget> targets = [];

        using (IServiceScope scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.Feeder1);
            ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

            for (int i = 0; i < count; i++)
            {
                var probe = ScheduleProbe.Create();
                context.Probes.Add(probe);
                targets.Add(new BackgroundTarget(probe.Id.Value));
            }

            await context.SaveChangesAsync();
        }

        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        ErrorOr<BackgroundRunStartedResponse> started = await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .StartAsync(new SlowProbeCommand(new(Guid.Empty), "slow"), targets);
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        return started.Value.Run;
    }

    private Task<ErrorOr<Success>> CancelAsync(IUserContext caller, BackgroundRunRef run) => CancelAsync(caller, run, tree.North);

    private async Task<ErrorOr<Success>> CancelAsync(IUserContext caller, BackgroundRunRef run, Guid currentScope)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(currentScope);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendAsync(new CancelBackgroundRunCommand(run.Schema, run.RunId));
    }

    private async Task<(ScheduledTaskRun Run, IReadOnlyList<ScheduledTaskRunItem> Items)> ReadAsync(BackgroundRunRef run)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return (
            await context.ScheduledTaskRuns.AsNoTracking().SingleAsync(one => one.Id == run.RunId),
            await context.ScheduledTaskRunItems.AsNoTracking().Where(item => item.RunId == run.RunId).ToListAsync());
    }

    private async Task AssertUntouchedAsync(BackgroundRunRef run)
    {
        (ScheduledTaskRun stored, IReadOnlyList<ScheduledTaskRunItem> items) = await ReadAsync(run);
        Assert.Equal(ScheduledRunStatus.Running, stored.Status);
        Assert.Null(stored.CancelRequestedAtUtc);
        Assert.Equal(0, stored.Cancelled);
        Assert.All(items, item => Assert.Equal(ScheduledRunItemStatus.Pending, item.Status));
    }
}
