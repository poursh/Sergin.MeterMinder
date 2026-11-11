using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>DeviceManagement, the first real module to opt in: the four scheduled-task tables live in schema <c>dm</c>.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceManagementScheduledTasksModelTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void DmModel_CarriesTheScheduledTaskTables()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IModel model = Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>()).Model;

        foreach ((Type type, string table) in new[]
        {
            (typeof(ScheduledTask), "scheduled_tasks"),
            (typeof(ScheduledTaskRun), "scheduled_task_runs"),
            (typeof(ScheduledTaskRunItem), "scheduled_task_run_items"),
        })
        {
            IEntityType entity = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(type));
            Assert.Equal("dm", entity.GetSchema());
            Assert.Equal(table, entity.GetTableName());
        }

        IIndex running = Assert.Single(
            model.FindEntityType(typeof(ScheduledTaskRun))!.GetIndexes(), index => index.GetDatabaseName() == "ix_scheduled_task_runs_task_id_running");
        Assert.Contains("'Running'", running.GetFilter(), StringComparison.Ordinal);
    }

    [Fact]
    public void DmContext_IsAScheduledTasksContext()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        Assert.IsAssignableFrom<IScheduledTasksDbContext>(scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>());
    }
}
