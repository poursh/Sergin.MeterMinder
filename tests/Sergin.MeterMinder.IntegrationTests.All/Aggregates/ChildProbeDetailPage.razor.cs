using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

public sealed partial class ChildProbeDetailPage
{
    [Parameter]
    public Guid Id { get; set; }
}
