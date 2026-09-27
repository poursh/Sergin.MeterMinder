using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// An entity no DbContext maps, used by the registry and guard tests. Never scanned by a host: the
/// registry is only ever built from a module's ApplicationAssembly, not from this test assembly, so
/// the deliberately broken configurations below cannot reach the real host.
/// </summary>
internal sealed class UnmappedEntity : AggregateRoot<Guid>;

internal sealed class UnmappedChild : Entity<Guid>;

internal sealed class UnmappedEntityAggregateFeatureConfiguration : IAggregateFeatureConfiguration<UnmappedEntity>
{
    public void Configure(AggregateFeatureBuilder<UnmappedEntity> builder) => builder.Audited();
}

internal sealed class DuplicateUnmappedEntityAggregateFeatureConfiguration : IAggregateFeatureConfiguration<UnmappedEntity>
{
    public void Configure(AggregateFeatureBuilder<UnmappedEntity> builder) => builder.Audited();
}

internal sealed class NeedsServiceAggregateFeatureConfiguration(IServiceProvider services) : IAggregateFeatureConfiguration<UnmappedEntity>
{
    public void Configure(AggregateFeatureBuilder<UnmappedEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(services);
        builder.Audited();
    }
}

internal sealed class SoftDeletableUnmappedEntityAggregateFeatureConfiguration : IAggregateFeatureConfiguration<UnmappedEntity>
{
    public void Configure(AggregateFeatureBuilder<UnmappedEntity> builder) =>
        builder.Audited(audit => audit.ExceptChild<UnmappedChild>()).SoftDeletable();
}

internal sealed class NoFeaturesAggregateFeatureConfiguration : IAggregateFeatureConfiguration<UnmappedEntity>
{
    public void Configure(AggregateFeatureBuilder<UnmappedEntity> builder)
    {
    }
}
