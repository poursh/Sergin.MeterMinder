using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers;

/// <summary>The permissions the Manufacturer aggregate owns, device models included. The catalogue is global, so a holder on any scope passes.</summary>
public static class ManufacturerPermissions
{
    public static PermissionDefinition Read { get; } = PermissionDefinition.Create("permission.dm.manufacturers.read", "View manufacturers and models");

    public static PermissionDefinition Update { get; } = PermissionDefinition.Create("permission.dm.manufacturers.update", "Create and edit manufacturers and models");

    public static PermissionDefinition Delete { get; } = PermissionDefinition.Create("permission.dm.manufacturers.delete", "Delete manufacturers");
}
