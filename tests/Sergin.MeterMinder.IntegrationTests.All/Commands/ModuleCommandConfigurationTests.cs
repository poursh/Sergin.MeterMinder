using System.Reflection;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.UserAccess.Application;
using Sergin.UserAccess.Application.Configurations;
using Sergin.UserAccess.Application.Contracts.Roles.Commands.Rename;
using Sergin.UserAccess.Application.Contracts.Scopes.Commands.Create;
using Sergin.UserAccess.Application.Contracts.Scopes.Commands.Move;
using Sergin.UserAccess.Application.Contracts.Users.Assignments.Commands.GetEffective;
using Sergin.UserAccess.Application.Contracts.Users.Assignments.Commands.GetList;
using Sergin.UserAccess.Application.Contracts.Users.Assignments.Commands.Assign;
using Sergin.UserAccess.Application.Contracts.Users.Commands.GetOne;
using Sergin.UserAccess.Application.Contracts.Users.Commands.ProvisionExternalUser;
using Sergin.UserAccess.Domain.Users;

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
    public void AddDeviceModel_NeedsAVersion_AndTheCatalogPermissionOnAnyScope()
    {
        CommandSettings settings = Registry.For(typeof(AddDeviceModelCommand));

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal("permission.dm.manufacturers.update", Assert.Single(settings.RequiredPermissions).Value);
        Assert.Equal(PermissionScope.Any, settings.PermissionScope);
    }

    [Fact]
    public void CreateDevice_NeedsUpdatePermission_InTheCurrentScope()
    {
        CommandSettings settings = Registry.For(typeof(CreateDeviceCommand));

        Assert.Equal("permission.dm.devices.update", Assert.Single(settings.RequiredPermissions).Value);
        Assert.Equal(PermissionScope.Current, settings.PermissionScope);
    }

    [Fact]
    public void CreateManufacturer_NeedsTheCatalogPermission_OnAnyScope()
    {
        CommandSettings settings = Registry.For(typeof(CreateManufacturerCommand));

        Assert.Equal("permission.dm.manufacturers.update", Assert.Single(settings.RequiredPermissions).Value);
        Assert.Equal(PermissionScope.Any, settings.PermissionScope);
    }

    [Fact]
    public void ProvisionExternalUser_IsUnconfigured()
    {
        Assert.Same(CommandSettings.None, Registry.For(typeof(ProvisionExternalUserCommand)));
    }

    [Fact]
    public void CreateUser_NeedsManagePermission_OnAnyScope()
    {
        CommandSettings settings = Registry.For(typeof(CreateUserCommand));

        Assert.Equal("permission.ua.users.manage", Assert.Single(settings.RequiredPermissions).Value);
        Assert.Equal(PermissionScope.Any, settings.PermissionScope);
    }

    /// <summary>
    /// Scoped management commands require no permission here: the scope they act on is the one the command names,
    /// not the caller's current scope, so their handlers check it against that target instead. Edits to one role carry a version; a scope move or delete touches several versioned
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

    /// <summary>
    /// Bound to their aggregate, the scope comes from its features: Device is scoped, so its requests are checked
    /// in the current scope; Manufacturer and User are not, so theirs pass a caller who holds the permission on
    /// any scope. No configuration chooses the scope itself.
    /// </summary>
    [Fact]
    public void BoundRequests_TakeTheirScopeFromTheAggregate()
    {
        CommandSettings deleteDevice = Registry.For(typeof(DeleteDeviceCommand));
        CommandSettings deleteManufacturer = Registry.For(typeof(DeleteManufacturerCommand));
        CommandSettings getUser = Registry.For(typeof(GetUserByIdQueryCommand));

        Assert.Equal(typeof(Device), deleteDevice.AggregateRoot);
        Assert.Equal(PermissionScope.Current, deleteDevice.PermissionScope);
        Assert.Equal(PermissionScope.Current, Registry.For(typeof(GetDeviceListQueryCommand)).PermissionScope);
        Assert.Equal(typeof(Manufacturer), deleteManufacturer.AggregateRoot);
        Assert.Equal(PermissionScope.Any, deleteManufacturer.PermissionScope);
        Assert.Equal(typeof(User), getUser.AggregateRoot);
        Assert.Equal(PermissionScope.Any, getUser.PermissionScope);
    }

    [Theory]
    [InlineData(typeof(GetManufacturerListQueryCommand))]
    [InlineData(typeof(GetManufacturerByIdQueryCommand))]
    [InlineData(typeof(GetDeviceModelListQueryCommand))]
    [InlineData(typeof(GetDeviceModelByIdQueryCommand))]
    public void CatalogReads_AreCheckedOnAnyScope(Type request)
    {
        CommandSettings settings = Registry.For(request);

        Assert.Equal(typeof(Manufacturer), settings.AggregateRoot);
        Assert.Equal(PermissionScope.Any, settings.PermissionScope);
    }

    [Theory]
    [InlineData(typeof(GetUserAssignmentsQueryCommand))]
    [InlineData(typeof(GetUserEffectivePermissionsQueryCommand))]
    public void AssignmentReads_AreBoundToTheUser(Type request)
    {
        CommandSettings settings = Registry.For(request);

        Assert.Equal(typeof(User), settings.AggregateRoot);
        Assert.Equal(PermissionScope.Any, settings.PermissionScope);
    }

    [Fact]
    public void ModulesDeclareTwentySevenRequests()
    {
        Assert.Equal(27, Registry.ConfiguredTypes.Count);
    }
}
