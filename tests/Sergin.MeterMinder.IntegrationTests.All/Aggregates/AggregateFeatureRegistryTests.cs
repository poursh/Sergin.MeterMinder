using Sergin.MeterMinder.IntegrationTests.All.Commands;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// The registry on its own: what a configuration class declares is what For() answers, an unconfigured
/// type answers None, and the two declaration mistakes the scan can see — two configurations for one type,
/// and a configuration that wants constructor arguments — fail with the offending type names.
/// </summary>
public sealed class AggregateFeatureRegistryTests
{
    [Fact]
    public void For_ConfiguredType_ReturnsDeclaredFeatures()
    {
        var registry =
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(UnmappedEntityAggregateFeatureConfiguration)]);

        Assert.True(registry.For(typeof(UnmappedEntity)).Audited);
        Assert.Equal(typeof(UnmappedEntity), Assert.Single(registry.ConfiguredTypes));
    }

    [Fact]
    public void SoftDeletable_ReachesEveryChild_EvenOneExceptedFromAudit()
    {
        var registry = AggregateFeatureRegistry.FromConfigurationTypes(
            [typeof(SoftDeletableUnmappedEntityAggregateFeatureConfiguration)]);

        Assert.Equal(
            new AggregateFeatures(Audited: true, SoftDeletable: true, Versioned: false),
            registry.For(typeof(UnmappedEntity)));
        Assert.Equal(
            new AggregateFeatures(Audited: false, SoftDeletable: true, Versioned: false),
            registry.ForChild(typeof(UnmappedEntity), typeof(UnmappedChild)));
    }

    [Fact]
    public void For_UnconfiguredType_ReturnsNone()
    {
        Assert.Equal(AggregateFeatures.None, AggregateFeatureRegistry.Empty.For(typeof(UnmappedEntity)));
    }

    [Fact]
    public void Configure_DeclaringNothing_StillRegistersTheTypeWithNoFeatures()
    {
        var registry =
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(NoFeaturesAggregateFeatureConfiguration)]);

        Assert.Equal(AggregateFeatures.None, registry.For(typeof(UnmappedEntity)));
    }

    [Fact]
    public void TwoConfigurationsForOneType_Throw_NamingBoth()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            AggregateFeatureRegistry.FromConfigurationTypes(
                [typeof(UnmappedEntityAggregateFeatureConfiguration), typeof(DuplicateUnmappedEntityAggregateFeatureConfiguration)]));

        Assert.Contains(typeof(UnmappedEntity).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(UnmappedEntityAggregateFeatureConfiguration), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DuplicateUnmappedEntityAggregateFeatureConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationWithConstructorArguments_Throws_NamingIt()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(NeedsServiceAggregateFeatureConfiguration)]));

        Assert.Contains(nameof(NeedsServiceAggregateFeatureConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromAssemblies_FindsConfigurationsInTheAssembly()
    {
        // The test assembly holds a duplicate on purpose, so scanning it must refuse — which proves the
        // scan found both classes without the test depending on their exact count.
        Assert.Throws<InvalidOperationException>(() =>
            AggregateFeatureRegistry.FromAssemblies([typeof(AggregateFeatureRegistryTests).Assembly]));
    }

    [Fact]
    public void ConfigurationTypesIn_ListsOnlyAggregateFeatureConfigurations()
    {
        IReadOnlyCollection<Type> types =
            AggregateFeatureRegistry.ConfigurationTypesIn(typeof(AggregateFeatureRegistryTests).Assembly);

        Assert.Contains(typeof(UnmappedEntityAggregateFeatureConfiguration), types);
        Assert.DoesNotContain(typeof(ConfiguredTestCommandConfiguration), types);
    }
}
