using Microsoft.EntityFrameworkCore;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Tasks;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;

public interface IDeviceManagementDbContext : IDbContext;

/// <summary>
/// Opts into the outbox (<see cref="IOutboxDbContext"/>): the <c>outbox_messages</c> and <c>inbox_messages</c>
/// tables live in this module's schema, added by the <c>AddOutbox</c> migration, so an integration event this
/// module raises rides on the same save as the aggregate that raised it, and a consumer here dedups through
/// its own inbox. No translator, event or handler is declared yet — the tables are ready for the first one.
/// It also opts into scheduled tasks (<see cref="ITasksDbContext"/>): the four scheduled-task tables live in
/// this module's schema, added by the <c>ReplaceScheduledTasksWithTasks</c> migration, and the aggregate features include
/// SharedKernel's scheduled-task configurations, as an opting-in context must.
/// </summary>
internal sealed class DeviceManagementDbContext(DbContextOptions<DeviceManagementDbContext> options)
    : SerginDbContext(options), IDeviceManagementDbContext, IDeviceManagementUnitOfWork, IOutboxDbContext, ITasksDbContext
{
    public const string Schema = "dm";

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<TaskSchedule> TaskSchedules => Set<TaskSchedule>();

    public DbSet<TaskRun> TaskRuns => Set<TaskRun>();

    public DbSet<TaskRunItem> TaskRunItems => Set<TaskRunItem>();

    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry
            .FromAssemblies([DeviceManagementApplicationConfigurationsAssemblyReference.Assembly])
            .Including(TaskConfigurationTypes.AggregateConfigurations);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        modelBuilder.ApplyOutbox();
        modelBuilder.ApplyTasks();

        base.OnModelCreating(modelBuilder);
    }
}
