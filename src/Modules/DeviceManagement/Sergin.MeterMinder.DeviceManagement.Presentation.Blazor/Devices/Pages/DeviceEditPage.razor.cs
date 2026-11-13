using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Pages;

/// <summary>The route for the device's generated edit form, shown as a page.</summary>
public sealed partial class DeviceEditPage
{
    [Parameter]
    public Guid Id { get; set; }
}
