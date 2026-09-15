using System.ComponentModel.DataAnnotations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Models;

public sealed class NewDeviceFormModel
{
    [Required]
    // Qualified: this model's own DeviceId property shadows the domain type inside the attribute.
    [StringLength(Domain.Devices.DeviceId.MaxLength, MinimumLength = 1)]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    public Guid ManufacturerId { get; set; }
}
