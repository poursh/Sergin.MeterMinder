using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Pages;

/// <summary>The route for <see cref="Components.DeviceModelDetailView"/>, shown as a page.</summary>
public sealed partial class DeviceModelDetailPage
{
    [Parameter]
    public Guid ManufacturerId { get; set; }

    [Parameter]
    public Guid Id { get; set; }
}
