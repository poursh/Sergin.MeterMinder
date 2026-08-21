using MudBlazor;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor;

public static class DeviceManagementNavigation
{
    public static IReadOnlyCollection<SerginNavItem> Items { get; } =
    [
        new SerginNavItem("Devices", "/dm/devices", Icons.Material.Filled.Router, Order: 100)
    ];
}
