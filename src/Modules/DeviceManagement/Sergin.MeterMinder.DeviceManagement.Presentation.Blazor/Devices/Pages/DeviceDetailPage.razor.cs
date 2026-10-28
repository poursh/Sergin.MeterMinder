using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Pages;

/// <summary>The route for <see cref="Components.DeviceDetailView"/>, shown as a page.</summary>
public sealed partial class DeviceDetailPage
{
    [Parameter]
    public Guid Id { get; set; }
}
