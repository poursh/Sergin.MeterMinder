using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Configuration;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Model;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.HardenedGuards;

// The refusals added when plan 4 hardened the generated views: an inline choice with nowhere to render, AppliesWhen on a
// child pair, an overview on a root listed under a parent. Their own namespace, so no host scans them; a test feeds a
// configuration to AggregateViewTestRegistry.Build by name. Test-only roots throughout.

// ---- Refusal: an inline choice whose detail field is hidden, or replaced by a slot ----

/// <summary>The color is an inline select, but its detail field is hidden, so the select would render nowhere.</summary>
internal sealed class InlineChoiceHiddenViewConfiguration : IAggregateViewConfiguration<EditProbe>
{
    public void Configure(AggregateViewBuilder<EditProbe> view)
    {
        view.Command<SetProbeColorCommand>().Edit(SerginEditMode.Inline);
        view.Detail<EditProbeResponse>(detail => detail.Field(x => x.Color).Hidden());
    }
}

/// <summary>The color is an inline select, but a DetailFields slot replaces the fields it would render in.</summary>
internal sealed class InlineChoiceInSlotViewConfiguration : IAggregateViewConfiguration<EditProbe>
{
    public void Configure(AggregateViewBuilder<EditProbe> view)
    {
        view.Command<SetProbeColorCommand>().Edit(SerginEditMode.Inline);
        view.Slot<ViewProbeOverviewSlot>(SerginViewSlot.DetailFields);
    }
}

// ---- A child entity with an IReverseOf pair of transitions ----

internal sealed record PairChildHostId(Guid Value);

internal sealed class PairChildHost : AggregateRoot<PairChildHostId>;

internal sealed record PairChildId(Guid Value);

internal sealed class PairChild : Entity<PairChildId>;

public static class PairChildHostTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "PairChildHosts");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Pair child host");

    public static TextDefinition ChildSingular { get; } = Category.Text("ChildSingular", "Pair child");

    public static TextDefinition Activate { get; } = Category.Text("Activate", "Activate");

    public static TextDefinition Deactivate { get; } = Category.Text("Deactivate", "Deactivate");

    public static FieldText Name { get; } = Category.Field(nameof(Name), "Name");
}

internal sealed class PairChildHostAggregateFeatureConfiguration : IAggregateFeatureConfiguration<PairChildHost>
{
    public void Configure(AggregateFeatureBuilder<PairChildHost> builder) =>
        builder.Texts(PairChildHostTexts.Category).Named(PairChildHostTexts.Singular);
}

internal sealed record PairChildListItem(Guid Id, string Name, bool Active);

internal sealed record ListPairChildrenQuery : ListQuery<PairChildListItem>, IChildListOf<PairChildHost, PairChildHostId, PairChild>
{
    public ListPairChildrenQuery(PairChildHostId rootId, Paggination paggination)
        : base(paggination) => RootId = rootId;

    public PairChildHostId RootId { get; }
}

internal sealed record ActivatePairChildCommand(PairChildHostId RootId, PairChildId Id)
    : ICommand, ITransitionCommand<PairChildHost, PairChildHostId, PairChild, PairChildId>;

internal sealed record DeactivatePairChildCommand(PairChildHostId RootId, PairChildId Id)
    : ICommand, ITransitionCommand<PairChildHost, PairChildHostId, PairChild, PairChildId>, IReverseOf<ActivatePairChildCommand>;

/// <summary>Refusal: a child pair declaring AppliesWhen, which child views do not apply.</summary>
internal sealed class ChildPairWithAppliesWhenViewConfiguration : IAggregateViewConfiguration<PairChildHost>
{
    public void Configure(AggregateViewBuilder<PairChildHost> view) =>
        view.Child<PairChild>(child =>
        {
            child.Named(PairChildHostTexts.ChildSingular);
            child.Command<ActivatePairChildCommand>().Label(PairChildHostTexts.Activate).AppliesWhen<PairChildListItem>(item => !item.Active);
            child.Command<DeactivatePairChildCommand>().Label(PairChildHostTexts.Deactivate);
        });
}

/// <summary>The valid child pair: neither command declares AppliesWhen, so a child view offers both.</summary>
internal sealed class ChildPairWithoutAppliesWhenViewConfiguration : IAggregateViewConfiguration<PairChildHost>
{
    public void Configure(AggregateViewBuilder<PairChildHost> view) =>
        view.Child<PairChild>(child =>
        {
            child.Named(PairChildHostTexts.ChildSingular);
            child.Command<ActivatePairChildCommand>().Label(PairChildHostTexts.Activate);
            child.Command<DeactivatePairChildCommand>().Label(PairChildHostTexts.Deactivate);
        });
}

// ---- Refusal: an overview on a root listed under a parent ----

internal sealed record OverviewNoteId(Guid Value);

internal sealed class OverviewNote : AggregateRoot<OverviewNoteId>;

public static class OverviewNoteTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "OverviewNotes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Overview note");

    public static FieldText Text { get; } = Category.Field(nameof(Text), "Text");
}

internal sealed class OverviewNoteAggregateFeatureConfiguration : IAggregateFeatureConfiguration<OverviewNote>
{
    public void Configure(AggregateFeatureBuilder<OverviewNote> builder) =>
        builder.Texts(OverviewNoteTexts.Category).Named(OverviewNoteTexts.Singular);
}

internal sealed record OverviewNoteListItem(Guid Id, string Text);

internal sealed record ListOverviewNotesQuery : ListQuery<OverviewNoteListItem>, IListOf<OverviewNote>, IUnderParent<ViewProbe, ViewProbeId>
{
    public ListOverviewNotesQuery(ViewProbeId parentId, Paggination paggination)
        : base(paggination) => ParentId = parentId;

    public ViewProbeId ParentId { get; init; }
}

internal sealed record OverviewNoteOverview(int Count);

/// <summary>An overview of the whole collection, which a list under a parent would send without the parent's id.</summary>
internal sealed record GetOverviewNotesOverviewQuery : IQuery<OverviewNoteOverview>, IOverviewQuery<OverviewNote>;

internal sealed class OverviewNoteViewConfiguration : IAggregateViewConfiguration<OverviewNote>
{
    public void Configure(AggregateViewBuilder<OverviewNote> view) =>
        view.ListUnder<ViewProbe>(probeId => $"/tst/view-probes/{probeId}/overview-notes");
}

/// <summary>What <see cref="AggregateViewTestRegistry"/> adds for the roots above.</summary>
internal static class HardenedGuardTypes
{
    public static Type[] AggregateConfigurations { get; } =
        [typeof(PairChildHostAggregateFeatureConfiguration), typeof(OverviewNoteAggregateFeatureConfiguration)];

    public static Type[] Requests { get; } =
    [
        typeof(ListPairChildrenQuery), typeof(ActivatePairChildCommand), typeof(DeactivatePairChildCommand),
        typeof(ListOverviewNotesQuery), typeof(GetOverviewNotesOverviewQuery),
    ];

    public static Type[] Texts { get; } = [typeof(PairChildHostTexts), typeof(OverviewNoteTexts)];
}
