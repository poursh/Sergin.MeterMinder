using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>Draws the probe parent's tags in place of a generated child section: it receives the root's view context.</summary>
public sealed partial class ParentProbeTagsSection
{
    [Parameter]
    public AggregateViewContext Context { get; set; } = default!;

    private string RecordName => Context.Record is ParentProbeResponse parent ? parent.Name : "none";
}
