using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Components;

/// <summary>
/// The device list's overview cards, in the generated list view's <c>ListOverview</c> slot: the devices in the current
/// scope, the models in use, the catalog's manufacturers and the devices added in the last 30 days. It reads the figures
/// the view loaded, which renders it anew on reload.
/// </summary>
public sealed partial class DeviceOverviewSlot
{
    /// <summary>What the generated view hands a slot: the device overview it loaded, in <c>Overview</c>.</summary>
    [Parameter]
    public AggregateViewContext? Context { get; set; }

    [Inject]
    private IStringLocalizer Localizer { get; set; } = default!;

    [Inject]
    private ICultureFormatter Formatter { get; set; } = default!;

    private IEnumerable<(TextDefinition Label, string Value, string Sub)> OverviewCards =>
    [
        (DeviceUiTexts.OverviewDevices, Count(Overview?.Devices), Localizer.Text(DeviceUiTexts.OverviewInScope)),
        (DeviceUiTexts.OverviewModelsInUse, Count(Overview?.ModelsInUse),
            Overview is null ? string.Empty : Localizer.Text(DeviceUiTexts.OverviewOfTotal, new { Total = Formatter.Number(Overview.ModelsTotal) })),
        (DeviceUiTexts.OverviewManufacturers, Count(Overview?.Manufacturers), Localizer.Text(DeviceUiTexts.OverviewInCatalog)),
        (DeviceUiTexts.OverviewAdded,
            Overview is null ? "–" : Localizer.Text(DeviceUiTexts.OverviewAddedCount, new { Count = Formatter.Number(Overview.AddedLast30Days) }),
            Localizer.Text(DeviceUiTexts.OverviewLastMonth)),
    ];

    // The generated view loads the overview once and hands it to both slots; a failed read leaves it null.
    private DeviceOverview? Overview => Context?.Overview as DeviceOverview;

    private string Count(int? value) => value is { } number ? Formatter.Number(number) : "–";
}
