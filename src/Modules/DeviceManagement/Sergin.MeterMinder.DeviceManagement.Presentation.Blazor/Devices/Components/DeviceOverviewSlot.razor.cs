using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Components;

/// <summary>
/// The device list's overview cards, in the generated list view's <c>ListOverview</c> slot: the devices in the current
/// scope, the models in use, the catalog's manufacturers and the devices added in the last 30 days. It loads its own
/// figures; the view renders it anew on reload.
/// </summary>
public sealed partial class DeviceOverviewSlot
{
    private DeviceOverview? overview;

    /// <summary>What the generated view hands a slot; the overview reads nothing from it.</summary>
    [Parameter]
    public AggregateViewContext? Context { get; set; }

    [Inject]
    private IStringLocalizer Localizer { get; set; } = default!;

    [Inject]
    private ICultureFormatter Formatter { get; set; } = default!;

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    private IEnumerable<(TextDefinition Label, string Value, string Sub)> OverviewCards =>
    [
        (DeviceUiTexts.OverviewDevices, Count(overview?.Devices), Localizer.Text(DeviceUiTexts.OverviewInScope)),
        (DeviceUiTexts.OverviewModelsInUse, Count(overview?.ModelsInUse),
            overview is null ? string.Empty : Localizer.Text(DeviceUiTexts.OverviewOfTotal, new { Total = Formatter.Number(overview.ModelsTotal) })),
        (DeviceUiTexts.OverviewManufacturers, Count(overview?.Manufacturers), Localizer.Text(DeviceUiTexts.OverviewInCatalog)),
        (DeviceUiTexts.OverviewAdded,
            overview is null ? "–" : Localizer.Text(DeviceUiTexts.OverviewAddedCount, new { Count = Formatter.Number(overview.AddedLast30Days) }),
            Localizer.Text(DeviceUiTexts.OverviewLastMonth)),
    ];

    protected override async Task OnInitializedAsync() => overview = await DeviceOverviewLoad.LoadAsync(Dispatcher);

    private string Count(int? value) => value is { } number ? Formatter.Number(number) : "–";
}
