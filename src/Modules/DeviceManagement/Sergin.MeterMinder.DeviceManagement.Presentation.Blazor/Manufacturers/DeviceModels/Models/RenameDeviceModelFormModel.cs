namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Models;

/// <summary>
/// The binding target for the rename form. No DataAnnotations: the field is validated by the pipeline's own
/// <c>RenameDeviceModelCommandValidator</c> through <c>ISerginFormValidator</c>.
/// </summary>
public sealed class RenameDeviceModelFormModel
{
    public string Name { get; set; } = string.Empty;
}
