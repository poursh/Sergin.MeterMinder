using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Components;

/// <summary>
/// The device list's overview line while collapsed, in the generated list view's <c>ListOverviewSummary</c> slot: the
/// device and model-in-use counts. It renders only while the overview is collapsed, and loads its own figures then.
/// </summary>
public sealed partial class DeviceOverviewSummarySlot
{
    private DeviceOverview? overview;

    /// <summary>What the generated view hands a slot; the summary reads nothing from it.</summary>
    [Parameter]
    public AggregateViewContext? Context { get; set; }

    [Inject]
    private IStringLocalizer Localizer { get; set; } = default!;

    [Inject]
    private ICultureFormatter Formatter { get; set; } = default!;

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    protected override async Task OnInitializedAsync() => overview = await DeviceOverviewLoad.LoadAsync(Dispatcher);

    private string Count(int? value) => value is { } number ? Formatter.Number(number) : "–";
}
