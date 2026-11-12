using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices;

/// <summary>
/// The texts only the Blazor pages show. The fields, the name and the error texts are in <see cref="DeviceTexts"/>,
/// whose category these build on, so each key is the one the Application layer declared before.
/// </summary>
public static class DeviceUiTexts
{
    private static readonly TextCategory category = DeviceTexts.Category;

    private static readonly TextCategory overview = category.Child("Overview");

    public static readonly TextDefinition Title = category.Text("Title", "Devices");

    public static readonly TextDefinition New = category.Text("New", "New device");

    public static readonly TextDefinition EditTitle = category.Text("EditTitle", "Edit device");

    public static readonly TextDefinition SelectManufacturerFirst =
        category.Text("SelectManufacturerFirst", "Select a manufacturer first");

    public static readonly TextDefinition SearchPlaceholder = category.Text("SearchPlaceholder", "Search device ID or model…");

    public static readonly TextDefinition CreatedBetween = category.Text("CreatedBetween", "Created between");

    public static readonly TextDefinition FilterChip =
        category.Text("FilterChip", "{Field}: {Value}", "An applied filter: the field's label, then the chosen value.");

    public static readonly TextDefinition DateRange =
        category.Text("DateRange", "{From} – {To}", "The created-between filter's chosen days.");

    public static readonly TextDefinition DeleteTitle = category.Text("DeleteTitle", "Delete device");

    public static readonly TextDefinition DeleteConfirm =
        category.Text("DeleteConfirm", "Delete {DeviceId}? It disappears from every list and page.");

    public static readonly TextDefinition DeleteManyTitle = category.Text("DeleteManyTitle", "Delete devices");

    public static readonly TextDefinition DeleteManyConfirm =
        category.Text("DeleteManyConfirm", "Delete {Count} devices? They disappear from every list and page.");

    public static readonly TextDefinition ReadProfile = category.Text("ReadProfile", "Read profile");

    public static readonly TextDefinition ReadProfileDone = category.Text("ReadProfileDone", "Profile read for {DeviceId}.");

    public static readonly TextDefinition BulkReadProfileDone =
        category.Text("BulkReadProfileDone", "Profile read for {Count} devices.", "Reported after a bulk Read profile; {Count} is the number of devices read.");

    public static readonly TextDefinition OverviewDevices = overview.Text("Devices", "Devices");

    public static readonly TextDefinition OverviewInScope = overview.Text("InScope", "in this scope");

    public static readonly TextDefinition OverviewModelsInUse = overview.Text("ModelsInUse", "Models in use");

    public static readonly TextDefinition OverviewOfTotal = overview.Text("OfTotal", "of {Total}");

    public static readonly TextDefinition OverviewManufacturers = overview.Text("Manufacturers", "Manufacturers");

    public static readonly TextDefinition OverviewInCatalog = overview.Text("InCatalog", "in the catalog");

    public static readonly TextDefinition OverviewAdded = overview.Text("Added", "Added · 30d");

    public static readonly TextDefinition OverviewAddedCount =
        overview.Text("AddedCount", "+{Count}", "The number of devices added in the last 30 days.");

    public static readonly TextDefinition OverviewLastMonth = overview.Text("LastMonth", "last 30 days");

    public static readonly TextDefinition SummaryDevices =
        overview.Text("SummaryDevices", "devices", "Follows the device count in the collapsed overview line.");

    public static readonly TextDefinition SummaryModelsInUse =
        overview.Text("SummaryModelsInUse", "models in use", "Follows the count of models in use in the collapsed overview line.");
}
