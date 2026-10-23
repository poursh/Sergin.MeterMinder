using ErrorOr;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// Requests and configurations for the registry tests. Never scanned by a host: hosts read module
/// ContractsAssemblies only, and a test host adds explicit types through ModuleConfigurationSource.FromTypes,
/// so the deliberately broken configurations below cannot reach the real host.
/// </summary>
internal sealed record ConfiguredTestCommand : ICommand<Success>;

/// <summary>The permissions the test configurations require, registered by <see cref="TestThingRoot"/>.</summary>
internal static class TestThingPermissions
{
    public static PermissionDefinition Read { get; } =
        PermissionDefinition.Create("permission.test.things.read", "View things");

    public static PermissionDefinition Update { get; } =
        PermissionDefinition.Create("permission.test.things.update", "Change things");

    public static PermissionDefinition Delete { get; } =
        PermissionDefinition.Create("permission.test.things.delete", "Delete things");
}

/// <summary>Registers <see cref="TestThingPermissions"/>; passed alongside any configuration that requires them.</summary>
internal sealed class TestThingRoot : AggregateRoot<Guid>;

internal sealed class TestThingRootConfiguration : IAggregateFeatureConfiguration<TestThingRoot>
{
    public void Configure(AggregateFeatureBuilder<TestThingRoot> builder) =>
        builder.Permissions(TestThingPermissions.Read, TestThingPermissions.Update, TestThingPermissions.Delete);
}

internal sealed record ConfiguredTestQuery : IQuery<Success>;

internal sealed record UnconfiguredTestCommand : ICommand<Success>;

internal sealed class ConfiguredTestCommandConfiguration : ICommandConfiguration<ConfiguredTestCommand>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder) =>
        builder.RequirePermissions(TestThingPermissions.Read)
            .RequirePermissions(TestThingPermissions.Update, TestThingPermissions.Delete)
            .RequireExpectedVersion();
}

internal sealed class ConfiguredTestQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions(TestThingPermissions.Read);
}

internal sealed class DuplicateConfiguredTestCommandConfiguration : ICommandConfiguration<ConfiguredTestCommand>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder) =>
        builder.RequireExpectedVersion();
}

internal sealed class NeedsServiceCommandConfiguration(IServiceProvider services) : ICommandConfiguration<ConfiguredTestCommand>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder)
    {
        ArgumentNullException.ThrowIfNull(services);
        builder.RequireExpectedVersion();
    }
}

internal sealed class VersionedQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequireExpectedVersion();
}

internal sealed class AllPlatformTestQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions(PermissionDefinition.AllPlatform);
}

internal abstract record AbstractTestCommand : ICommand<Success>;

internal sealed class AbstractRequestConfiguration : ICommandConfiguration<AbstractTestCommand>
{
    public void Configure(CommandConfigurationBuilder<AbstractTestCommand> builder) =>
        builder.RequirePermissions(TestThingPermissions.Delete);
}

// Aggregate-bound configurations. No context maps these roots: the registry reads only their feature
// configurations, passed alongside in the same FromConfigurationTypes list.
internal sealed class ScopedTestRoot : AggregateRoot<Guid>;

internal sealed class GlobalTestRoot : AggregateRoot<Guid>;

internal sealed class UnconfiguredTestRoot : AggregateRoot<Guid>;

internal sealed class ScopedTestRootFeatureConfiguration : IAggregateFeatureConfiguration<ScopedTestRoot>
{
    public void Configure(AggregateFeatureBuilder<ScopedTestRoot> builder) => builder.Versioned().Scoped();
}

internal sealed class GlobalTestRootFeatureConfiguration : IAggregateFeatureConfiguration<GlobalTestRoot>
{
    public void Configure(AggregateFeatureBuilder<GlobalTestRoot> builder) => builder.Audited();
}

internal sealed class ScopedBoundCommandConfiguration : ICommandConfiguration<ConfiguredTestCommand, ScopedTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder) =>
        builder.RequirePermissions(TestThingPermissions.Update).RequireExpectedVersion();
}

internal sealed class GlobalBoundQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery, GlobalTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions(TestThingPermissions.Read);
}

internal sealed class UnconfiguredRootBoundQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery, UnconfiguredTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions(TestThingPermissions.Read);
}

internal sealed class VersionOnUnversionedRootConfiguration : ICommandConfiguration<ConfiguredTestCommand, GlobalTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder) =>
        builder.RequirePermissions(TestThingPermissions.Update).RequireExpectedVersion();
}

// Both shapes for one request: the duplicate rule refuses it like two classes.
internal sealed class BothShapesConfiguration
    : ICommandConfiguration<ConfiguredTestQuery>, ICommandConfiguration<ConfiguredTestQuery, GlobalTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions(TestThingPermissions.Read);
}
