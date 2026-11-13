using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

/// <summary>The route for generated form, shown as a page.</summary>
public sealed partial class ManufacturerEditPage
{
    [Parameter]
    public Guid Id { get; set; }
}
