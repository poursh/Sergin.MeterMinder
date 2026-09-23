using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Audit;

/// <summary>
/// EF annotations are not inherited, so a TPH hierarchy whose base type alone is configured Audited()
/// leaves the derived type's own metadata carrying no Sergin:Audited annotation, even though it shares the
/// base's audit shadow columns through the same table. AuditColumns.IsAudited must walk the BaseType chain
/// to see that. No database connection is needed: UseNpgsql("Host=unused") only lets SerginDbContext build
/// its .Model, which is enough to exercise the real AggregateFeatureConvention.
/// </summary>
public sealed class AuditColumnsInheritanceTests
{
    [Fact]
    public void DerivedTypeOfAuditedBase_IsReportedAsAudited()
    {
        using InheritanceTestDbContext context = new(BuildOptions());

        IEntityType derived = Assert.IsAssignableFrom<IEntityType>(context.Model.FindEntityType(typeof(DerivedThing)));

        Assert.True(AuditColumns.IsAudited(derived));
    }

    private static DbContextOptions<InheritanceTestDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<InheritanceTestDbContext>().UseNpgsql("Host=unused").Options;
}

internal sealed class InheritanceTestDbContext(DbContextOptions<InheritanceTestDbContext> options)
    : SerginDbContext(options)
{
    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes([typeof(BaseThingAggregateConfiguration)]);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseThing>(thing =>
        {
            thing.ToTable("base_things");
            thing.HasKey(x => x.Id);
            thing.HasDiscriminator<string>("Kind")
                .HasValue<BaseThing>("base")
                .HasValue<DerivedThing>("derived");
        });

        base.OnModelCreating(modelBuilder);
    }
}

internal class BaseThing : AggregateRoot<Guid>
{
    protected BaseThing()
    {
    }
}

internal sealed class DerivedThing : BaseThing
{
    private DerivedThing()
    {
    }

    // Referenced only so Sonar sees the private constructor used; EF materializes rows through it via
    // reflection, invisible to static analysis, and this test only inspects the model, never a row.
    public static DerivedThing Create() => new();
}

internal sealed class BaseThingAggregateConfiguration : IAggregateConfiguration<BaseThing>
{
    public void Configure(AggregateFeatureBuilder<BaseThing> builder) => builder.Audited();
}
