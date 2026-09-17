namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;

/// <summary>
/// The binding target for the create form. No DataAnnotations: the field is validated by the
/// pipeline's own <c>AddDeviceModelCommandValidator</c> through <c>ISerginFormValidator</c>, and
/// attributes here would make MudForm report every rule twice.
/// </summary>
public sealed class NewDeviceModelFormModel
{
    public string Name { get; set; } = string.Empty;
}
