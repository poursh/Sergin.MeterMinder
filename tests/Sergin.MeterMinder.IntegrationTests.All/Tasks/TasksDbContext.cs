using Microsoft.EntityFrameworkCore;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Tasks;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

internal interface ITasksTestDbContext : IDbContext;

internal interface ITasksTestUnitOfWork : IUnitOfWork;

/// <summary>
/// A test-only module that opts into tasks: the probe aggregate and its child, plus the four task
/// tables, in schema <c>tst</c>, through the same <c>AddModuleDbContext</c> the real modules use. Its aggregate features
/// apply the probe's configuration and SharedKernel's <see cref="TaskScheduleAggregateFeatureConfiguration"/>, as an
/// opting-in module context must.
/// </summary>
internal sealed class TasksDbContext(DbContextOptions<TasksDbContext> options)
    : SerginDbContext(options), ITasksTestDbContext, ITasksTestUnitOfWork, ITasksDbContext
{
    public const string Schema = "tst";

    public DbSet<ScheduleProbe> Probes => Set<ScheduleProbe>();

    public DbSet<TaskSchedule> TaskSchedules => Set<TaskSchedule>();

    public DbSet<TaskRun> TaskRuns => Set<TaskRun>();

    public DbSet<TaskRunItem> TaskRunItems => Set<TaskRunItem>();

    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes(
            [typeof(ScheduleProbeAggregateFeatureConfiguration), .. TaskConfigurationTypes.AggregateConfigurations]);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ScheduleProbe>(probe =>
        {
            probe.ToTable("probes");
            probe.HasKey(x => x.Id);
            probe.Property(x => x.Id).HasConversion(id => id.Value, value => new ScheduleProbeId(value));
            probe.HasMany(x => x.Children).WithOne().HasForeignKey("ProbeId").OnDelete(DeleteBehavior.Cascade);
            probe.Navigation(x => x.Children).HasField("children");
        });

        modelBuilder.Entity<ScheduleProbeChild>(child =>
        {
            child.ToTable("probe_children");
            child.HasKey(x => x.Id);
            child.Property(x => x.Id).HasConversion(id => id.Value, value => new ScheduleProbeChildId(value));
        });

        modelBuilder.ApplyTasks();

        base.OnModelCreating(modelBuilder);
    }
}
