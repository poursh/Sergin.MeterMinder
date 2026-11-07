using Sergin.SharedKernel.Application.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;

/// <summary>
/// The Manufacturer aggregate's texts: its fields (a list column reads the field of its <c>ManufacturerListFields</c>
/// id), the manufacturer pages' titles, confirms and overview, and its validators' own messages. Its device models
/// declare theirs in <c>DeviceModelTexts</c>, a child category.
/// </summary>
public static class ManufacturerTexts
{
    public static readonly TextCategory Category = TextCategory.Create("DeviceManagement", "Manufacturers");

    private static readonly TextCategory errors = Category.Child("Errors");

    public static readonly FieldText Name = Category.Field("Name", "Name");

    public static readonly FieldText Address = Category.Field("Address", "Address");

    public static readonly TextDefinition Singular = Category.Text("Singular", "Manufacturer");

    /// <summary><c>DeleteManufacturerCommandValidator</c>: a model of the manufacturer is still used by a device.</summary>
    public static readonly TextDefinition InUse =
        errors.Text("InUse", "The manufacturer's models are still used by a device; delete those devices first.");
}
