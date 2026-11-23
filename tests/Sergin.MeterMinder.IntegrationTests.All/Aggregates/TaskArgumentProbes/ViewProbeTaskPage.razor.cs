using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.TaskArgumentProbes;

public sealed partial class ViewProbeTaskPage
{
    [Parameter]
    public Guid Id { get; set; }
}
