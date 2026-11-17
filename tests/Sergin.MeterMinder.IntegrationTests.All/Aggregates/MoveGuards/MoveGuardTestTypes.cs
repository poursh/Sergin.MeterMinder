using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.MoveGuards;

// The refusals of moves and of children drawn by their own component: a move on a root with no tree query, or whose tree
// items carry no ParentId, or with no label; and a Section child that also declares what only generated child views read.
// Their own namespace, so no host scans them; a test feeds a configuration to AggregateViewTestRegistry.Build by name.
// Test-only roots throughout.

// ---- Refusal: a move with no tree to pick the parent from ----

internal sealed record TreelessProbeId(Guid Value);

internal sealed class TreelessProbe : AggregateRoot<TreelessProbeId>;

public static class TreelessProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "TreelessProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Treeless probe");

    public static TextDefinition Move { get; } = Category.Text("Move", "Move treeless");

    public static FieldText NewParentId { get; } = Category.Field(nameof(NewParentId), "Move under");
}

internal sealed class TreelessProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<TreelessProbe>
{
    public void Configure(AggregateFeatureBuilder<TreelessProbe> builder) =>
        builder.Texts(TreelessProbeTexts.Category).Named(TreelessProbeTexts.Singular);
}

internal sealed record MoveTreelessProbeCommand(TreelessProbeId Id, TreelessProbeId NewParentId) : ICommand, IMoveCommand<TreelessProbe, TreelessProbeId>;

internal sealed class MoveWithoutTreeQueryViewConfiguration : IAggregateViewConfiguration<TreelessProbe>
{
    public void Configure(AggregateViewBuilder<TreelessProbe> view) =>
        view.Command<MoveTreelessProbeCommand>().Label(TreelessProbeTexts.Move);
}

// ---- Refusal: a move whose tree items carry no ParentId ----

internal sealed record FlatTreeProbeId(Guid Value);

internal sealed class FlatTreeProbe : AggregateRoot<FlatTreeProbeId>;

public static class FlatTreeProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "FlatTreeProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Flat tree probe");

    public static TextDefinition Move { get; } = Category.Text("Move", "Move flat");

    public static FieldText NewParentId { get; } = Category.Field(nameof(NewParentId), "Move under");
}

internal sealed class FlatTreeProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<FlatTreeProbe>
{
    public void Configure(AggregateFeatureBuilder<FlatTreeProbe> builder) =>
        builder.Texts(FlatTreeProbeTexts.Category).Named(FlatTreeProbeTexts.Singular);
}

internal sealed record FlatTreeItem(Guid Id, string Name);

internal sealed record GetFlatTreeProbeTreeQuery : IQuery<IReadOnlyCollection<FlatTreeItem>>, ITreeQuery<FlatTreeProbe>;

internal sealed record MoveFlatTreeProbeCommand(FlatTreeProbeId Id, FlatTreeProbeId NewParentId) : ICommand, IMoveCommand<FlatTreeProbe, FlatTreeProbeId>;

internal sealed class MoveTreeItemWithoutParentIdViewConfiguration : IAggregateViewConfiguration<FlatTreeProbe>
{
    public void Configure(AggregateViewBuilder<FlatTreeProbe> view) =>
        view.Command<MoveFlatTreeProbeCommand>().Label(FlatTreeProbeTexts.Move);
}

// ---- Refusal: a move with no label ----

/// <summary>The probe parent with its move left unlabelled.</summary>
internal sealed class MoveWithoutLabelViewConfiguration : IAggregateViewConfiguration<ParentProbe>
{
    public void Configure(AggregateViewBuilder<ParentProbe> view) =>
        view.Child<ParentProbeTag>(child => child.Named(ParentProbeTexts.Tag).Section<ParentProbeTagsSection>());
}

// ---- Refusals: a Section child that declares what only the generated child views read ----

/// <summary>The probe parent's section child, its move hidden, spoiled by <paramref name="spoil"/>.</summary>
internal abstract class SectionSpoiledViewConfiguration(Action<ChildViewBuilder<ParentProbe, ParentProbeTag>> spoil)
    : IAggregateViewConfiguration<ParentProbe>
{
    public void Configure(AggregateViewBuilder<ParentProbe> view)
    {
        view.Command<MoveParentProbeCommand>().Hidden();
        view.Child<ParentProbeTag>(child =>
        {
            child.Named(ParentProbeTexts.Tag).Section<ParentProbeTagsSection>();
            spoil(child);
        });
    }
}

internal sealed class SectionWithRoutesViewConfiguration()
    : SectionSpoiledViewConfiguration(child => child.Routes(create: id => $"/tst/probe-parents/{id}/tags/new"));

internal sealed class SectionWithListViewConfiguration()
    : SectionSpoiledViewConfiguration(child => child.List<ParentProbeTagItem>(list => list.Title(x => x.Label)));

internal sealed class SectionWithDetailViewConfiguration()
    : SectionSpoiledViewConfiguration(child => child.Detail<ParentProbeTagItem>(detail => detail.Title(x => x.Label)));

internal sealed class SectionWithFormViewConfiguration()
    : SectionSpoiledViewConfiguration(child => child.Form<AddParentProbeTagCommand>(form => form.Title(ParentProbeTexts.Tag)));

internal sealed class SectionWithCommandViewConfiguration()
    : SectionSpoiledViewConfiguration(child => child.Command<AddParentProbeTagCommand>().Label(ParentProbeTexts.Tag));

internal static class MoveGuardTypes
{
    public static Type[] AggregateConfigurations { get; } =
        [typeof(TreelessProbeAggregateFeatureConfiguration), typeof(FlatTreeProbeAggregateFeatureConfiguration)];

    public static Type[] Requests { get; } =
        [typeof(MoveTreelessProbeCommand), typeof(GetFlatTreeProbeTreeQuery), typeof(MoveFlatTreeProbeCommand)];

    public static Type[] Texts { get; } = [typeof(TreelessProbeTexts), typeof(FlatTreeProbeTexts)];
}
