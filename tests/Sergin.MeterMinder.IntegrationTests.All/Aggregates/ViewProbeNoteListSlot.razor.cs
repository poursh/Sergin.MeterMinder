using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

public sealed partial class ViewProbeNoteListSlot
{
    [Parameter]
    public AggregateViewContext? Context { get; set; }

    private string? ParentName => (Context?.Parent as ViewProbeResponse)?.Name.Value;
}
