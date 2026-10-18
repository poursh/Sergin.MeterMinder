using ErrorOr;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// Requests and configurations for the registry tests. Never scanned by a host: hosts read module
/// ContractsAssemblies only, and a test host adds explicit types through CommandConfigurationSource.FromTypes,
/// so the deliberately broken configurations below cannot reach the real host.
/// </summary>
internal sealed record ConfiguredTestCommand : ICommand<Success>;

internal sealed record ConfiguredTestQuery : IQuery<Success>;

internal sealed record UnconfiguredTestCommand : ICommand<Success>;

internal sealed class ConfiguredTestCommandConfiguration : ICommandConfiguration<ConfiguredTestCommand>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder) =>
        builder.RequirePermissions("permission.test.things.read")
            .RequirePermissions("permission.test.things.update", "permission.test.things.delete")
            .RequireExpectedVersion();
}

internal sealed class ConfiguredTestQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions("permission.test.things.read");
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

internal sealed class MalformedPermissionConfiguration : ICommandConfiguration<ConfiguredTestQuery>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions("not a permission");
}

internal sealed class AtRootWithoutPermissionConfiguration : ICommandConfiguration<ConfiguredTestQuery>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.AtRoot();
}

internal sealed class AtRootQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions("permission.test.things.read").AtRoot();
}

internal abstract record AbstractTestCommand : ICommand<Success>;

internal sealed class AbstractRequestConfiguration : ICommandConfiguration<AbstractTestCommand>
{
    public void Configure(CommandConfigurationBuilder<AbstractTestCommand> builder) =>
        builder.RequirePermissions("permission.test.things.delete");
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
        builder.RequirePermissions("permission.test.things.update").RequireExpectedVersion();
}

internal sealed class GlobalBoundQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery, GlobalTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions("permission.test.things.read");
}

internal sealed class UnconfiguredRootBoundQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery, UnconfiguredTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions("permission.test.things.read");
}

internal sealed class GlobalBoundInCurrentScopeQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery, GlobalTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions("permission.test.things.read").InCurrentScope();
}

internal sealed class ScopedBoundAtRootQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery, ScopedTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions("permission.test.things.read").AtRoot();
}

internal sealed class InCurrentScopeWithoutPermissionConfiguration : ICommandConfiguration<ConfiguredTestQuery, GlobalTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.InCurrentScope();
}

internal sealed class VersionOnUnversionedRootConfiguration : ICommandConfiguration<ConfiguredTestCommand, GlobalTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder) =>
        builder.RequirePermissions("permission.test.things.update").RequireExpectedVersion();
}

// Both shapes for one request: the duplicate rule refuses it like two classes.
internal sealed class BothShapesConfiguration
    : ICommandConfiguration<ConfiguredTestQuery>, ICommandConfiguration<ConfiguredTestQuery, GlobalTestRoot>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions("permission.test.things.read");
}
