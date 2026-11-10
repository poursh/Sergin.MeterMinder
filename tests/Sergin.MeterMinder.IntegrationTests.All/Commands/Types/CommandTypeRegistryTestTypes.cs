using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands.Types;

/// <summary>
/// Feature configurations for <see cref="TestTypedRoot"/>, one per shape a test needs; a test passes exactly one,
/// since the registry refuses two configurations for one root. No context maps the root.
/// </summary>
internal static class TestTypedTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("DeviceManagement", "CommandTypeRegistryTests");
}

internal sealed class VersionedTypedRootConfiguration : IAggregateFeatureConfiguration<TestTypedRoot>
{
    public void Configure(AggregateFeatureBuilder<TestTypedRoot> builder) => builder.Versioned().Texts(TestTypedTexts.Category);
}

internal sealed class UnversionedTypedRootConfiguration : IAggregateFeatureConfiguration<TestTypedRoot>
{
    public void Configure(AggregateFeatureBuilder<TestTypedRoot> builder) => builder.Audited();
}

internal sealed class ScopedTypedRootConfiguration : IAggregateFeatureConfiguration<TestTypedRoot>
{
    public void Configure(AggregateFeatureBuilder<TestTypedRoot> builder) => builder.Versioned().Scoped();
}

/// <summary>A typed record with a configuration: its permissions come from here, its root from the type.</summary>
internal sealed class PermissionedDeleteConfiguration : ICommandConfiguration<TestDeleteCommand>
{
    public void Configure(CommandConfigurationBuilder<TestDeleteCommand> builder) =>
        builder.RequirePermissions(TestThingPermissions.Delete);
}

internal sealed class WaivedDeleteConfiguration : ICommandConfiguration<TestDeleteCommand>
{
    public void Configure(CommandConfigurationBuilder<TestDeleteCommand> builder) => builder.WithoutExpectedVersion();
}

internal sealed class WaivedCreateConfiguration : ICommandConfiguration<TestCreateCommand>
{
    public void Configure(CommandConfigurationBuilder<TestCreateCommand> builder) => builder.WithoutExpectedVersion();
}

internal sealed class WaivedQueryConfiguration : ICommandConfiguration<TestGetOneQuery>
{
    public void Configure(CommandConfigurationBuilder<TestGetOneQuery> builder) => builder.WithoutExpectedVersion();
}

internal sealed class WaivedUntypedConfiguration : ICommandConfiguration<TestUntypedCommand>
{
    public void Configure(CommandConfigurationBuilder<TestUntypedCommand> builder) => builder.WithoutExpectedVersion();
}
