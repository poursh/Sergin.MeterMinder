using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands.Types;

/// <summary>
/// The registry reads a typed record's aggregate root, permission scope and expected-version rule from the
/// record's type, with or without a configuration.
/// </summary>
public sealed class CommandTypeRegistryTests
{
    [Fact]
    public void TypedEntityCommand_OnVersionedRoot_RequiresVersion_WithoutConfiguration()
    {
        CommandConfigurationRegistry registry = Registry(typeof(TestDeleteCommand), typeof(VersionedTypedRootConfiguration));

        CommandSettings settings = registry.For(typeof(TestDeleteCommand));

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal(typeof(TestTypedRoot), settings.AggregateRoot);
        Assert.Empty(settings.RequiredPermissions);
        Assert.Contains(typeof(TestDeleteCommand), registry.RequestTypes);
        Assert.DoesNotContain(typeof(TestDeleteCommand), registry.ConfiguredTypes);
    }

    [Fact]
    public void TypedChildCommand_RequiresTheRootsVersion()
    {
        CommandSettings settings = Settings(typeof(TestRemoveChildCommand), typeof(VersionedTypedRootConfiguration));

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal(typeof(TestTypedRoot), settings.AggregateRoot);
    }

    [Fact]
    public void TypedAddChildCommand_RequiresTheRootsVersion() =>
        Assert.True(Settings(typeof(TestAddChildCommand), typeof(VersionedTypedRootConfiguration)).RequiresExpectedVersion);

    [Fact]
    public void TypedCreateCommand_RequiresNoVersion()
    {
        CommandSettings settings = Settings(typeof(TestCreateCommand), typeof(VersionedTypedRootConfiguration));

        Assert.False(settings.RequiresExpectedVersion);
        Assert.Equal(typeof(TestTypedRoot), settings.AggregateRoot);
    }

    [Fact]
    public void TypedCreateUnderParent_RequiresNoVersion() =>
        Assert.False(Settings(typeof(TestCreateUnderParentCommand), typeof(VersionedTypedRootConfiguration)).RequiresExpectedVersion);

    [Fact]
    public void TypedCriteriaCommand_RequiresNoVersion() =>
        Assert.False(Settings(typeof(TestCriteriaCommand), typeof(VersionedTypedRootConfiguration)).RequiresExpectedVersion);

    [Fact]
    public void TypedEntityQuery_OnVersionedRoot_RequiresNoVersion()
    {
        CommandSettings settings = Settings(typeof(TestGetOneQuery), typeof(VersionedTypedRootConfiguration));

        Assert.False(settings.RequiresExpectedVersion);
        Assert.Equal(typeof(TestTypedRoot), settings.AggregateRoot);
    }

    [Fact]
    public void TypedChildQuery_OnVersionedRoot_RequiresNoVersion() =>
        Assert.False(Settings(typeof(TestChildGetOneQuery), typeof(VersionedTypedRootConfiguration)).RequiresExpectedVersion);

    [Fact]
    public void TypedEntityCommand_OnUnversionedRoot_RequiresNoVersion()
    {
        CommandSettings settings = Settings(typeof(TestDeleteCommand), typeof(UnversionedTypedRootConfiguration));

        Assert.False(settings.RequiresExpectedVersion);
        Assert.Equal(typeof(TestTypedRoot), settings.AggregateRoot);
    }

    [Fact]
    public void CloneCommand_RequiresNoVersion() =>
        Assert.False(Settings(typeof(TestCloneCommand), typeof(VersionedTypedRootConfiguration)).RequiresExpectedVersion);

    [Fact]
    public void TypedRecordWithConfiguration_TakesItsPermissions_AndItsRootFromTheType()
    {
        CommandConfigurationRegistry registry = Registry(
            typeof(TestDeleteCommand), typeof(PermissionedDeleteConfiguration), typeof(VersionedTypedRootConfiguration), typeof(TestThingRootConfiguration));

        CommandSettings settings = registry.For(typeof(TestDeleteCommand));

        Assert.Equal(TestThingPermissions.Delete, Assert.Single(settings.RequiredPermissions));
        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal(typeof(TestTypedRoot), settings.AggregateRoot);
        Assert.Equal(typeof(TestDeleteCommand), Assert.Single(registry.ConfiguredTypes));
        Assert.Equal(typeof(TestDeleteCommand), Assert.Single(registry.RequestTypes));
    }

    [Fact]
    public void TypedRecord_ConfiguredWithoutListingTheRecord_StillTakesItsRoot()
    {
        CommandSettings settings = Settings(
            typeof(PermissionedDeleteConfiguration), typeof(VersionedTypedRootConfiguration), typeof(TestThingRootConfiguration));

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal(typeof(TestTypedRoot), settings.AggregateRoot);
    }

    [Fact]
    public void TypedRecord_WithoutConfiguration_TakesTheRootsTextCategory() =>
        Assert.Equal(
            TestTypedTexts.Category,
            Registry(typeof(TestDeleteCommand), typeof(VersionedTypedRootConfiguration)).CategoryOf(typeof(TestDeleteCommand)));

    [Fact]
    public void WithoutExpectedVersion_WaivesTheDefault()
    {
        CommandSettings settings = Settings(typeof(WaivedDeleteConfiguration), typeof(VersionedTypedRootConfiguration));

        Assert.False(settings.RequiresExpectedVersion);
        Assert.Equal(typeof(TestTypedRoot), settings.AggregateRoot);
    }

    [Fact]
    public void WithoutExpectedVersion_WhereNothingIsRequired_Throws_NamingTheConfiguration()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Registry(typeof(WaivedCreateConfiguration), typeof(VersionedTypedRootConfiguration)));

        Assert.Contains(typeof(WaivedCreateConfiguration).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(TestCreateCommand).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutExpectedVersion_OnUnversionedRoot_Throws_NamingTheConfiguration()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Registry(typeof(WaivedDeleteConfiguration), typeof(UnversionedTypedRootConfiguration)));

        Assert.Contains(typeof(WaivedDeleteConfiguration).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutExpectedVersion_OnQuery_Throws_NamingTheConfiguration()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Registry(typeof(WaivedQueryConfiguration), typeof(VersionedTypedRootConfiguration)));

        Assert.Contains(typeof(WaivedQueryConfiguration).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(TestGetOneQuery).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutExpectedVersion_OnUntypedRecord_Throws_NamingTheConfiguration()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Registry(typeof(WaivedUntypedConfiguration)));

        Assert.Contains(typeof(WaivedUntypedConfiguration).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(TestUntypedCommand).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireAndWithoutExpectedVersion_Together_Throw_NamingTheConfiguration()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Registry(typeof(BothVersionCallsConfiguration), typeof(VersionedTypedRootConfiguration)));

        Assert.Contains(typeof(BothVersionCallsConfiguration).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedRecord_BoundToAnotherRoot_Throws_NamingBoth()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Registry(
                typeof(MisboundDeleteConfiguration), typeof(VersionedTypedRootConfiguration),
                typeof(OtherRootFeatureConfiguration), typeof(TestThingRootConfiguration)));

        Assert.Contains(typeof(MisboundDeleteConfiguration).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(TestDeleteCommand).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(TestTypedRoot).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(TestOtherRoot).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedRecord_BoundToItsOwnRoot_IsAccepted()
    {
        CommandSettings settings = Settings(
            typeof(SameRootBoundDeleteConfiguration), typeof(VersionedTypedRootConfiguration), typeof(TestThingRootConfiguration));

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal(typeof(TestTypedRoot), settings.AggregateRoot);
    }

    [Fact]
    public void TypedRecordOnUnscopedRoot_IsCheckedOnAnyScope() =>
        Assert.Equal(PermissionScope.Any, Settings(typeof(TestDeleteCommand), typeof(VersionedTypedRootConfiguration)).PermissionScope);

    [Fact]
    public void TypedRecordOnScopedRoot_IsCheckedInTheCurrentScope() =>
        Assert.Equal(PermissionScope.Current, Settings(typeof(TestDeleteCommand), typeof(ScopedTypedRootConfiguration)).PermissionScope);

    [Fact]
    public void UntypedUnconfiguredRecord_ReturnsNone_AndIsNoRequestType()
    {
        CommandConfigurationRegistry registry = Registry(typeof(TestUntypedCommand));

        Assert.Same(CommandSettings.None, registry.For(typeof(TestUntypedCommand)));
        Assert.Empty(registry.RequestTypes);
    }

    [Fact]
    public void FromTypes_PutsRequestsInRequestTypes_NotConfigurationTypes()
    {
        var source = ModuleConfigurationSource.FromTypes(typeof(TestDeleteCommand), typeof(PermissionedDeleteConfiguration));

        Assert.Equal(typeof(TestDeleteCommand), Assert.Single(source.RequestTypes));
        Assert.Equal(typeof(PermissionedDeleteConfiguration), Assert.Single(source.ConfigurationTypes));
    }

    [Fact]
    public void FromAssemblies_ReadsTheContractsAssemblysRequests()
    {
        var source = ModuleConfigurationSource.FromAssemblies(
            typeof(CommandTypeRegistryTests).Assembly, typeof(CommandTypeRegistryTests).Assembly, "test");

        Assert.Contains(typeof(TestDeleteCommand), source.RequestTypes);
        Assert.DoesNotContain(typeof(PermissionedDeleteConfiguration), source.RequestTypes);
        Assert.Contains(typeof(PermissionedDeleteConfiguration), source.ConfigurationTypes);
        Assert.Equal("test", source.Schema);
    }

    /// <summary>The settings of the first type: a request itself, or the request a configuration configures.</summary>
    private static CommandSettings Settings(params Type[] types) => Registry(types).For(RequestOf(types[0]));

    private static Type RequestOf(Type type) =>
        type.GetInterfaces()
            .FirstOrDefault(contract => contract.IsGenericType
                && (contract.GetGenericTypeDefinition() == typeof(ICommandConfiguration<>)
                    || contract.GetGenericTypeDefinition() == typeof(ICommandConfiguration<,>)))
            ?.GetGenericArguments()[0]
        ?? type;

    private static CommandConfigurationRegistry Registry(params Type[] types) =>
        CommandConfigurationRegistry.FromSources([ModuleConfigurationSource.FromTypes(types)]);
}
