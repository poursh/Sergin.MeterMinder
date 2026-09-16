using System.ComponentModel.DataAnnotations;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;

public sealed class NewManufacturerFormModel
{
    [Required]
    [StringLength(ManufacturerName.MaxLength, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    // Optional: a blank field is "no address", which the page maps to null before dispatching.
    [StringLength(ManufacturerAddress.MaxLength)]
    public string? Address { get; set; }
}
