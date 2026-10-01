using ErrorOr;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;

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
