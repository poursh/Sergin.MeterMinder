using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers;

/// <summary>
/// The texts only the Blazor pages show. The fields, the name and the error texts are in <see cref="ManufacturerTexts"/>,
/// whose category these build on, so each key is the one the Application layer declared before.
/// </summary>
public static class ManufacturerUiTexts
{
    private static readonly TextCategory category = ManufacturerTexts.Category;

    private static readonly TextCategory overview = category.Child("Overview");

    public static readonly TextDefinition Title = category.Text("Title", "Manufacturers");

    public static readonly TextDefinition New = category.Text("New", "New manufacturer");

    public static readonly TextDefinition EditTitle = category.Text("EditTitle", "Edit manufacturer");

    public static readonly TextDefinition SearchPlaceholder = category.Text("SearchPlaceholder", "Search name or address…");

    public static readonly TextDefinition DeleteTitle = category.Text("DeleteTitle", "Delete manufacturer");

    public static readonly TextDefinition DeleteConfirm =
        category.Text("DeleteConfirm", "Delete {Name} and its models? They disappear from every list and page.");

    public static readonly TextDefinition DeleteManyTitle = category.Text("DeleteManyTitle", "Delete manufacturers");

    public static readonly TextDefinition DeleteManyConfirm = category.Text(
        "DeleteManyConfirm", "Delete {Count} manufacturers and their models? They disappear from every list and page.");

    public static readonly TextDefinition OverviewModels = overview.Text("Models", "Models");

    public static readonly TextDefinition OverviewOfManufacturer = overview.Text("OfManufacturer", "of this manufacturer");

    public static readonly TextDefinition OverviewDevices = overview.Text("Devices", "Devices");

    public static readonly TextDefinition OverviewInEveryScope = overview.Text("InEveryScope", "in every scope");

    public static readonly TextDefinition SummaryModels =
        overview.Text("SummaryModels", "models", "Follows the model count in the collapsed overview line.");

    public static readonly TextDefinition SummaryDevices =
        overview.Text("SummaryDevices", "devices", "Follows the device count in the collapsed overview line.");
}
