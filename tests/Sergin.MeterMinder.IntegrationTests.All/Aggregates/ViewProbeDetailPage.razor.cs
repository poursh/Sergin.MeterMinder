using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

public sealed partial class ViewProbeDetailPage
{
    [Parameter]
    public Guid Id { get; set; }
}
