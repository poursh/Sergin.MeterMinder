using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.ParentedLists;

// View configurations that each break one rule of a list under a parent. Their own namespace, so no host scans them; a
// test feeds a configuration to AggregateViewTestRegistry.Build by name. The valid one is ViewProbeNoteViewConfiguration.

/// <summary>Refusal: a parented list query routed with Routes(list: …), not ListUnder.</summary>
internal sealed class RoutedViewProbeNoteViewConfiguration : IAggregateViewConfiguration<ViewProbeNote>
{
    public void Configure(AggregateViewBuilder<ViewProbeNote> view) =>
        view.Routes("/tst/view-probe-notes", id => $"/tst/view-probe-notes/{id}");
}

/// <summary>Refusal: ListUnder names a parent that is not the list query's.</summary>
internal sealed class WrongParentViewProbeNoteViewConfiguration : IAggregateViewConfiguration<ViewProbeNote>
{
    public void Configure(AggregateViewBuilder<ViewProbeNote> view) =>
        view.ListUnder<ViewOwner>(ownerId => $"/tst/view-owners/{ownerId}/notes");
}

/// <summary>Refusal: ListUnder on a root whose list query has no parent.</summary>
internal sealed class ListUnderViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
        view.ListUnder<ViewOwner>(ownerId => $"/tst/view-owners/{ownerId}/probes");
        view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);
    }
}
