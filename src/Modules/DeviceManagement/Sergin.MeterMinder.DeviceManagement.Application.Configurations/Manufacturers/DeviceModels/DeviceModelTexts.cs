using Sergin.SharedKernel.Application.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels;

/// <summary>
/// The texts of a manufacturer's device models, a category nested under <see cref="ManufacturerTexts"/>: the model
/// fields (a list column reads the field of its <c>DeviceModelListFields</c> id), the model pages and list, and the
/// errors raised about a model. <see cref="NameInUse"/> is the text <c>Manufacturer.AddModel</c> and
/// <c>RenameModel</c> name through <c>ManufacturerErrorKeys.ModelNameInUse</c>.
/// </summary>
public static class DeviceModelTexts
{
    public static readonly TextCategory Category = ManufacturerTexts.Category.Child("DeviceModels");

    private static readonly TextCategory errors = Category.Child("Errors");

    public static readonly FieldText Name = Category.Field("Name", "Name");

    public static readonly FieldText ManufacturerName = Category.Field("ManufacturerName", "Manufacturer");

    // A text, not a field: a field named Id would also name the Id in every model command's validation messages.
    public static readonly TextDefinition Identifier = Category.Text("Identifier", "ID");

    public static readonly TextDefinition Title = Category.Text("Title", "Device models");

    public static readonly TextDefinition Singular = Category.Text("Singular", "Device model");

    public static readonly TextDefinition New = Category.Text("New", "New device model");

    public static readonly TextDefinition RenameTitle = Category.Text("RenameTitle", "Rename device model");

    public static readonly TextDefinition Add = Category.Text("Add", "Add model");

    public static readonly TextDefinition SearchPlaceholder = Category.Text("SearchPlaceholder", "Search model name…");

    public static readonly TextDefinition Empty = Category.Text("Empty", "No models yet.");

    public static readonly TextDefinition DevicesColumn = Category.Text("DevicesColumn", "Devices");

    public static readonly TextDefinition DeviceCount = Category.Text("DeviceCount", "{Count} devices");

    public static readonly TextDefinition InUseTooltip = Category.Text("InUseTooltip", "In use by devices — cannot be removed");

    public static readonly TextDefinition RemoveTitle = Category.Text("RemoveTitle", "Remove device model");

    public static readonly TextDefinition RemoveConfirm =
        Category.Text("RemoveConfirm", "Remove {Name}? It disappears from its manufacturer and every list.");

    public static readonly TextDefinition RemoveManyTitle = Category.Text("RemoveManyTitle", "Remove device models");

    public static readonly TextDefinition RemoveManyConfirm =
        Category.Text("RemoveManyConfirm", "Remove {Count} models? They disappear from their manufacturer and every list.");

    /// <summary><c>RemoveDeviceModelCommandValidator</c>: a device still uses the model.</summary>
    public static readonly TextDefinition InUse =
        errors.Text("InUse", "The model is still used by a device; delete those devices first.");

    /// <summary>The manufacturer already has a model of that name (<c>ManufacturerErrorKeys.ModelNameInUse</c>).</summary>
    public static readonly TextDefinition NameInUse = errors.Text("NameInUse", "'Name' is already in use.");
}
