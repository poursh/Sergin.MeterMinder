namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Models;

/// <summary>
/// The binding target for the edit form. No DataAnnotations: the fields are validated by the pipeline's own
/// <c>UpdateDeviceCommandValidator</c> through <c>ISerginFormValidator</c>.
/// </summary>
public sealed class EditDeviceFormModel
{
    public string DeviceId { get; set; } = string.Empty;

    // The manufacturer is page state on EditDevicePage, not part of what is submitted.
    public Guid DeviceModelId { get; set; }
}
