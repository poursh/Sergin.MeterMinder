namespace Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

/// <summary>
/// The text keys the Manufacturer aggregate's own validation errors name in their metadata (<c>ErrorText.Of</c>). The
/// domain cannot see the declarations in <c>.Application.Configurations</c>, so each key is written here once and
/// declared there (<c>DeviceModelTexts</c>); a test refuses a key with no declaration.
/// </summary>
public static class ManufacturerErrorKeys
{
    /// <summary>A model name this manufacturer already uses, from <see cref="Manufacturer.AddModel"/> and <see cref="Manufacturer.RenameModel"/>.</summary>
    public const string ModelNameInUse = "device-management.manufacturers.device-models.errors.name-in-use";
}
