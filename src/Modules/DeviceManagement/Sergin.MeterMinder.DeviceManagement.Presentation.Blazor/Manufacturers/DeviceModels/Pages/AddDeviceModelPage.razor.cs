using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Pages;

/// <summary>The route for <see cref="Components.AddDeviceModelView"/>, shown as a page.</summary>
public sealed partial class AddDeviceModelPage
{
    [Parameter]
    public Guid ManufacturerId { get; set; }
}
