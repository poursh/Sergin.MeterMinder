using ErrorOr;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
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
            .RequirePermissions(TestThingPermissions.Update, TestThingPermissions.Delete);
}

internal sealed class ConfiguredTestQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions(TestThingPermissions.Read);
}

internal sealed class DuplicateConfiguredTestCommandConfiguration : ICommandConfiguration<ConfiguredTestCommand>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder) =>
        builder.RequirePermissions(TestThingPermissions.Read);
}

internal sealed class NeedsServiceCommandConfiguration(IServiceProvider services) : ICommandConfiguration<ConfiguredTestCommand>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder)
    {
        ArgumentNullException.ThrowIfNull(services);
        builder.RequirePermissions(PermissionDefinition.AllPlatform);
    }
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

// Typed requests on test roots: the registry reads their root from the type and the root's features from the feature
// configurations passed alongside in the same FromConfigurationTypes list. No context maps these roots.
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

internal sealed record ScopedTestDeleteCommand(Guid Id) : IDeleteCommand<ScopedTestRoot, Guid>, ICommand<Deleted>;

internal sealed record GlobalTestGetOneQuery(Guid Id) : IGetOneQuery<GlobalTestRoot, Guid>, IQuery<Success>;

internal sealed record UnconfiguredRootGetOneQuery(Guid Id) : IGetOneQuery<UnconfiguredTestRoot, Guid>, IQuery<Success>;

internal sealed class ScopedTestDeleteCommandConfiguration : ICommandConfiguration<ScopedTestDeleteCommand>
{
    public void Configure(CommandConfigurationBuilder<ScopedTestDeleteCommand> builder) =>
        builder.RequirePermissions(TestThingPermissions.Update);
}

internal sealed class GlobalTestGetOneQueryConfiguration : ICommandConfiguration<GlobalTestGetOneQuery>
{
    public void Configure(CommandConfigurationBuilder<GlobalTestGetOneQuery> builder) =>
        builder.RequirePermissions(TestThingPermissions.Read);
}

internal sealed class UnconfiguredRootGetOneQueryConfiguration : ICommandConfiguration<UnconfiguredRootGetOneQuery>
{
    public void Configure(CommandConfigurationBuilder<UnconfiguredRootGetOneQuery> builder) =>
        builder.RequirePermissions(TestThingPermissions.Read);
}
