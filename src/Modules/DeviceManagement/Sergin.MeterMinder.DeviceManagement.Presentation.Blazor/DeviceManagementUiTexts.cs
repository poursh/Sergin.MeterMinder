using Sergin.SharedKernel.Application.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor;

/// <summary>
/// The module's own texts that belong to no aggregate: its menu title (the drawer group). Only the UI shows it, so it
/// is declared here rather than in Application.Configurations. The verbs its pages share
/// (Create, Edit, Rename, Remove, and the bulk outcome words) are SharedKernel's <c>CommonTexts</c>.
/// </summary>
public static class DeviceManagementUiTexts
{
    public static readonly TextCategory Category = TextCategory.Create("DeviceManagement");

    public static readonly TextDefinition MenuTitle = Category.Text("MenuTitle", "Device Management");
}
