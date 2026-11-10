using Microsoft.EntityFrameworkCore;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.ScheduledTasks;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

internal interface IScheduledTasksTestDbContext : IDbContext;

internal interface IScheduledTasksTestUnitOfWork : IUnitOfWork;

/// <summary>
/// A test-only module that opts into scheduled tasks: the probe aggregate and its child, plus the four scheduled-task
/// tables, in schema <c>tst</c>, through the same <c>AddModuleDbContext</c> the real modules use. Its aggregate features
/// apply the probe's configuration and SharedKernel's <see cref="ScheduledTaskAggregateFeatureConfiguration"/>, as an
/// opting-in module context must.
/// </summary>
internal sealed class ScheduledTasksDbContext(DbContextOptions<ScheduledTasksDbContext> options)
    : SerginDbContext(options), IScheduledTasksTestDbContext, IScheduledTasksTestUnitOfWork, IScheduledTasksDbContext
{
    public const string Schema = "tst";

    public DbSet<ScheduleProbe> Probes => Set<ScheduleProbe>();

    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();

    public DbSet<ScheduledTaskRun> ScheduledTaskRuns => Set<ScheduledTaskRun>();

    public DbSet<ScheduledTaskRunItem> ScheduledTaskRunItems => Set<ScheduledTaskRunItem>();

    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes(
            [typeof(ScheduleProbeAggregateFeatureConfiguration), .. ScheduledTaskConfigurationTypes.AggregateConfigurations]);

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

        modelBuilder.ApplyScheduledTasks();

        base.OnModelCreating(modelBuilder);
    }
}
