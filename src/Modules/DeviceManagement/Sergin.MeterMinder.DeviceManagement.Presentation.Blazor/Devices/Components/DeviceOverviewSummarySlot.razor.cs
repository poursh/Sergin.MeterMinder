using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Components;

/// <summary>
/// The device list's overview line while collapsed, in the generated list view's <c>ListOverviewSummary</c> slot: the
/// device and model-in-use counts. It renders only while the overview is collapsed.
/// </summary>
public sealed partial class DeviceOverviewSummarySlot
{
    /// <summary>What the generated view hands a slot: the device overview it loaded, in <c>Overview</c>.</summary>
    [Parameter]
    public AggregateViewContext? Context { get; set; }

    [Inject]
    private IStringLocalizer Localizer { get; set; } = default!;

    [Inject]
    private ICultureFormatter Formatter { get; set; } = default!;

    // The generated view loads the overview once and hands it to both slots; a failed read leaves it null.
    private DeviceOverview? Overview => Context?.Overview as DeviceOverview;

    private string Count(int? value) => value is { } number ? Formatter.Number(number) : "–";
}
