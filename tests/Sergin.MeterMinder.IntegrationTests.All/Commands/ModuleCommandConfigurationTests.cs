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
    public void ModulesDeclareFifteenRequests()
    {
        Assert.Equal(15, Registry.ConfiguredTypes.Count);
    }
}
