using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

public sealed partial class ScheduleProbeNoteField
{
    [Parameter]
    public string Value { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }
}
