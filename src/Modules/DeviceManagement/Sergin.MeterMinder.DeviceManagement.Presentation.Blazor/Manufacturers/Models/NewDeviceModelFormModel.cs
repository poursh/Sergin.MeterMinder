using System.ComponentModel.DataAnnotations;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;

public sealed class NewDeviceModelFormModel
{
    [Required]
    [StringLength(DeviceModelName.MaxLength, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}
