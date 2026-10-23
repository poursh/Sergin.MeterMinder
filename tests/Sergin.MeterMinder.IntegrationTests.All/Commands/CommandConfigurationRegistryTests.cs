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
        var registry = CommandConfigurationRegistry.FromConfigurationTypes([typeof(ConfiguredTestCommandConfiguration), typeof(TestThingRootConfiguration)]);

        CommandSettings settings = registry.For(typeof(ConfiguredTestCommand));

        string[] expected = ["permission.test.things.read", "permission.test.things.update", "permission.test.things.delete"];

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal(expected, settings.RequiredPermissions.Select(permission => permission.Permission.Value));
        Assert.Equal(typeof(ConfiguredTestCommand), Assert.Single(registry.ConfiguredTypes));
    }

    [Fact]
    public void For_ConfiguredQuery_ReturnsItsPermission_AndNoVersion()
    {
        var registry = CommandConfigurationRegistry.FromConfigurationTypes([typeof(ConfiguredTestQueryConfiguration), typeof(TestThingRootConfiguration)]);

        CommandSettings settings = registry.For(typeof(ConfiguredTestQuery));

        Assert.False(settings.RequiresExpectedVersion);
        Assert.Equal(TestThingPermissions.Read, Assert.Single(settings.RequiredPermissions));
    }

    [Fact]
    public void For_UnconfiguredRequest_ReturnsNone()
    {
        var registry = CommandConfigurationRegistry.FromConfigurationTypes([typeof(ConfiguredTestCommandConfiguration), typeof(TestThingRootConfiguration)]);

        Assert.Same(CommandSettings.None, registry.For(typeof(UnconfiguredTestCommand)));
        Assert.Same(CommandSettings.None, CommandConfigurationRegistry.Empty.For(typeof(ConfiguredTestCommand)));
    }

    [Fact]
    public void TwoConfigurationsForOneRequest_Throw_NamingBoth()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes(
                [typeof(ConfiguredTestCommandConfiguration), typeof(DuplicateConfiguredTestCommandConfiguration), typeof(TestThingRootConfiguration)]));

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
    public void BoundToAScopedRoot_IsCheckedInTheCurrentScope_AndCarriesTheRoot()
    {
        CommandSettings settings = CommandConfigurationRegistry.FromConfigurationTypes(
            [typeof(ScopedBoundCommandConfiguration), typeof(ScopedTestRootFeatureConfiguration), typeof(TestThingRootConfiguration)])
            .For(typeof(ConfiguredTestCommand));

        Assert.Equal(PermissionScope.Current, settings.PermissionScope);
        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal(typeof(ScopedTestRoot), settings.AggregateRoot);
    }

    [Theory]
    [InlineData(typeof(GlobalBoundQueryConfiguration))]
    [InlineData(typeof(UnconfiguredRootBoundQueryConfiguration))]
    public void BoundToAnUnscopedRoot_IsCheckedOnAnyScope(Type configuration)
    {
        CommandSettings settings = CommandConfigurationRegistry.FromConfigurationTypes(
            [configuration, typeof(GlobalTestRootFeatureConfiguration), typeof(TestThingRootConfiguration)])
            .For(typeof(ConfiguredTestQuery));

        Assert.Equal(PermissionScope.Any, settings.PermissionScope);
    }

    [Fact]
    public void Unbound_IsCheckedInTheCurrentScope_WithNoRoot()
    {
        CommandSettings settings = CommandConfigurationRegistry.FromConfigurationTypes([typeof(ConfiguredTestQueryConfiguration), typeof(TestThingRootConfiguration)])
            .For(typeof(ConfiguredTestQuery));

        Assert.Equal(PermissionScope.Current, settings.PermissionScope);
        Assert.Null(settings.AggregateRoot);
    }

    [Fact]
    public void FromSources_ReadsTheFeaturesOfTheRoot_FromAnySource()
    {
        CommandSettings settings = CommandConfigurationRegistry.FromSources(
        [
            ModuleConfigurationSource.FromTypes(typeof(ScopedBoundCommandConfiguration)),
            ModuleConfigurationSource.FromTypes(typeof(ScopedTestRootFeatureConfiguration), typeof(TestThingRootConfiguration)),
        ]).For(typeof(ConfiguredTestCommand));

        Assert.Equal(PermissionScope.Current, settings.PermissionScope);
    }

    [Fact]
    public void ExpectedVersionOnAnUnversionedRoot_Throws_NamingTheRootAndConfiguration()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes(
                [typeof(VersionOnUnversionedRootConfiguration), typeof(GlobalTestRootFeatureConfiguration), typeof(TestThingRootConfiguration)]));

        Assert.Contains(typeof(GlobalTestRoot).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(VersionOnUnversionedRootConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BothShapesForOneRequest_Throw_AsADuplicate()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes([typeof(BothShapesConfiguration), typeof(TestThingRootConfiguration)]));

        Assert.Contains(typeof(ConfiguredTestQuery).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(BothShapesConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationForAnAbstractRequest_Throws_NamingIt()
    {
        // Lookup is by the exact type sent, so a policy on an abstract base would never apply to anything.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes([typeof(AbstractRequestConfiguration), typeof(TestThingRootConfiguration)]));

        Assert.Contains(typeof(AbstractTestCommand).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AbstractRequestConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredPermission_NotRegisteredByAnyAggregate_Fails()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes([typeof(ConfiguredTestQueryConfiguration)]));

        Assert.Contains(nameof(ConfiguredTestQueryConfiguration), error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(ConfiguredTestQuery).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains("permission.test.things.read", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredPermission_RegisteredInAnotherSource_Passes()
    {
        CommandSettings settings = CommandConfigurationRegistry.FromSources(
        [
            ModuleConfigurationSource.FromTypes(typeof(ConfiguredTestQueryConfiguration)),
            ModuleConfigurationSource.FromTypes(typeof(TestThingRootConfiguration)),
        ]).For(typeof(ConfiguredTestQuery));

        Assert.Single(settings.RequiredPermissions);
    }

    [Fact]
    public void RequiredAllPlatform_NeedsNoRegistration()
    {
        CommandSettings settings = CommandConfigurationRegistry.FromConfigurationTypes([typeof(AllPlatformTestQueryConfiguration)])
            .For(typeof(ConfiguredTestQuery));

        Assert.Equal(PermissionDefinition.AllPlatform, Assert.Single(settings.RequiredPermissions));
    }

    [Fact]
    public void FromSources_MergesEverySource()
    {
        var registry = CommandConfigurationRegistry.FromSources(
        [
            ModuleConfigurationSource.FromTypes(typeof(ConfiguredTestCommandConfiguration)),
            ModuleConfigurationSource.FromTypes(typeof(ConfiguredTestQueryConfiguration), typeof(TestThingRootConfiguration)),
        ]);

        Assert.Equal(2, registry.ConfiguredTypes.Count);
    }

    [Fact]
    public void FromAssembly_FindsConfigurationsInTheAssembly()
    {
        var source = ModuleConfigurationSource.FromAssembly(typeof(CommandConfigurationRegistryTests).Assembly, "test");

        Assert.Contains(typeof(ConfiguredTestCommandConfiguration), source.ConfigurationTypes);
        Assert.Contains(typeof(TestThingRootConfiguration), source.AggregateConfigurationTypes);
        Assert.DoesNotContain(typeof(ConfiguredTestCommand), source.ConfigurationTypes);
    }
}
