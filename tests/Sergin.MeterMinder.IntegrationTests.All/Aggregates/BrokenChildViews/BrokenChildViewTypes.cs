using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.BrokenChildViews;

// Child-entity view configurations that each break one rule, and the types two of them need: a root whose child has no
// add command and no detail query, and a root with a set-property command. Their own namespace, so no host scans them;
// a test feeds a configuration to AggregateViewTestRegistry.Build by name. The valid child probe is ChildHost.

// ---- A child with a list and a detail query but no add command ----

internal sealed record BareChildHostId(Guid Value);

internal sealed class BareChildHost : AggregateRoot<BareChildHostId>;

internal sealed record BareChildId(Guid Value);

internal sealed class BareChild : Entity<BareChildId>;

public static class BareChildHostTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "BareChildHosts");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Bare child host");

    public static TextDefinition ChildSingular { get; } = Category.Text("ChildSingular", "Bare child");
}

internal sealed class BareChildHostAggregateFeatureConfiguration : IAggregateFeatureConfiguration<BareChildHost>
{
    public void Configure(AggregateFeatureBuilder<BareChildHost> builder) =>
        builder.Texts(BareChildHostTexts.Category).Named(BareChildHostTexts.Singular);
}

internal sealed record BareChildResponse(Guid Id);

/// <summary>A list query only: the child has no detail query and no add command.</summary>
internal sealed record ListBareChildrenQuery : ListQuery<BareChildResponse>, IChildListOf<BareChildHost, BareChildHostId, BareChild>
{
    public ListBareChildrenQuery(BareChildHostId rootId, Paggination paggination)
        : base(paggination) => RootId = rootId;

    public BareChildHostId RootId { get; }
}

internal sealed class ListBareChildrenQueryConfiguration : ICommandConfiguration<ListBareChildrenQuery>
{
    public void Configure(CommandConfigurationBuilder<ListBareChildrenQuery> builder) => builder.Texts(BareChildHostTexts.Category);
}

// ---- A root with a set-property command ----

internal sealed record SetProbeId(Guid Value);

internal sealed class SetProbe : AggregateRoot<SetProbeId>;

public static class SetProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "SetProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Set probe");

    public static FieldText Name { get; } = Category.Field(nameof(Name), "Name");
}

internal sealed class SetProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<SetProbe>
{
    public void Configure(AggregateFeatureBuilder<SetProbe> builder) => builder.Texts(SetProbeTexts.Category).Named(SetProbeTexts.Singular);
}

internal sealed record RenameSetProbeCommand(SetProbeId Id, string Name) : ICommand, ISetPropertyCommand<SetProbe, SetProbeId>;

internal sealed class RenameSetProbeCommandConfiguration : ICommandConfiguration<RenameSetProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<RenameSetProbeCommand> builder) => builder.Texts(SetProbeTexts.Category);
}

/// <summary>Its record has no Name property, so the set-property command cannot be edited inline.</summary>
internal sealed record SetProbeResponse(Guid Id, string Label);

internal sealed record GetSetProbeQuery(SetProbeId Id) : IQuery<SetProbeResponse>, IGetOneQuery<SetProbe, SetProbeId>;

// ---- A root whose set-property command sets two values ----

internal sealed record PairSetProbeId(Guid Value);

internal sealed class PairSetProbe : AggregateRoot<PairSetProbeId>;

public static class PairSetProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "PairSetProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Pair set probe");

    public static FieldText Left { get; } = Category.Field(nameof(Left), "Left");

    public static FieldText Right { get; } = Category.Field(nameof(Right), "Right");
}

internal sealed class PairSetProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<PairSetProbe>
{
    public void Configure(AggregateFeatureBuilder<PairSetProbe> builder) =>
        builder.Texts(PairSetProbeTexts.Category).Named(PairSetProbeTexts.Singular);
}

internal sealed record SetPairSetProbeCommand(PairSetProbeId Id, string Left, string Right) : ICommand, ISetPropertyCommand<PairSetProbe, PairSetProbeId>;

/// <summary>Declares nothing: the command is offered in a dialog, which edits one value.</summary>
internal sealed class PairSetPropertyViewConfiguration : IAggregateViewConfiguration<PairSetProbe>
{
    public void Configure(AggregateViewBuilder<PairSetProbe> view)
    {
    }
}

/// <summary>A set-property command declared Inline whose value the GetOne response does not carry.</summary>
internal sealed class InlineWithoutResponsePropertyViewConfiguration : IAggregateViewConfiguration<SetProbe>
{
    public void Configure(AggregateViewBuilder<SetProbe> view) =>
        view.Command<RenameSetProbeCommand>().Label(CommonTexts.Rename).Edit(SerginEditMode.Inline);
}

// ---- The broken configurations ----

/// <summary>The root's requests name a child its view never declares: it has no name.</summary>
internal sealed class ChildWithoutNameViewConfiguration : IAggregateViewConfiguration<ChildHost>
{
    public void Configure(AggregateViewBuilder<ChildHost> view)
    {
    }
}

/// <summary>A child declared on a root none of whose requests names it.</summary>
internal sealed class NotAChildViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
        view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);
        view.Child<ViewProbeChild>(child => child.Named(ViewProbeChildTexts.Singular));
    }
}

/// <summary>A child command typed to another root.</summary>
internal sealed class ChildCommandOfAnotherRootViewConfiguration : IAggregateViewConfiguration<BareChildHost>
{
    public void Configure(AggregateViewBuilder<BareChildHost> view) =>
        view.Child<BareChild>(child =>
        {
            child.Named(BareChildHostTexts.ChildSingular);
            child.Command<AddViewProbeChildCommand>().Label(ViewProbeChildTexts.Add);
        });
}

/// <summary>A field override on a property the child's response lacks.</summary>
internal sealed class ChildUnknownFieldViewConfiguration : IAggregateViewConfiguration<ChildHost>
{
    public void Configure(AggregateViewBuilder<ChildHost> view) =>
        view.Child<ViewProbeChild>(child =>
        {
            child.Named(ViewProbeChildTexts.Singular);
            child.Detail<ViewProbeChildResponse>(detail => detail.Field(record => record.Name.Length));
        });
}

/// <summary>A child's set-property command left in the default (dialog) mode.</summary>
internal sealed class ChildSetPropertyWithoutPageModeViewConfiguration : IAggregateViewConfiguration<ChildHost>
{
    public void Configure(AggregateViewBuilder<ChildHost> view) =>
        view.Child<ViewProbeChild>(child =>
        {
            child.Named(ViewProbeChildTexts.Singular);
            child.Command<RenameViewProbeChildCommand>().Label(CommonTexts.Rename);
        });
}

/// <summary>A Page-mode command and no edit route.</summary>
internal sealed class ChildPageModeWithoutEditRouteViewConfiguration : IAggregateViewConfiguration<ChildHost>
{
    public void Configure(AggregateViewBuilder<ChildHost> view) =>
        view.Child<ViewProbeChild>(child =>
        {
            child.Named(ViewProbeChildTexts.Singular).Routes(detail: (hostId, id) => $"/tst/child-hosts/{hostId}/children/{id}");
            child.Command<RenameViewProbeChildCommand>().Label(CommonTexts.Rename).Edit(SerginEditMode.Page);
        });
}

/// <summary>Two commands that edit on a page of their own.</summary>
internal sealed class ChildTwoPageModeCommandsViewConfiguration : IAggregateViewConfiguration<ChildHost>
{
    public void Configure(AggregateViewBuilder<ChildHost> view) =>
        view.Child<ViewProbeChild>(child =>
        {
            child.Named(ViewProbeChildTexts.Singular).Routes(edit: (hostId, id) => $"/tst/child-hosts/{hostId}/children/{id}/edit");
            child.Command<RenameViewProbeChildCommand>().Label(CommonTexts.Rename).Edit(SerginEditMode.Page);
            child.Command<RemoveViewProbeChildCommand>().Edit(SerginEditMode.Page);
        });
}

/// <summary>A create route on a child with no add command.</summary>
internal sealed class ChildCreateRouteWithoutAddCommandViewConfiguration : IAggregateViewConfiguration<BareChildHost>
{
    public void Configure(AggregateViewBuilder<BareChildHost> view) =>
        view.Child<BareChild>(child =>
            child.Named(BareChildHostTexts.ChildSingular).Routes(create: hostId => $"/tst/bare-child-hosts/{hostId}/children/new"));
}

/// <summary>A detail route on a child with no detail query.</summary>
internal sealed class ChildDetailRouteWithoutQueryViewConfiguration : IAggregateViewConfiguration<BareChildHost>
{
    public void Configure(AggregateViewBuilder<BareChildHost> view) =>
        view.Child<BareChild>(child =>
            child.Named(BareChildHostTexts.ChildSingular).Routes(detail: (hostId, id) => $"/tst/bare-child-hosts/{hostId}/children/{id}"));
}

/// <summary>A disabled-when over a record that none of the command's views hold.</summary>
internal sealed class ChildDisabledWhenOnForeignRecordViewConfiguration : IAggregateViewConfiguration<ChildHost>
{
    public void Configure(AggregateViewBuilder<ChildHost> view) =>
        view.Child<ViewProbeChild>(child =>
        {
            child.Named(ViewProbeChildTexts.Singular);
            child.Command<RenameViewProbeChildCommand>().Label(CommonTexts.Rename).Edit(SerginEditMode.Page);
            child.Command<RemoveViewProbeChildCommand>().DisabledWhen<ViewProbeResponse>(record => record.Hits > 0);
        });
}

/// <summary>A set-property command on the root itself in Page mode, which only a child's accepts.</summary>
internal sealed class RootSetPropertyViewConfiguration : IAggregateViewConfiguration<SetProbe>
{
    public void Configure(AggregateViewBuilder<SetProbe> view) =>
        view.Command<RenameSetProbeCommand>().Label(CommonTexts.Rename).Edit(SerginEditMode.Page);
}

/// <summary>A detail route that writes the child's id where the root's belongs.</summary>
internal sealed class ChildRouteWithWrongIdViewConfiguration : IAggregateViewConfiguration<ChildHost>
{
    public void Configure(AggregateViewBuilder<ChildHost> view) =>
        view.Child<ViewProbeChild>(child =>
        {
            child.Named(ViewProbeChildTexts.Singular).Routes(
                detail: (_, id) => $"/tst/child-hosts/{id}/children/{id}",
                edit: (hostId, id) => $"/tst/child-hosts/{hostId}/children/{id}/edit");
            child.Command<RenameViewProbeChildCommand>().Label(CommonTexts.Rename).Edit(SerginEditMode.Page);
        });
}

/// <summary>A child list that asks for a filter picker, which a child list section never shows.</summary>
internal sealed class ChildListFilterViewConfiguration : IAggregateViewConfiguration<ChildHost>
{
    public void Configure(AggregateViewBuilder<ChildHost> view) =>
        view.Child<ViewProbeChild>(child =>
        {
            child.Named(ViewProbeChildTexts.Singular).Routes(edit: (hostId, id) => $"/tst/child-hosts/{hostId}/children/{id}/edit");
            child.Command<RenameViewProbeChildCommand>().Label(CommonTexts.Rename).Edit(SerginEditMode.Page);
            child.List<ViewProbeChildListItem>(list => list.Filter("name", typeof(ViewProbeOverviewSlot)));
        });
}
