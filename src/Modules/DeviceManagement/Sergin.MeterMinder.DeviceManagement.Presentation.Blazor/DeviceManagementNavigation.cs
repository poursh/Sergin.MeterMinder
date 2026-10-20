using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor;

/// <summary>
/// The module's drawer entries. Each is also named individually so a page can build its breadcrumb section step
/// with <c>SerginBreadcrumb.Of(...)</c> from the one place the drawer reads the label and href.
/// </summary>
public static class DeviceManagementNavigation
{
    public static SerginNavItem Devices { get; } = new(
        "Devices",
        "/dm/devices",
        Icons.Material.Filled.Router,
        Order: 100,
        RequiredPermission: DevicePermissions.Read.Permission.Value);

    public static SerginNavItem Manufacturers { get; } = new(
        "Manufacturers",
        "/dm/manufacturers",
        Icons.Material.Filled.Factory,
        Order: 110,
        RequiredPermission: ManufacturerPermissions.Read.Permission.Value);

    public static IReadOnlyCollection<SerginNavItem> Items { get; } = [Devices, Manufacturers];
}
