using System.ComponentModel.DataAnnotations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Models;

public sealed class NewDeviceFormModel
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    public Guid ManufacturerId { get; set; }
}
