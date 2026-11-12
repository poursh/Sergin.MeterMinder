using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// Sergin's rows are the definition, so a host start puts the engine back in step with them: a task whose trigger the
/// engine lost gets a new one, every trigger is enabled exactly when its task is active, and a trigger of this host's
/// schemas that no live task links is removed. Each test seeds the rows
/// through a host without the rebuild, then starts a second host over the same engine and waits for the rebuild.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TriggerRebuildTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Schema = ScheduledTasksDbContext.Schema;
    private const string Cron = "0 * * * *";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly FakeScheduledTaskEngine engine = new();

    private WebApplicationFactory<Program> seeding = default!;
    private RbacScenario tree = default!;

    public async Task InitializeAsync()
    {
        seeding = CreateHost(hostedServices: false);
        await ScheduledTaskTestHost.ResetSchemaAsync(seeding);
        tree = await RbacScenario.BuildAsync(seeding);
    }

    public async Task DisposeAsync() => await seeding.DisposeAsync();

    [Fact]
    public async Task MissingTrigger_IsRecreatedAtStart()
    {
        var lost = Guid.CreateVersion7();
        ScheduledTaskId id = await SeedTaskAsync(lost, paused: false);

        await using WebApplicationFactory<Program> restarted = Start();

        Guid linked = await EventuallyAsync<Guid>(async () => (await TaskAsync(id)).EngineTriggerId is { } now && now != lost ? now : null);
        (ScheduledTrigger trigger, bool enabled) = engine.Triggers[linked];
        Assert.Equal(new ScheduledTrigger(Schema, id, Cron, "UTC"), trigger);
        Assert.True(enabled);
        Assert.False(engine.Triggers.ContainsKey(lost));
    }

    [Fact]
    public async Task RecreatedTrigger_IsLinkedUnderTheSystemActor()
    {
        var lost = Guid.CreateVersion7();
        ScheduledTaskId id = await SeedTaskAsync(lost, paused: false);

        await using WebApplicationFactory<Program> restarted = Start();

        await EventuallyAsync<Guid>(async () => (await TaskAsync(id)).EngineTriggerId is { } now && now != lost ? now : null);
        Assert.Equal(SystemActor.Id, await ModifiedByAsync(id));
    }

    [Fact]
    public async Task PausedTask_IsRecreatedDisabled_AndAnExistingTriggerIsDisabled()
    {
        ScheduledTaskId lostOne = await SeedTaskAsync(Guid.CreateVersion7(), paused: true);
        Guid kept = await engine.ScheduleAsync(new ScheduledTrigger(Schema, new ScheduledTaskId(Guid.Empty), Cron, "UTC"), CancellationToken.None);
        ScheduledTaskId keptOne = await SeedTaskAsync(kept, paused: true);

        await using WebApplicationFactory<Program> restarted = Start();

        Guid recreated = await EventuallyAsync<Guid>(async () =>
            (await TaskAsync(lostOne)).EngineTriggerId is { } now && engine.Triggers.ContainsKey(now) ? now : null);
        Assert.False(engine.Triggers[recreated].Enabled);
        Assert.True(await EventuallyTrueAsync(() => Task.FromResult(!engine.Triggers[kept].Enabled)));
        Assert.Equal(kept, (await TaskAsync(keptOne)).EngineTriggerId);
    }

    [Fact]
    public async Task ActiveTask_DisabledTrigger_IsEnabledAgainAtStart()
    {
        Guid disabled = await engine.ScheduleAsync(new ScheduledTrigger(Schema, new ScheduledTaskId(Guid.Empty), Cron, "UTC"), CancellationToken.None);
        await engine.SetEnabledAsync(disabled, enabled: false, CancellationToken.None);
        ScheduledTaskId id = await SeedTaskAsync(disabled, paused: false);

        await using WebApplicationFactory<Program> restarted = Start();

        Assert.True(await EventuallyTrueAsync(() => Task.FromResult(engine.Triggers[disabled].Enabled)));
        Assert.Equal(disabled, (await TaskAsync(id)).EngineTriggerId);
    }

    [Fact]
    public async Task OrphanTrigger_OfThisHostsSchemas_IsRemovedAtStart()
    {
        Guid orphan = engine.AddTrigger(new ScheduledTrigger(Schema, new ScheduledTaskId(Guid.CreateVersion7()), Cron, "UTC"), DateTime.UtcNow.AddMinutes(-10));
        Guid justMade = engine.AddTrigger(new ScheduledTrigger(Schema, new ScheduledTaskId(Guid.CreateVersion7()), Cron, "UTC"), DateTime.UtcNow);
        Guid otherHosts = engine.AddTrigger(new ScheduledTrigger("zz", new ScheduledTaskId(Guid.CreateVersion7()), Cron, "UTC"), DateTime.UtcNow.AddMinutes(-10));
        Guid linked = await engine.ScheduleAsync(new ScheduledTrigger(Schema, new ScheduledTaskId(Guid.Empty), Cron, "UTC"), CancellationToken.None);
        await SeedTaskAsync(linked, paused: false);

        await using WebApplicationFactory<Program> restarted = Start();

        Assert.True(await EventuallyTrueAsync(() => Task.FromResult(!engine.Triggers.ContainsKey(orphan))));
        Assert.True(engine.Triggers.ContainsKey(justMade));
        Assert.True(engine.Triggers.ContainsKey(otherHosts));
        Assert.True(engine.Triggers.ContainsKey(linked));
    }

    [Fact]
    public void OptedInContext_WithoutAnEngine_FailsStart()
    {
        using WebApplicationFactory<Program> host = ScheduledTaskTestHost.Create(factory, hostedServices: true);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => host.CreateClient());

        Assert.Contains(nameof(IScheduledTaskEngine), failure.Message, StringComparison.Ordinal);
        Assert.Contains(Schema, failure.Message, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateHost(bool hostedServices) =>
        ScheduledTaskTestHost.Create(
            factory,
            builder => builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(engine))),
            hostedServices);

    /// <summary>Starts a second host over the same engine and database: the restart the rebuild runs on.</summary>
    private WebApplicationFactory<Program> Start()
    {
        WebApplicationFactory<Program> restarted = CreateHost(hostedServices: true);
        using HttpClient client = restarted.CreateClient();

        return restarted;
    }

    private async Task<ScheduledTaskId> SeedTaskAsync(Guid engineTriggerId, bool paused)
    {
        using IServiceScope scope = seeding.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);
        IScheduledTaskStore store = scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(Schema);

        var task = ScheduledTask.Create(
            "tst.probes.touch", "{}", [new ScheduledTaskTarget(Guid.CreateVersion7(), null)], Cron, "UTC", new UserId(Guid.CreateVersion7()));
        task.LinkTrigger(engineTriggerId);

        if (paused)
        {
            task.Pause();
        }

        store.AddTask(task);
        await store.SaveAsync(CancellationToken.None);

        return task.Id;
    }

    private async Task<ScheduledTask> TaskAsync(ScheduledTaskId id)
    {
        using IServiceScope scope = seeding.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.ScheduledTasks.IgnoreQueryFilters().AsNoTracking().SingleAsync(task => task.Id == id);
    }

    private async Task<Guid?> ModifiedByAsync(ScheduledTaskId id)
    {
        using IServiceScope scope = seeding.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.ScheduledTasks.IgnoreQueryFilters().AsNoTracking()
            .Where(task => task.Id == id)
            .Select(task => EF.Property<Guid?>(task, AuditColumns.ModifiedBy))
            .SingleAsync();
    }

    /// <summary>Polls until <paramref name="probe"/> answers a value; the rebuild runs in the background after start.</summary>
    private static async Task<T> EventuallyAsync<T>(Func<Task<T?>> probe)
        where T : struct
    {
        DateTime deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            await Task.Delay(100);
        }

        Assert.Fail("The rebuild did not happen in time.");
        return default;
    }

    private static async Task<bool> EventuallyTrueAsync(Func<Task<bool>> probe) =>
        await EventuallyAsync<bool>(async () => await probe() ? true : null);
}
