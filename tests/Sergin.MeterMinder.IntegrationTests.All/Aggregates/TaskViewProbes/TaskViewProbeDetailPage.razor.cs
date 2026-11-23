using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.TaskViewProbes;

public sealed partial class TaskViewProbeDetailPage
{
    [Parameter]
    public Guid Id { get; set; }
}
