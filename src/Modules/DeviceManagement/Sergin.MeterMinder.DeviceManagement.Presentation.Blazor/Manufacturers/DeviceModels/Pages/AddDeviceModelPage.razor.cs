using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Pages;

/// <summary>The route for generated child form, shown as a page.</summary>
public sealed partial class AddDeviceModelPage
{
    [Parameter]
    public Guid ManufacturerId { get; set; }
}
