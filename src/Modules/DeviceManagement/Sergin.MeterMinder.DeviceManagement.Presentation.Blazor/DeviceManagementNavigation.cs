using MudBlazor;
using Sergin.SharedKernel.Modules;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor;

/// <summary>
/// The module's drawer group and entries. Each entry is also named individually so a page can build its breadcrumb
/// section step with <c>SerginBreadcrumb.Of(...)</c> from the one place the drawer reads the label and href.
/// </summary>
public static class DeviceManagementNavigation
{
    public static SerginNavGroup Group { get; } = new(
        "Device Management",
        Icons.Material.Filled.ElectricMeter,
        Order: 100);

    public static SerginNavItem Devices { get; } = new(
        "Devices",
        "/dm/devices",
        Icons.Material.Filled.Router,
        Order: 100,
        RequiredPermission: DevicePermissions.Read);

    public static SerginNavItem Manufacturers { get; } = new(
        "Manufacturers",
        "/dm/manufacturers",
        Icons.Material.Filled.Factory,
        Order: 110,
        RequiredPermission: ManufacturerPermissions.Read);

    public static IReadOnlyCollection<SerginNavItem> Items { get; } = [Devices, Manufacturers];
}
