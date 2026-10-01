using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// The registry on its own: what a configuration declares is what For() answers, an unconfigured request
/// answers None, and each declaration mistake the scan can see fails naming the offending type.
/// </summary>
public sealed class CommandConfigurationRegistryTests
{
    [Fact]
    public void For_ConfiguredCommand_ReturnsDeclaredSettings_WithPermissionsAppended()
    {
        var registry = CommandConfigurationRegistry.FromConfigurationTypes([typeof(ConfiguredTestCommandConfiguration)]);

        CommandSettings settings = registry.For(typeof(ConfiguredTestCommand));

        string[] expected = ["permission.test.things.read", "permission.test.things.update", "permission.test.things.delete"];

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal(expected, settings.RequiredPermissions.Select(permission => permission.Value));
        Assert.Equal(typeof(ConfiguredTestCommand), Assert.Single(registry.ConfiguredTypes));
    }

    [Fact]
    public void For_ConfiguredQuery_ReturnsItsPermission_AndNoVersion()
    {
        var registry = CommandConfigurationRegistry.FromConfigurationTypes([typeof(ConfiguredTestQueryConfiguration)]);

        CommandSettings settings = registry.For(typeof(ConfiguredTestQuery));

        Assert.False(settings.RequiresExpectedVersion);
        Assert.Equal((Permission)"permission.test.things.read", Assert.Single(settings.RequiredPermissions));
    }

    [Fact]
    public void For_UnconfiguredRequest_ReturnsNone()
    {
        var registry = CommandConfigurationRegistry.FromConfigurationTypes([typeof(ConfiguredTestCommandConfiguration)]);

        Assert.Same(CommandSettings.None, registry.For(typeof(UnconfiguredTestCommand)));
        Assert.Same(CommandSettings.None, CommandConfigurationRegistry.Empty.For(typeof(ConfiguredTestCommand)));
    }

    [Fact]
    public void TwoConfigurationsForOneRequest_Throw_NamingBoth()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes(
                [typeof(ConfiguredTestCommandConfiguration), typeof(DuplicateConfiguredTestCommandConfiguration)]));

        Assert.Contains(typeof(ConfiguredTestCommand).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ConfiguredTestCommandConfiguration), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DuplicateConfiguredTestCommandConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationWithConstructorArguments_Throws_NamingIt()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes([typeof(NeedsServiceCommandConfiguration)]));

        Assert.Contains(nameof(NeedsServiceCommandConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedVersionOnAQuery_Throws_NamingTheQueryAndConfiguration()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes([typeof(VersionedQueryConfiguration)]));

        Assert.Contains(typeof(ConfiguredTestQuery).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(VersionedQueryConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedPermission_Throws_NamingTheConfiguration_WithTheCauseInside()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes([typeof(MalformedPermissionConfiguration)]));

        Assert.Contains(nameof(MalformedPermissionConfiguration), error.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
    }

    [Fact]
    public void FromSources_MergesEverySource()
    {
        var registry = CommandConfigurationRegistry.FromSources(
        [
            CommandConfigurationSource.FromTypes(typeof(ConfiguredTestCommandConfiguration)),
            CommandConfigurationSource.FromTypes(typeof(ConfiguredTestQueryConfiguration)),
        ]);

        Assert.Equal(2, registry.ConfiguredTypes.Count);
    }

    [Fact]
    public void FromAssembly_FindsConfigurationsInTheAssembly()
    {
        var source = CommandConfigurationSource.FromAssembly(typeof(CommandConfigurationRegistryTests).Assembly);

        Assert.Contains(typeof(ConfiguredTestCommandConfiguration), source.ConfigurationTypes);
        Assert.Contains(typeof(MalformedPermissionConfiguration), source.ConfigurationTypes);
        Assert.DoesNotContain(typeof(ConfiguredTestCommand), source.ConfigurationTypes);
    }
}
