using System.ComponentModel.DataAnnotations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Models;

public sealed class NewDeviceFormModel
{
    [Required]
    // Qualified: this model's own DeviceId property shadows the domain type inside the attribute.
    [StringLength(Domain.Devices.DeviceId.MaxLength, MinimumLength = 1)]
    public string DeviceId { get; set; } = string.Empty;

    // The manufacturer is page state on CreateDevicePage, not part of what is submitted. [Required] on a Guid
    // never fails (Guid.Empty is not null) — the pipeline validator's NotEmpty is the real check, as it was for
    // ManufacturerId before.
    [Required]
    public Guid DeviceModelId { get; set; }
}
