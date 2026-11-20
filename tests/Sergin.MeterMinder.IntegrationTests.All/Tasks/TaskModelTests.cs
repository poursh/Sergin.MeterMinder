using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.Administration.UserAccess.Infrastructure.Data;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Tasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// The scheduled-task model and the per-module opt-in: a context implementing <see cref="ITasksDbContext"/>
/// carries the four tables in its own schema with the aggregate features applied, a context that does not carries
/// none, and the real host, which opts nothing in, is unaffected.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TaskModelTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private WebApplicationFactory<Program> host = default!;

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory);
        await TaskTestHost.ResetSchemaAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public void OptedInContext_MapsTheFourTables()
    {
        IModel model = Model();
        IEntityType tasks = model.FindEntityType(typeof(TaskSchedule))!;
        IEntityType targets = tasks.FindNavigation(nameof(TaskSchedule.Targets))!.TargetEntityType;

        Assert.Equal(
            [
                ("tst", "task_schedules"),
                ("tst", "task_schedule_targets"),
                ("tst", "task_runs"),
                ("tst", "task_run_items"),
            ],
            [
                .. new[] { tasks, targets, model.FindEntityType(typeof(TaskRun))!, model.FindEntityType(typeof(TaskRunItem))! }
                    .Select(entityType => (entityType.GetSchema(), entityType.GetTableName())),
            ]);

        string[] columns = [.. tasks.GetProperties().Select(property => property.GetColumnName(StoreObjectIdentifier.Table("task_schedules", "tst"))!)];

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

        Assert.Equal("jsonb", model.FindEntityType(typeof(TaskSchedule))!.FindProperty(nameof(TaskSchedule.Arguments))!.GetColumnType());
        Assert.Equal("jsonb", model.FindEntityType(typeof(TaskRun))!.FindProperty(nameof(TaskRun.Arguments))!.GetColumnType());
    }

    [Fact]
    public void TargetIndex_Exists()
    {
        IModel model = Model();
        IEntityType targets = model.FindEntityType(typeof(TaskSchedule))!.FindNavigation(nameof(TaskSchedule.Targets))!.TargetEntityType;
        IEntityType runs = model.FindEntityType(typeof(TaskRun))!;

        Assert.Contains(targets.GetIndexes(), index => Names(index) == nameof(TaskScheduleTarget.TargetId));
        Assert.Equal(["ScheduleId", nameof(TaskScheduleTarget.TargetId)], [.. targets.FindPrimaryKey()!.Properties.Select(property => property.Name)]);
        Assert.Contains(
            runs.GetIndexes(),
            index => Names(index) == $"{nameof(TaskRun.ScheduleId)},{nameof(TaskRun.StartedAtUtc)}");

        // The schedules section's one-off runs read: a run item over the target, joined to its run.
        Assert.Contains(
            model.FindEntityType(typeof(TaskRunItem))!.GetIndexes(),
            index => Names(index) == $"{nameof(TaskRunItem.TargetId)},{nameof(TaskRunItem.RunId)}");
    }

    [Fact]
    public void RunItem_TargetLabel_IsNullableText_AndRunsHaveTheActivityIndex()
    {
        IModel model = DesignModel();
        IProperty label = model.FindEntityType(typeof(TaskRunItem))!.FindProperty(nameof(TaskRunItem.TargetLabel))!;

        Assert.True(label.IsNullable);
        Assert.Equal(typeof(string), label.ClrType);

        IIndex index = Assert.Single(
            model.FindEntityType(typeof(TaskRun))!.GetIndexes(),
            candidate => Names(candidate) == $"{nameof(TaskRun.ActingUserId)},{nameof(TaskRun.StartedAtUtc)}");
        Assert.Equal([false, true], index.IsDescending);
    }

    [Fact]
    public void Run_CarriesTheCancelColumns()
    {
        IEntityType runs = DesignModel().FindEntityType(typeof(TaskRun))!;
        var table = StoreObjectIdentifier.Table("task_runs", "tst");
        IProperty requested = runs.FindProperty(nameof(TaskRun.CancelRequestedAtUtc))!;
        IProperty cancelled = runs.FindProperty(nameof(TaskRun.Cancelled))!;

        Assert.Equal("cancel_requested_at_utc", requested.GetColumnName(table));
        Assert.True(requested.IsNullable);
        Assert.Equal("cancelled", cancelled.GetColumnName(table));
        Assert.False(cancelled.IsNullable);
        Assert.Equal(0, cancelled.GetDefaultValue());
    }

    [Fact]
    public void RunItem_CarriesTheProgressColumn()
    {
        IEntityType items = DesignModel().FindEntityType(typeof(TaskRunItem))!;
        IProperty progress = items.FindProperty(nameof(TaskRunItem.Progress))!;

        Assert.Equal("progress", progress.GetColumnName(StoreObjectIdentifier.Table("task_run_items", "tst")));
        Assert.Equal("smallint", progress.GetColumnType());
        Assert.True(progress.IsNullable);
    }

    [Fact]
    public void OneRunningRunPerTask_IsAPartialUniqueIndex()
    {
        IEntityType runs = Model().FindEntityType(typeof(TaskRun))!;

        IIndex index = Assert.Single(runs.GetIndexes(), one => one.GetDatabaseName() == "ix_task_runs_schedule_id_running");
        Assert.True(index.IsUnique);
        Assert.Equal(nameof(TaskRun.ScheduleId), Names(index));
        Assert.Equal("status = 'Running'", index.GetFilter());
    }

    [Fact]
    public void OneRunningCollectionRunPerNameAndScope_IsAPartialUniqueIndex()
    {
        IEntityType runs = Model().FindEntityType(typeof(TaskRun))!;

        Assert.Equal(2, runs.GetIndexes().Count(one => one.IsUnique));
        IIndex index = Assert.Single(runs.GetIndexes(), one => one.GetDatabaseName() == "ix_task_runs_collection_running");
        Assert.True(index.IsUnique);
        Assert.Equal($"{nameof(TaskRun.CommandName)},{nameof(TaskRun.ScopeId)}", Names(index));
        Assert.Equal("status = 'Running' AND target_kind = 'Collection'", index.GetFilter());
    }

    [Fact]
    public void Run_TargetKind_IsARequiredName_AndItemTargetIdIsNullable_WithNoRootId()
    {
        IModel model = DesignModel();
        IProperty kind = model.FindEntityType(typeof(TaskRun))!.FindProperty(nameof(TaskRun.TargetKind))!;
        IEntityType items = model.FindEntityType(typeof(TaskRunItem))!;

        Assert.Equal("target_kind", kind.GetColumnName(StoreObjectIdentifier.Table("task_runs", "tst")));
        Assert.False(kind.IsNullable);
        Assert.Equal(typeof(string), kind.GetProviderClrType());
        Assert.True(items.FindProperty(nameof(TaskRunItem.TargetId))!.IsNullable);
        Assert.DoesNotContain(items.GetProperties(), property => property.Name == "RootId");
        Assert.DoesNotContain(model.FindEntityType(typeof(TaskRun))!.GetProperties(), property => property.Name == "RootId");
    }

    [Fact]
    public void NotOptedInContext_HasNoTables_AndNoRegistration()
    {
        using IServiceScope scope = host.Services.CreateScope();
        IModel userAccess = Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService<IUserAccessDbContext>()).Model;

        Assert.Null(userAccess.FindEntityType(typeof(TaskSchedule)));
        Assert.Null(userAccess.FindEntityType(typeof(TaskRun)));

        // DeviceManagement opted in itself; the test module is the second registration, and UserAccess has none.
        IReadOnlyCollection<TasksRegistration> registrations = [.. host.Services.GetServices<TasksRegistration>()];
        Assert.Equal(2, registrations.Count);
        Assert.Contains(new TasksRegistration("tst", typeof(TasksDbContext)), registrations);
        Assert.Contains(registrations, registration => registration.Schema == "dm");
    }

    [Fact]
    public void TwoOptedInContexts_ShareOneConfigurationSource()
    {
        var services = new ServiceCollection();
        IConfigurationSection sergin = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sergin:ConnectionStrings:Database"] = "Host=unused" })
            .Build()
            .GetSection("Sergin");

        services.AddModuleDbContext<TasksDbContext, ITasksTestDbContext, ITasksTestUnitOfWork>(sergin, "tst");
        services.AddModuleDbContext<TasksDbContext, ITasksTestDbContext, ITasksTestUnitOfWork>(sergin, "tst2");

        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(TasksRegistration)));
        Assert.Single(
            services,
            descriptor => descriptor.ImplementationInstance is ModuleConfigurationSource source
                && source.AggregateConfigurationTypes.Contains(typeof(TaskScheduleAggregateFeatureConfiguration)));
    }

    [Fact]
    public void PermissionCatalogue_HasManage()
    {
        PermissionCatalog catalog = host.Services.GetRequiredService<PermissionCatalog>();

        Assert.True(catalog.Contains(TaskPermissions.Manage));
    }

    [Fact]
    public void OptedInHost_RegistryDeclaresTheTaskScheduleFeatures()
    {
        AggregateFeatures features = host.Services.GetRequiredService<AggregateFeatureRegistry>().For(typeof(TaskSchedule));

        Assert.Equal(new AggregateFeatures(Audited: true, SoftDeletable: true, Versioned: true, Scoped: true), features);
    }

    [Fact]
    public async Task RealHost_StillStarts()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // DeviceManagement opts in, so the real host runs the scheduled-task services for it and knows the task's features.
        Assert.Equal("dm", Assert.Single(factory.Services.GetServices<TasksRegistration>()).Schema);
        Assert.Equal(
            new AggregateFeatures(Audited: true, SoftDeletable: true, Versioned: true, Scoped: true),
            factory.Services.GetRequiredService<AggregateFeatureRegistry>().For(typeof(TaskSchedule)));
    }

    [Fact]
    public async Task Task_Run_AndItems_RoundTrip()
    {
        var savedBy = new UserId(Guid.CreateVersion7());
        var target = new TaskScheduleTarget(Guid.CreateVersion7());
        var task = TaskSchedule.Create("tst.probes.touch", """{"Note":{"Value":"x"}}""", [target], "0 2 * * *", "Asia/Tehran", savedBy);
        task.LinkTrigger(Guid.CreateVersion7());

        var run = TaskRun.Start(
            new TaskRunDefinition(task.Id, TaskRunTrigger.ScheduleNow, savedBy, Guid.CreateVersion7(), "root", task.CommandName, task.Arguments),
            total: 1,
            startedAtUtc: DateTime.UtcNow);
        var item = TaskRunItem.Create(run.Id, target.TargetId, DateTime.UtcNow);
        item.MarkRunning(DateTime.UtcNow);
        item.MarkFailed("Code", "Description", DateTime.UtcNow);
        run.Complete(succeeded: 0, failed: 1, cancelled: 0, DateTime.UtcNow);

        using (IServiceScope scope = host.Services.CreateScope())
        {
            TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            context.TaskSchedules.Add(task);
            context.TaskRuns.Add(run);
            context.TaskRunItems.Add(item);
            await context.SaveChangesAsync();
        }

        using (IServiceScope scope = host.Services.CreateScope())
        {
            TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

            TaskSchedule stored = await context.TaskSchedules.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == task.Id);
            Assert.Equal([target], stored.Targets);
            Assert.Equal(TaskScheduleState.Active, stored.State);
            Assert.Equal(savedBy, stored.DelegatingUserId);
            Assert.Equal(task.EngineTriggerId, stored.EngineTriggerId);

            TaskRun storedRun = await context.TaskRuns.SingleAsync(candidate => candidate.Id == run.Id);
            Assert.Equal(
                (TaskRunStatus.Completed, 1, 0, 1, 0, (DateTime?)null),
                (storedRun.Status, storedRun.Total, storedRun.Succeeded, storedRun.Failed, storedRun.Cancelled, storedRun.CancelRequestedAtUtc));

            TaskRunItem storedItem = await context.TaskRunItems.SingleAsync(candidate => candidate.Id == item.Id);
            Assert.Equal((TaskRunItemStatus.Failed, 1, "Code"), (storedItem.Status, storedItem.Attempts, storedItem.ErrorCode));
        }
    }

    private static string Names(IIndex index) => string.Join(",", index.Properties.Select(property => property.Name));

    // Descending columns are design-time metadata, not kept in the runtime model.
    private IModel DesignModel()
    {
        using IServiceScope scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TasksDbContext>().GetService<IDesignTimeModel>().Model;
    }

    private IModel Model()
    {
        using IServiceScope scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TasksDbContext>().Model;
    }
}
