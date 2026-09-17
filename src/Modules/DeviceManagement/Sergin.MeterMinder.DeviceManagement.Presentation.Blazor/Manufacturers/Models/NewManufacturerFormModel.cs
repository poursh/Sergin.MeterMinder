namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;

/// <summary>
/// The binding target for the create form. No DataAnnotations: the fields are validated by the
/// pipeline's own <c>CreateManufacturerCommandValidator</c> through <c>ISerginFormValidator</c>, and
/// attributes here would make MudForm report every rule twice.
/// </summary>
public sealed class NewManufacturerFormModel
{
    public string Name { get; set; } = string.Empty;

    // Optional: a blank field is "no address", which the page maps to null before dispatching.
    public string? Address { get; set; }
}
