using Sergin.SharedKernel.Application.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations;

/// <summary>
/// The module's own texts that belong to no aggregate: its menu title (the drawer group) and the verbs its pages
/// share that SharedKernel's <c>CommonTexts</c> does not carry.
/// </summary>
public static class DeviceManagementTexts
{
    public static readonly TextCategory Category = TextCategory.Create("DeviceManagement");

    private static readonly TextCategory actions = Category.Child("Actions");

    public static readonly TextDefinition MenuTitle = Category.Text("MenuTitle", "Device Management");

    public static readonly TextDefinition Create = actions.Text("Create", "Create");

    public static readonly TextDefinition Edit = actions.Text("Edit", "Edit");

    public static readonly TextDefinition Rename = actions.Text("Rename", "Rename");

    public static readonly TextDefinition Remove = actions.Text("Remove", "Remove");

    /// <summary>What a bulk delete reports after its count: <c>3 deleted</c>.</summary>
    public static readonly TextDefinition Deleted =
        actions.Text("Deleted", "deleted", "Follows the number of records a bulk delete removed.");

    /// <summary>What a bulk remove reports after its count: <c>3 removed</c>.</summary>
    public static readonly TextDefinition Removed =
        actions.Text("Removed", "removed", "Follows the number of records a bulk remove took out.");
}
