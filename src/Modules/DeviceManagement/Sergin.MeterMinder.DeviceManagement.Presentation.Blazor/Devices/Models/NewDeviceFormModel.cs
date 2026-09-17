namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Models;

/// <summary>
/// The binding target for the create form. No DataAnnotations: the fields are validated by the
/// pipeline's own <c>CreateDeviceCommandValidator</c> through <c>ISerginFormValidator</c>, and
/// attributes here would make MudForm report every rule twice.
/// </summary>
public sealed class NewDeviceFormModel
{
    public string DeviceId { get; set; } = string.Empty;

    // The manufacturer is page state on CreateDevicePage, not part of what is submitted.
    public Guid DeviceModelId { get; set; }
}
