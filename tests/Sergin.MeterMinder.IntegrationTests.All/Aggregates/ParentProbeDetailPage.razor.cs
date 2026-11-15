using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

public sealed partial class ParentProbeDetailPage
{
    [Parameter]
    public Guid Id { get; set; }
}
