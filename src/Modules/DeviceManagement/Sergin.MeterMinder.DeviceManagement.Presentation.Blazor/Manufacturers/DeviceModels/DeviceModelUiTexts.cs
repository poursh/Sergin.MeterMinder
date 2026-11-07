using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels;

/// <summary>
/// The texts only the Blazor pages show. The fields, the name and the error texts are in <see cref="DeviceModelTexts"/>,
/// whose category these build on, so each key is the one the Application layer declared before.
/// </summary>
public static class DeviceModelUiTexts
{
    private static readonly TextCategory category = DeviceModelTexts.Category;

    // A text, not a field: a field named Id would also name the Id in every model command's validation messages.
    public static readonly TextDefinition Identifier = category.Text("Identifier", "ID");

    public static readonly TextDefinition Title = category.Text("Title", "Device models");

    public static readonly TextDefinition New = category.Text("New", "New device model");

    public static readonly TextDefinition RenameTitle = category.Text("RenameTitle", "Rename device model");

    public static readonly TextDefinition Add = category.Text("Add", "Add model");

    public static readonly TextDefinition SearchPlaceholder = category.Text("SearchPlaceholder", "Search model name…");

    public static readonly TextDefinition Empty = category.Text("Empty", "No models yet.");

    public static readonly TextDefinition DevicesColumn = category.Text("DevicesColumn", "Devices");

    public static readonly TextDefinition DeviceCount = category.Text("DeviceCount", "{Count} devices");

    public static readonly TextDefinition InUseTooltip = category.Text("InUseTooltip", "In use by devices — cannot be removed");

    public static readonly TextDefinition RemoveTitle = category.Text("RemoveTitle", "Remove device model");

    public static readonly TextDefinition RemoveConfirm =
        category.Text("RemoveConfirm", "Remove {Name}? It disappears from its manufacturer and every list.");

    public static readonly TextDefinition RemoveManyTitle = category.Text("RemoveManyTitle", "Remove device models");

    public static readonly TextDefinition RemoveManyConfirm =
        category.Text("RemoveManyConfirm", "Remove {Count} models? They disappear from their manufacturer and every list.");
}
