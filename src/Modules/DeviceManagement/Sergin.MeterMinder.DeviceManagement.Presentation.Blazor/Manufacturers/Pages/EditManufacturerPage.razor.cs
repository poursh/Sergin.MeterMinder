using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

/// <summary>The route for <see cref="Components.EditManufacturerView"/>, shown as a page.</summary>
public sealed partial class EditManufacturerPage
{
    [Parameter]
    public Guid Id { get; set; }
}
