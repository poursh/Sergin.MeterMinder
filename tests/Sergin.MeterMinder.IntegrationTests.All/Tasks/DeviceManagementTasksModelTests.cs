using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Tasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>DeviceManagement, the first real module to opt in: the four scheduled-task tables live in schema <c>dm</c>.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceManagementTasksModelTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void DmModel_CarriesTheTaskTables()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IModel model = Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>()).Model;

        foreach ((Type type, string table) in new[]
        {
            (typeof(TaskSchedule), "task_schedules"),
            (typeof(TaskRun), "task_runs"),
            (typeof(TaskRunItem), "task_run_items"),
        })
        {
            IEntityType entity = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(type));
            Assert.Equal("dm", entity.GetSchema());
            Assert.Equal(table, entity.GetTableName());
        }

        IIndex running = Assert.Single(
            model.FindEntityType(typeof(TaskRun))!.GetIndexes(), index => index.GetDatabaseName() == "ix_task_runs_schedule_id_running");
        Assert.Contains("'Running'", running.GetFilter(), StringComparison.Ordinal);
    }

    [Fact]
    public void DmModel_CarriesTheTargetLabelAndTheActivityIndex()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IModel model = Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>()).GetService<IDesignTimeModel>().Model;
        IEntityType items = model.FindEntityType(typeof(TaskRunItem))!;
        IProperty label = items.FindProperty(nameof(TaskRunItem.TargetLabel))!;

        Assert.Equal("target_label", label.GetColumnName(StoreObjectIdentifier.Table("task_run_items", "dm")));
        Assert.True(label.IsNullable);

        IIndex index = Assert.Single(
            model.FindEntityType(typeof(TaskRun))!.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ix_task_runs_acting_user_id_started_at_utc");
        Assert.Equal(["acting_user_id", "started_at_utc"], [.. index.Properties.Select(property => property.GetColumnName())]);
        Assert.Equal([false, true], index.IsDescending);
    }

    [Fact]
    public void DmModel_CarriesTheCancelColumns()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IModel model = Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>()).GetService<IDesignTimeModel>().Model;
        IEntityType runs = model.FindEntityType(typeof(TaskRun))!;
        var table = StoreObjectIdentifier.Table("task_runs", "dm");
        IProperty requested = runs.FindProperty(nameof(TaskRun.CancelRequestedAtUtc))!;
        IProperty cancelled = runs.FindProperty(nameof(TaskRun.Cancelled))!;

        Assert.Equal("cancel_requested_at_utc", requested.GetColumnName(table));
        Assert.Equal("timestamp with time zone", requested.GetColumnType());
        Assert.True(requested.IsNullable);
        Assert.Equal("cancelled", cancelled.GetColumnName(table));
        Assert.Equal("integer", cancelled.GetColumnType());
        Assert.False(cancelled.IsNullable);
        Assert.Equal(0, cancelled.GetDefaultValue());
    }

    [Fact]
    public void DmModel_CarriesTheProgressColumn()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IModel model = Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>()).GetService<IDesignTimeModel>().Model;
        IProperty progress = model.FindEntityType(typeof(TaskRunItem))!.FindProperty(nameof(TaskRunItem.Progress))!;

        Assert.Equal("progress", progress.GetColumnName(StoreObjectIdentifier.Table("task_run_items", "dm")));
        Assert.Equal("smallint", progress.GetColumnType());
        Assert.True(progress.IsNullable);
    }

    [Fact]
    public void DmContext_IsATasksContext()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        Assert.IsAssignableFrom<ITasksDbContext>(scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>());
    }
}
