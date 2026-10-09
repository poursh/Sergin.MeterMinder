using System.Reflection;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.UserAccess.Application;
using Sergin.UserAccess.Application.Configurations;
using Sergin.UserAccess.Application.Contracts.Roles.Commands.Rename;
using Sergin.UserAccess.Application.Contracts.Scopes.Commands.Create;
using Sergin.UserAccess.Application.Contracts.Scopes.Commands.Move;
using Sergin.UserAccess.Application.Contracts.Users.Assignments.Commands.Assign;
using Sergin.UserAccess.Application.Contracts.Users.Commands.GetOne;
using Sergin.UserAccess.Application.Contracts.Users.Commands.ProvisionExternalUser;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// The modules' declarations, read straight from their configurations assemblies: the attribute-era policy, one
/// request per shape, with the two that look like mistakes pinned on purpose.
/// </summary>
public sealed class ModuleCommandConfigurationTests
{
    private static CommandConfigurationRegistry Registry { get; } = CommandConfigurationRegistry.FromSources(
    [
        CommandConfigurationSource.FromAssembly(DeviceManagementApplicationConfigurationsAssemblyReference.Assembly),
        CommandConfigurationSource.FromAssembly(UserAccessApplicationConfigurationsAssemblyReference.Assembly),
    ]);

    [Fact]
    public void ContractsAndApplicationAssemblies_HoldNoConfigurations()
    {
        Assembly[] assemblies =
        [
            typeof(DeleteDeviceCommand).Assembly,
            DeviceManagementApplicationAssemblyReference.Assembly,
            typeof(GetUserByIdQueryCommand).Assembly,
            UserAccessApplicationAssemblyReference.Assembly,
        ];

        foreach (Assembly assembly in assemblies)
        {
            Assert.Empty(CommandConfigurationSource.FromAssembly(assembly).ConfigurationTypes);
            Assert.Empty(AggregateFeatureRegistry.ConfigurationTypesIn(assembly));
        }

        Assert.Equal(2, AggregateFeatureRegistry.ConfigurationTypesIn(
            DeviceManagementApplicationConfigurationsAssemblyReference.Assembly).Count);
    }

    [Fact]
    public void DeleteDevice_NeedsDeletePermission_AndAVersion()
    {
        CommandSettings settings = Registry.For(typeof(DeleteDeviceCommand));

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal("permission.dm.devices.delete", Assert.Single(settings.RequiredPermissions).Value);
    }

    [Fact]
    public void DeviceListQuery_NeedsReadPermission()
    {
        Assert.Equal("permission.dm.devices.read", Assert.Single(Registry.For(typeof(GetDeviceListQueryCommand)).RequiredPermissions).Value);
    }

    [Fact]
    public void AddDeviceModel_NeedsAVersion_ButNoPermission()
    {
        CommandSettings settings = Registry.For(typeof(AddDeviceModelCommand));

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Empty(settings.RequiredPermissions);
    }

    [Fact]
    public void ProvisionExternalUser_IsUnconfigured()
    {
        Assert.Same(CommandSettings.None, Registry.For(typeof(ProvisionExternalUserCommand)));
    }

    [Fact]
    public void CreateUser_NeedsGlobalManagePermission()
    {
        Assert.Equal("permission.ua.users.manage", Assert.Single(Registry.For(typeof(CreateUserCommand)).RequiredPermissions).Value);
    }

    /// <summary>
    /// Scoped management commands require no permission here: a regional administrator holds it only in their
    /// region, so the global claims the pipeline checks would refuse them. Their handlers check it against the
    /// target scope instead. Edits to one role carry a version; a scope move or delete touches several versioned
    /// roots (the subtree, the owned roles) in one save, so it cannot.
    /// </summary>
    [Fact]
    public void ScopedManagementCommands_CheckNoGlobalPermission()
    {
        CommandSettings renameRole = Registry.For(typeof(RenameRoleCommand));

        Assert.Empty(renameRole.RequiredPermissions);
        Assert.True(renameRole.RequiresExpectedVersion);
        Assert.Same(CommandSettings.None, Registry.For(typeof(CreateScopeCommand)));
        Assert.Same(CommandSettings.None, Registry.For(typeof(MoveScopeCommand)));
        Assert.Same(CommandSettings.None, Registry.For(typeof(AssignRoleCommand)));
    }

    [Fact]
    public void ModulesDeclareTwentyFiveRequests()
    {
        Assert.Equal(25, Registry.ConfiguredTypes.Count);
    }
}
