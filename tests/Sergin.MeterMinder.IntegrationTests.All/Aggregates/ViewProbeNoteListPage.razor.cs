using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

public sealed partial class ViewProbeNoteListPage
{
    [Parameter]
    public Guid ProbeId { get; set; }
}
