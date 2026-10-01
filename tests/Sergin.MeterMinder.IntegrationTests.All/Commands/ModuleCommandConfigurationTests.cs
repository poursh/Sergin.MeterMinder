using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.UserAccess.Application.Contracts.Users.Commands.GetOne;
using Sergin.UserAccess.Application.Contracts.Users.Commands.ProvisionExternalUser;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// The modules' declarations, read straight from their contracts assemblies: the attribute-era policy, one
/// request per shape, with the two that look like mistakes pinned on purpose.
/// </summary>
public sealed class ModuleCommandConfigurationTests
{
    private static CommandConfigurationRegistry Registry { get; } = CommandConfigurationRegistry.FromSources(
    [
        CommandConfigurationSource.FromAssembly(typeof(DeleteDeviceCommand).Assembly),
        CommandConfigurationSource.FromAssembly(typeof(GetUserByIdQueryCommand).Assembly),
    ]);

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
