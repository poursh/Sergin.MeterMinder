using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

/// <summary>The route for <see cref="Components.ManufacturerDetailView"/>, shown as a page.</summary>
public sealed partial class ManufacturerDetailPage
{
    [Parameter]
    public Guid Id { get; set; }
}
