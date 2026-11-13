using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOverview;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Components;

/// <summary>
/// The manufacturer detail's overview cards, in the generated detail view's <c>DetailOverview</c> slot: the models of the
/// manufacturer and its devices in every scope. It reads the figures the view loaded; a missing figure shows as a dash.
/// </summary>
public sealed partial class ManufacturerOverviewSlot
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
