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

    private static readonly TextCategory overview = Category.Child("Overview");

    private static readonly TextCategory errors = Category.Child("Errors");

    public static readonly FieldText Name = Category.Field("Name", "Name");

    public static readonly FieldText Address = Category.Field("Address", "Address");

    public static readonly TextDefinition Title = Category.Text("Title", "Manufacturers");

    public static readonly TextDefinition Singular = Category.Text("Singular", "Manufacturer");

    public static readonly TextDefinition New = Category.Text("New", "New manufacturer");

    public static readonly TextDefinition EditTitle = Category.Text("EditTitle", "Edit manufacturer");

    public static readonly TextDefinition SearchPlaceholder = Category.Text("SearchPlaceholder", "Search name or address…");

    public static readonly TextDefinition DeleteTitle = Category.Text("DeleteTitle", "Delete manufacturer");

    public static readonly TextDefinition DeleteConfirm =
        Category.Text("DeleteConfirm", "Delete {Name} and its models? They disappear from every list and page.");

    public static readonly TextDefinition DeleteManyTitle = Category.Text("DeleteManyTitle", "Delete manufacturers");

    public static readonly TextDefinition DeleteManyConfirm = Category.Text(
        "DeleteManyConfirm", "Delete {Count} manufacturers and their models? They disappear from every list and page.");

    public static readonly TextDefinition OverviewModels = overview.Text("Models", "Models");

    public static readonly TextDefinition OverviewOfManufacturer = overview.Text("OfManufacturer", "of this manufacturer");

    public static readonly TextDefinition OverviewDevices = overview.Text("Devices", "Devices");

    public static readonly TextDefinition OverviewInEveryScope = overview.Text("InEveryScope", "in every scope");

    public static readonly TextDefinition SummaryModels =
        overview.Text("SummaryModels", "models", "Follows the model count in the collapsed overview line.");

    public static readonly TextDefinition SummaryDevices =
        overview.Text("SummaryDevices", "devices", "Follows the device count in the collapsed overview line.");

    /// <summary><c>DeleteManufacturerCommandValidator</c>: a model of the manufacturer is still used by a device.</summary>
    public static readonly TextDefinition InUse =
        errors.Text("InUse", "The manufacturer's models are still used by a device; delete those devices first.");
}
