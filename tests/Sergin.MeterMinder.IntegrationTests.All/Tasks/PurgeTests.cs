using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// The purge loop: every <c>PurgeInterval</c>, each opted-in module's finished runs older than <c>Retention</c> go, with
/// their items; a recent run and a run still in progress, whatever its age, stay.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PurgeTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Schema = TasksDbContext.Schema;

    private WebApplicationFactory<Program> host = default!;

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(
            factory,
            builder =>
            {
                builder.UseSetting("Sergin:Tasks:PurgeInterval", "00:00:01");
                builder.UseSetting("Sergin:Tasks:Retention", "1.00:00:00");
                builder.ConfigureServices(services =>
                    services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(new FakeTaskEngine())));
            },
            hostedServices: true);

        await TaskTestHost.ResetSchemaAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task OldRuns_ArePurged_RecentKept()
    {
        DateTime old = DateTime.UtcNow.AddDays(-2);
        (Guid oldRun, Guid oldItem) = await SeedRunAsync(old, completedAtUtc: old);
        (Guid oldSkipped, _) = await SeedRunAsync(old, completedAtUtc: old, skipped: true);
        (Guid recentRun, Guid recentItem) = await SeedRunAsync(DateTime.UtcNow, completedAtUtc: DateTime.UtcNow);
        (Guid unfinished, Guid unfinishedItem) = await SeedRunAsync(old, completedAtUtc: null);

        DateTime deadline = DateTime.UtcNow.AddSeconds(20);

        while (await RunExistsAsync(oldRun) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
        }

        Assert.False(await RunExistsAsync(oldRun));
        Assert.False(await ItemExistsAsync(oldItem));
        Assert.False(await RunExistsAsync(oldSkipped));
        Assert.True(await RunExistsAsync(recentRun));
        Assert.True(await ItemExistsAsync(recentItem));
        Assert.True(await RunExistsAsync(unfinished));
        Assert.True(await ItemExistsAsync(unfinishedItem));
    }

    private async Task<(Guid Run, Guid Item)> SeedRunAsync(DateTime startedAtUtc, DateTime? completedAtUtc, bool skipped = false)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Schema);
        var definition = new TaskRunDefinition(
            null, TaskRunTrigger.Once, new UserId(Guid.CreateVersion7()), Guid.CreateVersion7(), "root", "tst.probes.touch", "{}");

        if (skipped)
        {
            var skippedRun = TaskRun.Skipped(definition, startedAtUtc);
            store.AddRun(skippedRun, []);
            await store.SaveAsync(CancellationToken.None);

            return (skippedRun.Id, Guid.Empty);
        }

        var run = TaskRun.Start(definition, 1, startedAtUtc);
        var item = TaskRunItem.Create(run.Id, Guid.CreateVersion7(), null, DateTime.UtcNow);

        if (completedAtUtc is { } at)
        {
            item.MarkSucceeded(at);
            run.Complete(1, 0, 0, at);
        }

        store.AddRun(run, [item]);
        await store.SaveAsync(CancellationToken.None);

        return (run.Id, item.Id);
    }

    private async Task<bool> RunExistsAsync(Guid id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<TasksDbContext>().TaskRuns.AnyAsync(run => run.Id == id);
    }

    private async Task<bool> ItemExistsAsync(Guid id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<TasksDbContext>().TaskRunItems.AnyAsync(item => item.Id == id);
    }
}
