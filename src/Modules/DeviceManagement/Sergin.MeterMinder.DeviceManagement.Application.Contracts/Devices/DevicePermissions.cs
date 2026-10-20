using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices;

/// <summary>The permissions the Device aggregate owns. Devices are scoped, so each is checked on the caller's current scope.</summary>
public static class DevicePermissions
{
    public static PermissionDefinition Read { get; } = PermissionDefinition.Create("permission.dm.devices.read", "View devices");

    public static PermissionDefinition Update { get; } = PermissionDefinition.Create("permission.dm.devices.update", "Create and edit devices");

    public static PermissionDefinition Delete { get; } = PermissionDefinition.Create("permission.dm.devices.delete", "Delete devices");
}
