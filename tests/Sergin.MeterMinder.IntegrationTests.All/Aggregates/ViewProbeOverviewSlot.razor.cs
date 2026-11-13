using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

public sealed partial class ViewProbeOverviewSlot
{
    [Parameter]
    public AggregateViewContext? Context { get; set; }

    private ViewProbeOverview? Overview => (ViewProbeOverview?)Context?.Overview;
}
