namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;

/// <summary>
/// The binding target for the edit form. No DataAnnotations: the fields are validated by the pipeline's own
/// <c>UpdateManufacturerCommandValidator</c> through <c>ISerginFormValidator</c>.
/// </summary>
public sealed class EditManufacturerFormModel
{
    public string Name { get; set; } = string.Empty;

    // Optional: a blank field is "no address", which the page maps to null before dispatching.
    public string? Address { get; set; }
}
