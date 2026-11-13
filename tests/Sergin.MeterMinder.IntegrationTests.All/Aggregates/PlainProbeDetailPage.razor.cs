using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

public sealed partial class PlainProbeDetailPage
{
    [Parameter]
    public Guid Id { get; set; }
}
