using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The scheduled-task model and the per-module opt-in: a context implementing <see cref="IScheduledTasksDbContext"/>
/// carries the four tables in its own schema with the aggregate features applied, a context that does not carries
/// none, and the real host, which opts nothing in, is unaffected.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScheduledTaskModelTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private WebApplicationFactory<Program> host = default!;

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory);
        await ScheduledTaskTestHost.ResetSchemaAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public void OptedInContext_MapsTheFourTables()
    {
        IModel model = Model();
        IEntityType tasks = model.FindEntityType(typeof(ScheduledTask))!;
        IEntityType targets = tasks.FindNavigation(nameof(ScheduledTask.Targets))!.TargetEntityType;

        Assert.Equal(
            [
                ("tst", "scheduled_tasks"),
                ("tst", "scheduled_task_targets"),
                ("tst", "scheduled_task_runs"),
                ("tst", "scheduled_task_run_items"),
            ],
            [
                .. new[] { tasks, targets, model.FindEntityType(typeof(ScheduledTaskRun))!, model.FindEntityType(typeof(ScheduledTaskRunItem))! }
                    .Select(entityType => (entityType.GetSchema(), entityType.GetTableName())),
            ]);

        string[] columns = [.. tasks.GetProperties().Select(property => property.GetColumnName(StoreObjectIdentifier.Table("scheduled_tasks", "tst"))!)];

        Assert.Contains("created_at_utc", columns);
        Assert.Contains("deleted_at_utc", columns);
        Assert.Contains("row_version", columns);
        Assert.Contains("scope_id", columns);
        Assert.Contains("scope_path", columns);
    }

    [Fact]
    public void Arguments_IsJsonb()
    {
        IModel model = Model();

        Assert.Equal("jsonb", model.FindEntityType(typeof(ScheduledTask))!.FindProperty(nameof(ScheduledTask.Arguments))!.GetColumnType());
        Assert.Equal("jsonb", model.FindEntityType(typeof(ScheduledTaskRun))!.FindProperty(nameof(ScheduledTaskRun.Arguments))!.GetColumnType());
    }

    [Fact]
    public void TargetIndex_Exists()
    {
        IModel model = Model();
        IEntityType targets = model.FindEntityType(typeof(ScheduledTask))!.FindNavigation(nameof(ScheduledTask.Targets))!.TargetEntityType;
        IEntityType runs = model.FindEntityType(typeof(ScheduledTaskRun))!;

        Assert.Contains(targets.GetIndexes(), index => Names(index) == nameof(ScheduledTaskTarget.TargetId));
        Assert.Equal(["TaskId", nameof(ScheduledTaskTarget.TargetId)], [.. targets.FindPrimaryKey()!.Properties.Select(property => property.Name)]);
        Assert.Contains(
            runs.GetIndexes(),
            index => Names(index) == $"{nameof(ScheduledTaskRun.TaskId)},{nameof(ScheduledTaskRun.StartedAtUtc)}");
    }

    [Fact]
    public void OneRunningRunPerTask_IsAPartialUniqueIndex()
    {
        IEntityType runs = Model().FindEntityType(typeof(ScheduledTaskRun))!;

        IIndex index = Assert.Single(runs.GetIndexes(), one => one.IsUnique);
        Assert.Equal(nameof(ScheduledTaskRun.TaskId), Names(index));
        Assert.Equal("status = 'Running'", index.GetFilter());
        Assert.Equal("ix_scheduled_task_runs_task_id_running", index.GetDatabaseName());
    }

    [Fact]
    public void NotOptedInContext_HasNoTables_AndNoRegistration()
    {
        using IServiceScope scope = host.Services.CreateScope();
        IModel deviceManagement = Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>()).Model;

        Assert.Null(deviceManagement.FindEntityType(typeof(ScheduledTask)));
        Assert.Null(deviceManagement.FindEntityType(typeof(ScheduledTaskRun)));

        ScheduledTasksRegistration registration = Assert.Single(host.Services.GetServices<ScheduledTasksRegistration>());
        Assert.Equal(new ScheduledTasksRegistration("tst", typeof(ScheduledTasksDbContext)), registration);
    }

    [Fact]
    public void TwoOptedInContexts_ShareOneConfigurationSource()
    {
        var services = new ServiceCollection();
        IConfigurationSection sergin = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sergin:ConnectionStrings:Database"] = "Host=unused" })
            .Build()
            .GetSection("Sergin");

        services.AddModuleDbContext<ScheduledTasksDbContext, IScheduledTasksTestDbContext, IScheduledTasksTestUnitOfWork>(sergin, "tst");
        services.AddModuleDbContext<ScheduledTasksDbContext, IScheduledTasksTestDbContext, IScheduledTasksTestUnitOfWork>(sergin, "tst2");

        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(ScheduledTasksRegistration)));
        Assert.Single(
            services,
            descriptor => descriptor.ImplementationInstance is ModuleConfigurationSource source
                && source.AggregateConfigurationTypes.Contains(typeof(ScheduledTaskAggregateFeatureConfiguration)));
    }

    [Fact]
    public void PermissionCatalogue_HasManage()
    {
        PermissionCatalog catalog = host.Services.GetRequiredService<PermissionCatalog>();

        Assert.True(catalog.Contains(ScheduledTaskPermissions.Manage));
    }

    [Fact]
    public void OptedInHost_RegistryDeclaresTheScheduledTaskFeatures()
    {
        AggregateFeatures features = host.Services.GetRequiredService<AggregateFeatureRegistry>().For(typeof(ScheduledTask));

        Assert.Equal(new AggregateFeatures(Audited: true, SoftDeletable: true, Versioned: true, Scoped: true), features);
    }

    [Fact]
    public async Task RealHost_StillStarts()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.Services.GetServices<ScheduledTasksRegistration>());
        Assert.Equal(AggregateFeatures.None, factory.Services.GetRequiredService<AggregateFeatureRegistry>().For(typeof(ScheduledTask)));
    }

    [Fact]
    public async Task Task_Run_AndItems_RoundTrip()
    {
        var savedBy = new UserId(Guid.CreateVersion7());
        var target = new ScheduledTaskTarget(Guid.CreateVersion7(), RootId: null);
        var task = ScheduledTask.Create("tst.probes.touch", """{"Note":{"Value":"x"}}""", [target], "0 2 * * *", "Asia/Tehran", savedBy);
        task.LinkTrigger(Guid.CreateVersion7());

        var run = ScheduledTaskRun.Start(
            new ScheduledRunDefinition(task.Id, ScheduledRunTrigger.RunNow, savedBy, Guid.CreateVersion7(), "root", task.CommandName, task.Arguments),
            total: 1,
            startedAtUtc: DateTime.UtcNow);
        var item = ScheduledTaskRunItem.Create(run.Id, target.TargetId, target.RootId, DateTime.UtcNow);
        item.MarkRunning(DateTime.UtcNow);
        item.MarkFailed("Code", "Description", DateTime.UtcNow);
        run.Complete(succeeded: 0, failed: 1, DateTime.UtcNow);

        using (IServiceScope scope = host.Services.CreateScope())
        {
            ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();
            context.ScheduledTasks.Add(task);
            context.ScheduledTaskRuns.Add(run);
            context.ScheduledTaskRunItems.Add(item);
            await context.SaveChangesAsync();
        }

        using (IServiceScope scope = host.Services.CreateScope())
        {
            ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

            ScheduledTask stored = await context.ScheduledTasks.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == task.Id);
            Assert.Equal([target], stored.Targets);
            Assert.Equal(ScheduledTaskState.Active, stored.State);
            Assert.Equal(savedBy, stored.DelegatingUserId);
            Assert.Equal(task.EngineTriggerId, stored.EngineTriggerId);

            ScheduledTaskRun storedRun = await context.ScheduledTaskRuns.SingleAsync(candidate => candidate.Id == run.Id);
            Assert.Equal((ScheduledRunStatus.Completed, 1, 0, 1), (storedRun.Status, storedRun.Total, storedRun.Succeeded, storedRun.Failed));

            ScheduledTaskRunItem storedItem = await context.ScheduledTaskRunItems.SingleAsync(candidate => candidate.Id == item.Id);
            Assert.Equal((ScheduledRunItemStatus.Failed, 1, "Code"), (storedItem.Status, storedItem.Attempts, storedItem.ErrorCode));
        }
    }

    private static string Names(IIndex index) => string.Join(",", index.Properties.Select(property => property.Name));

    private IModel Model()
    {
        using IServiceScope scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>().Model;
    }
}
