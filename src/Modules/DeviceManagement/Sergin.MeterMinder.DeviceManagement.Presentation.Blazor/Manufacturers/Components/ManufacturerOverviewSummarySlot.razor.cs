using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOverview;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Components;

/// <summary>
/// The manufacturer detail's overview line while collapsed, in the generated detail view's <c>DetailOverviewSummary</c>
/// slot: the model and device counts, a dash for a missing figure.
/// </summary>
public sealed partial class ManufacturerOverviewSummarySlot
{
    /// <summary>What the generated view hands a slot: the manufacturer overview it loaded, in <c>Overview</c>.</summary>
    [Parameter]
    public AggregateViewContext? Context { get; set; }

    [Inject]
    private IStringLocalizer Localizer { get; set; } = default!;

    [Inject]
    private ICultureFormatter Formatter { get; set; } = default!;

    private ManufacturerOverview? Overview => Context?.Overview as ManufacturerOverview;

    private string Count(int? value) => value is { } number ? Formatter.Number(number) : "–";
}
