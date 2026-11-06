using Sergin.SharedKernel.Application.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;

/// <summary>
/// The Device aggregate's texts: one field per command property and list field a person sees (a list column and a
/// filter chip read the field of their <c>DeviceListFields</c> id), and the device pages' titles, confirms and
/// overview.
/// </summary>
public static class DeviceTexts
{
    public static readonly TextCategory Category = TextCategory.Create("DeviceManagement", "Devices");

    private static readonly TextCategory overview = Category.Child("Overview");

    public static readonly FieldText DeviceId = Category.Field("DeviceId", "Device ID");

    public static readonly FieldText DeviceModelId = Category.Field("DeviceModelId", "Model", placeholder: "Select a model");

    public static readonly FieldText DeviceModelName = Category.Field("DeviceModelName", "Model");

    public static readonly FieldText ManufacturerId =
        Category.Field("ManufacturerId", "Manufacturer", placeholder: "Select a manufacturer");

    public static readonly FieldText ManufacturerName = Category.Field("ManufacturerName", "Manufacturer");

    public static readonly FieldText ScopeId = Category.Field("ScopeId", "Scope");

    public static readonly FieldText CreatedAtUtc = Category.Field("CreatedAtUtc", "Created");

    public static readonly TextDefinition Title = Category.Text("Title", "Devices");

    public static readonly TextDefinition Singular = Category.Text("Singular", "Device");

    public static readonly TextDefinition New = Category.Text("New", "New device");

    public static readonly TextDefinition EditTitle = Category.Text("EditTitle", "Edit device");

    public static readonly TextDefinition SelectManufacturerFirst =
        Category.Text("SelectManufacturerFirst", "Select a manufacturer first");

    public static readonly TextDefinition SearchPlaceholder = Category.Text("SearchPlaceholder", "Search device ID or model…");

    public static readonly TextDefinition CreatedBetween = Category.Text("CreatedBetween", "Created between");

    public static readonly TextDefinition FilterChip =
        Category.Text("FilterChip", "{Field}: {Value}", "An applied filter: the field's label, then the chosen value.");

    public static readonly TextDefinition DateRange =
        Category.Text("DateRange", "{From} – {To}", "The created-between filter's chosen days.");

    public static readonly TextDefinition DeleteTitle = Category.Text("DeleteTitle", "Delete device");

    public static readonly TextDefinition DeleteConfirm =
        Category.Text("DeleteConfirm", "Delete {DeviceId}? It disappears from every list and page.");

    public static readonly TextDefinition DeleteManyTitle = Category.Text("DeleteManyTitle", "Delete devices");

    public static readonly TextDefinition DeleteManyConfirm =
        Category.Text("DeleteManyConfirm", "Delete {Count} devices? They disappear from every list and page.");

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
