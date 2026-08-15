using MudBlazor;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.Presentation.Blazor;

public static class MeterMinderNavigation
{
    public static IReadOnlyCollection<SerginNavItem> Items { get; } =
    [
        new SerginNavItem("Devices", "/mm/devices", Icons.Material.Filled.Router, Order: 100)
    ];
}
