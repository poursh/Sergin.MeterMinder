using Sergin.MeterMinder.IntegrationTests.All.Aggregates.BadOverviewTargets;
using Sergin.MeterMinder.IntegrationTests.All.Aggregates.BrokenChildViews;
using Sergin.MeterMinder.IntegrationTests.All.Aggregates.UnlistedTargets;
using Sergin.MeterMinder.IntegrationTests.All.Aggregates.UnresolvedFilters;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

// Test-only roots, requests and view configurations. The registry tests build their own AggregateViewRegistry from the
// configurations they name (AggregateViewTestRegistry.Build); only the generated-view probe host (ViewProbeTestHost)
// registers the probe into a test host, never into the real one.

// ---- The probe root and its requests ----

internal sealed record ViewProbeId(Guid Value);

internal sealed record ViewProbeName(string Value);

internal sealed class ViewProbe : AggregateRoot<ViewProbeId>;

internal static class ViewProbePermissions
{
    public static PermissionDefinition Read { get; } = PermissionDefinition.Create("permission.tst.view-probes.read", "View view probes");

    public static PermissionDefinition Update { get; } = PermissionDefinition.Create("permission.tst.view-probes.update", "Change view probes");

    public static PermissionDefinition Run { get; } = PermissionDefinition.Create("permission.tst.view-probes.run", "Run view probes");
}

public static class ViewProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "ViewProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "View probe");

    public static TextDefinition Title { get; } = Category.Text("Title", "View probes");

    public static TextDefinition Run { get; } = Category.Text("Run", "Run probe");

    public static FieldText Name { get; } = Category.Field(nameof(Name), "Name");

    public static FieldText OwnerId { get; } = Category.Field(nameof(OwnerId), "Owner");
}

internal sealed class ViewProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<ViewProbe>
{
    public void Configure(AggregateFeatureBuilder<ViewProbe> builder) =>
        builder.Audited().Versioned()
            .Permissions(ViewProbePermissions.Read, ViewProbePermissions.Update, ViewProbePermissions.Run)
            .Texts(ViewProbeTexts.Category)
            .Named(ViewProbeTexts.Singular);
}

/// <summary>Its name is a value object, so a generated view proves it shows the wrapped value, not the record.</summary>
internal sealed record ViewProbeResponse(Guid Id, ViewProbeName Name, int Hits);

internal sealed record GetViewProbeQuery(ViewProbeId Id) : IQuery<ViewProbeResponse>, IGetOneQuery<ViewProbe, ViewProbeId>;

/// <summary>What the probe's entity overview shows: one figure.</summary>
internal sealed record ViewProbeOverview(int Figure);

internal sealed record GetViewProbeOverviewQuery(ViewProbeId Id) : IQuery<ViewProbeOverview>, IEntityOverviewQuery<ViewProbe, ViewProbeId>;

internal sealed record GetViewProbeAuditQuery(ViewProbeId Id) : IQuery<AuditInfo>, IAuditQuery<ViewProbe, ViewProbeId>;

internal sealed record ViewProbeListItem(Guid Id, string Name);

internal sealed record ListViewProbesQuery : ListQuery<ViewProbeListItem>, IListOf<ViewProbe>
{
    public ListViewProbesQuery(Paggination paggination)
        : base(paggination)
    {
    }
}

/// <summary>The probe list's fields: two identifier filters, one naming a root and one a child under it.</summary>
public static class ViewProbeListFields
{
    public static ListFieldSet Set { get; } = ListFieldSet.Create(
        ListField.SortOnly("name"),
        ListField.FilterOnly("viewOwnerId", ListFieldKind.Identifier, FilteringType.equals),
        ListField.FilterOnly("viewPartId", ListFieldKind.Identifier, FilteringType.equals));
}

internal sealed record CreateViewProbeCommand(ViewProbeName Name, ViewOwnerId OwnerId) : ICommand, ICreateCommand<ViewProbe, ViewProbeId>;

internal sealed record RunViewProbeCommand(ViewProbeId Id) : ICommand, ITransitionCommand<ViewProbe, ViewProbeId>, ISelectable;

internal sealed class RunViewProbeCommandConfiguration : ICommandConfiguration<RunViewProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<RunViewProbeCommand> builder) =>
        builder.RequirePermissions(ViewProbePermissions.Run).Schedulable("tst.view-probes.run");
}

internal sealed record DeleteViewProbeCommand(ViewProbeId Id) : ICommand, IDeleteCommand<ViewProbe, ViewProbeId>;

/// <summary>The valid configuration every registry test reads.</summary>
internal sealed class ViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
        view.Title(ViewProbeTexts.Title)
            .Routes("/tst/view-probes", id => $"/tst/view-probes/{id}");

        view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);

        view.Slot<ViewProbeOverviewSlot>(SerginViewSlot.DetailOverview);
    }
}

// ---- Another root with a child: what pickers and filters point at ----

internal sealed record ViewOwnerId(Guid Value);

internal sealed class ViewOwner : AggregateRoot<ViewOwnerId>;

internal sealed record ViewPartId(Guid Value);

internal sealed class ViewPart : Entity<ViewPartId>;

internal sealed record ViewOwnerResponse(Guid Id);

internal sealed record GetViewOwnerQuery(ViewOwnerId Id) : IQuery<ViewOwnerResponse>, IGetOneQuery<ViewOwner, ViewOwnerId>;

internal sealed record ViewOwnerListItem(Guid Id);

/// <summary>What makes ViewOwner pickable: its options come from this list.</summary>
internal sealed record ListViewOwnersQuery : ListQuery<ViewOwnerListItem>, IListOf<ViewOwner>
{
    public ListViewOwnersQuery(Paggination paggination)
        : base(paggination)
    {
    }
}

internal sealed record ViewPartListItem(Guid Id);

/// <summary>What makes ViewPart pickable under a ViewOwner: a list query the generated picker can build and read.</summary>
internal sealed record ListViewPartsQuery : ListQuery<ViewPartListItem>, IChildListOf<ViewOwner, ViewOwnerId, ViewPart>
{
    public ListViewPartsQuery(ViewOwnerId rootId, Paggination paggination)
        : base(paggination) => RootId = rootId;

    public ViewOwnerId RootId { get; }
}

internal sealed record RenameViewOwnerCommand(ViewOwnerId Id) : ICommand, IUpdateCommand<ViewOwner, ViewOwnerId>;

// ---- A root whose form holds a child entity's id ----

internal sealed record PartHolderId(Guid Value);

internal sealed class PartHolder : AggregateRoot<PartHolderId>;

public static class PartHolderTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "PartHolders");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Part holder");

    public static FieldText PartId { get; } = Category.Field(nameof(PartId), "Part");

    /// <summary>The part picker's parent select: a ChildPicker's parent is labelled by the &lt;Parent&gt;Id field.</summary>
    public static FieldText ViewOwnerId { get; } = Category.Field(nameof(ViewOwnerId), "Owner");
}

internal sealed class PartHolderAggregateFeatureConfiguration : IAggregateFeatureConfiguration<PartHolder>
{
    public void Configure(AggregateFeatureBuilder<PartHolder> builder) =>
        builder.Texts(PartHolderTexts.Category).Named(PartHolderTexts.Singular);
}

internal sealed record PartHolderResponse(Guid Id, Guid OwnerId);

internal sealed record CreatePartHolderCommand(ViewPartId PartId) : ICommand, ICreateCommand<PartHolder, PartHolderId>;

/// <summary>Refusal 8: a child entity's id with no ChildPicker.</summary>
internal sealed class PartHolderWithoutPickerViewConfiguration : IAggregateViewConfiguration<PartHolder>
{
    public void Configure(AggregateViewBuilder<PartHolder> view)
    {
    }
}

internal sealed class PartHolderWithPickerViewConfiguration : IAggregateViewConfiguration<PartHolder>
{
    public void Configure(AggregateViewBuilder<PartHolder> view) =>
        view.Form<CreatePartHolderCommand>(form =>
            form.ChildPicker<ViewOwner, PartHolderResponse>(command => command.PartId, record => record.OwnerId));
}

// ---- A root with routes and no queries ----

internal sealed record QuerylessProbeId(Guid Value);

internal sealed class QuerylessProbe : AggregateRoot<QuerylessProbeId>;

public static class QuerylessProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "QuerylessProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Queryless probe");
}

internal sealed class QuerylessProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<QuerylessProbe>
{
    public void Configure(AggregateFeatureBuilder<QuerylessProbe> builder) =>
        builder.Texts(QuerylessProbeTexts.Category).Named(QuerylessProbeTexts.Singular);
}

internal sealed record DeleteQuerylessProbeCommand(QuerylessProbeId Id) : ICommand, IDeleteCommand<QuerylessProbe, QuerylessProbeId>;

/// <summary>Refusal 9.</summary>
internal sealed class QuerylessProbeViewConfiguration : IAggregateViewConfiguration<QuerylessProbe>
{
    public void Configure(AggregateViewBuilder<QuerylessProbe> view) =>
        view.Routes("/tst/queryless-probes", id => $"/tst/queryless-probes/{id}");
}

// ---- Broken probe configurations: each is fed only to the test that needs it ----

/// <summary>Refusal 1, beside <see cref="ViewProbeViewConfiguration"/>.</summary>
internal sealed class SecondViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view) => view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);
}

/// <summary>Refusal 2.</summary>
internal sealed class ArgumentViewProbeViewConfiguration(TextDefinition title) : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view) => view.Title(title);
}

/// <summary>Refusal 3: a command typed to another root.</summary>
internal sealed class ForeignCommandViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
        view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);
        view.Command<RenameViewOwnerCommand>().Label(ViewProbeTexts.Run);
    }
}

/// <summary>Refusal 4: a transition with no label.</summary>
internal sealed class UnlabelledViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
    }
}

/// <summary>Refusal 4 does not apply to a hidden transition.</summary>
internal sealed class HiddenRunViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view) => view.Command<RunViewProbeCommand>().Hidden();
}

/// <summary>Refusal 5: another root's response.</summary>
internal sealed class WrongDetailViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
        view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);
        view.Detail<ViewOwnerResponse>(detail => detail.Field(record => record.Id));
    }
}

/// <summary>Refusal 5, for the list.</summary>
internal sealed class WrongListViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
        view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);
        view.List<ViewProbeResponse>(list => list.Field(item => item.Name));
    }
}

/// <summary>Refusal 6: a field override that is not a property of the record.</summary>
internal sealed class UnknownFieldViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
        view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);
        view.Detail<ViewProbeResponse>(detail => detail.Field(record => record.Name.Value.Length));
    }
}

/// <summary>A field override that hides a labelled field.</summary>
internal sealed class HiddenNameViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
        view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);
        view.Detail<ViewProbeResponse>(detail => detail.Field(record => record.Name).Hidden());
    }
}

/// <summary>Refusal 7: a route the probe page declares (detail) beside one nothing declares (list).</summary>
internal sealed class UnroutedViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
        view.Routes("/tst/nowhere", id => $"/tst/probes/{id}");
        view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);
    }
}

/// <summary>Filter() naming a field the list does not have.</summary>
internal sealed class UnknownFilterViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
        view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);
        view.List<ViewProbeListItem>(list => list.Filter("gadgetId", typeof(MudBlazor.MudText)));
    }
}

// ---- A root whose only requests are a child entity's: the good child probe ----

internal sealed record ChildHostId(Guid Value);

internal sealed class ChildHost : AggregateRoot<ChildHostId>;

internal sealed record ViewProbeChildId(Guid Value);

internal sealed record ViewProbeChildName(string Value);

internal sealed class ViewProbeChild : Entity<ViewProbeChildId>;

internal static class ChildHostPermissions
{
    public static PermissionDefinition Update { get; } = PermissionDefinition.Create("permission.tst.child-hosts.update", "Change child hosts");
}

public static class ChildHostTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "ChildHosts");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Child host");
}

public static class ViewProbeChildTexts
{
    public static TextCategory Category { get; } = ChildHostTexts.Category.Child("Children");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Probe child");

    public static TextDefinition Title { get; } = Category.Text("Title", "Probe children");

    public static TextDefinition Add { get; } = Category.Text("Add", "Add child");

    public static TextDefinition Empty { get; } = Category.Text("Empty", "No children yet.");

    public static TextDefinition UsesColumn { get; } = Category.Text("UsesColumn", "Uses");

    public static TextDefinition UsesFormat { get; } = Category.Text("UsesFormat", "{Count} uses");

    public static TextDefinition InUse { get; } = Category.Text("InUse", "In use");

    public static TextDefinition NewTitle { get; } = Category.Text("NewTitle", "New child");

    public static FieldText Name { get; } = Category.Field(nameof(Name), "Name");
}

internal sealed class ChildHostAggregateFeatureConfiguration : IAggregateFeatureConfiguration<ChildHost>
{
    public void Configure(AggregateFeatureBuilder<ChildHost> builder) =>
        builder.Versioned()
            .Permissions(ChildHostPermissions.Update)
            .Texts(ChildHostTexts.Category)
            .Named(ChildHostTexts.Singular);
}

internal sealed record ViewProbeChildResponse(Guid Id, string Name);

internal sealed record GetViewProbeChildQuery(ChildHostId RootId, ViewProbeChildId Id)
    : IQuery<ViewProbeChildResponse>, IChildGetOneQuery<ChildHost, ChildHostId, ViewProbeChild, ViewProbeChildId>;

internal sealed record ViewProbeChildListItem(Guid Id, string Name, int Uses);

internal sealed record ListViewProbeChildrenQuery : ListQuery<ViewProbeChildListItem>, IChildListOf<ChildHost, ChildHostId, ViewProbeChild>
{
    public ListViewProbeChildrenQuery(ChildHostId rootId, Paggination paggination)
        : base(paggination) => RootId = rootId;

    public ChildHostId RootId { get; }
}

internal sealed record AddViewProbeChildCommand(ChildHostId RootId, ViewProbeChildName Name)
    : ICommand, IAddChildCommand<ChildHost, ChildHostId, ViewProbeChild, ViewProbeChildId>;

internal sealed record RemoveViewProbeChildCommand(ChildHostId RootId, ViewProbeChildId Id)
    : ICommand, IRemoveChildCommand<ChildHost, ChildHostId, ViewProbeChild, ViewProbeChildId>, ISelectable;

internal sealed record RenameViewProbeChildCommand(ChildHostId RootId, ViewProbeChildId Id, ViewProbeChildName Name)
    : ICommand, ISetPropertyCommand<ChildHost, ChildHostId, ViewProbeChild, ViewProbeChildId>;

internal sealed class GetViewProbeChildQueryConfiguration : ICommandConfiguration<GetViewProbeChildQuery>
{
    public void Configure(CommandConfigurationBuilder<GetViewProbeChildQuery> builder) => builder.Texts(ViewProbeChildTexts.Category);
}

internal sealed class ListViewProbeChildrenQueryConfiguration : ICommandConfiguration<ListViewProbeChildrenQuery>
{
    public void Configure(CommandConfigurationBuilder<ListViewProbeChildrenQuery> builder) => builder.Texts(ViewProbeChildTexts.Category);
}

internal sealed class AddViewProbeChildCommandConfiguration : ICommandConfiguration<AddViewProbeChildCommand>
{
    public void Configure(CommandConfigurationBuilder<AddViewProbeChildCommand> builder) =>
        builder.RequirePermissions(ChildHostPermissions.Update).Texts(ViewProbeChildTexts.Category);
}

internal sealed class RemoveViewProbeChildCommandConfiguration : ICommandConfiguration<RemoveViewProbeChildCommand>
{
    public void Configure(CommandConfigurationBuilder<RemoveViewProbeChildCommand> builder) =>
        builder.RequirePermissions(ChildHostPermissions.Update).Texts(ViewProbeChildTexts.Category);
}

internal sealed class RenameViewProbeChildCommandConfiguration : ICommandConfiguration<RenameViewProbeChildCommand>
{
    public void Configure(CommandConfigurationBuilder<RenameViewProbeChildCommand> builder) =>
        builder.RequirePermissions(ChildHostPermissions.Update).Texts(ViewProbeChildTexts.Category);
}

/// <summary>The valid child configuration the broken ones in <c>BrokenChildViews</c> each spoil in one way.</summary>
internal sealed class ChildHostViewConfiguration : IAggregateViewConfiguration<ChildHost>
{
    public void Configure(AggregateViewBuilder<ChildHost> view) =>
        view.Child<ViewProbeChild>(child =>
        {
            child.Named(ViewProbeChildTexts.Singular).Title(ViewProbeChildTexts.Title)
                .Routes(
                    detail: (hostId, id) => $"/tst/child-hosts/{hostId}/children/{id}",
                    create: hostId => $"/tst/child-hosts/{hostId}/children/new",
                    edit: (hostId, id) => $"/tst/child-hosts/{hostId}/children/{id}/edit");

            child.Command<AddViewProbeChildCommand>().Label(ViewProbeChildTexts.Add);
            child.Command<RenameViewProbeChildCommand>().Label(CommonTexts.Rename).Edit(SerginEditMode.Page);
            child.Command<RemoveViewProbeChildCommand>().DisabledWhen<ViewProbeChildListItem>(x => x.Uses > 0, ViewProbeChildTexts.InUse);

            child.List<ViewProbeChildListItem>(list =>
            {
                list.Title(x => x.Name).Empty(ViewProbeChildTexts.Empty);
                list.Field(x => x.Uses).Label(ViewProbeChildTexts.UsesColumn).Format(ViewProbeChildTexts.UsesFormat, "Count");
            });
            child.Detail<ViewProbeChildResponse>(detail => detail.Title(x => x.Name));
            child.Form<AddViewProbeChildCommand>(form => form.Title(ViewProbeChildTexts.NewTitle));
        });
}

/// <summary>The registry every view test builds against: the probe types above, in one test catalog.</summary>
internal static class AggregateViewTestRegistry
{
    private static readonly Type[] aggregateConfigurationTypes =
    [
        typeof(ViewProbeAggregateFeatureConfiguration), typeof(PartHolderAggregateFeatureConfiguration),
        typeof(QuerylessProbeAggregateFeatureConfiguration), typeof(FilterProbeAggregateFeatureConfiguration),
        typeof(UnlistedProbeAggregateFeatureConfiguration), typeof(NoteProbeAggregateFeatureConfiguration),
        typeof(BadQueryProbeAggregateFeatureConfiguration), typeof(PlainProbeAggregateFeatureConfiguration),
        typeof(EditScheduledProbeAggregateFeatureConfiguration), typeof(CtorProbeAggregateFeatureConfiguration),
        typeof(BadOverviewProbeAggregateFeatureConfiguration), typeof(ChildHostAggregateFeatureConfiguration),
        typeof(BareChildHostAggregateFeatureConfiguration), typeof(SetProbeAggregateFeatureConfiguration),
        typeof(EnumGapProbeAggregateFeatureConfiguration),
    ];

    private static readonly Type[] requestAndConfigurationTypes =
    [
        .. aggregateConfigurationTypes, typeof(RunViewProbeCommandConfiguration),
        typeof(GetViewProbeQuery), typeof(GetViewProbeOverviewQuery), typeof(GetViewProbeAuditQuery), typeof(ListViewProbesQuery), typeof(CreateViewProbeCommand), typeof(RunViewProbeCommand),
        typeof(DeleteViewProbeCommand), typeof(GetViewOwnerQuery), typeof(ListViewPartsQuery), typeof(RenameViewOwnerCommand),
        typeof(CreatePartHolderCommand), typeof(DeleteQuerylessProbeCommand), typeof(RunWithNoteNoteProbeCommand), typeof(GetBadQueryProbeQuery), typeof(ListFilterProbesQuery),
        typeof(ListViewOwnersQuery), typeof(ListUnlistedProbesQuery), typeof(CreateUnlistedProbeCommand), typeof(GetUnlistedThingQuery),
        typeof(GetPlainProbeQuery), typeof(ListPlainProbesQuery), typeof(UpdatePlainProbeCommand),
        typeof(UpdateEditScheduledProbeCommand), typeof(UpdateEditScheduledProbeCommandConfiguration), typeof(CreateCtorProbeCommand),
        typeof(GetBadOverviewProbeOverviewQuery), typeof(GetViewProbeChildQuery), typeof(GetViewProbeChildQueryConfiguration),
        typeof(ListViewProbeChildrenQuery), typeof(ListViewProbeChildrenQueryConfiguration), typeof(AddViewProbeChildCommand),
        typeof(AddViewProbeChildCommandConfiguration), typeof(RemoveViewProbeChildCommand), typeof(RemoveViewProbeChildCommandConfiguration),
        typeof(RenameViewProbeChildCommand), typeof(RenameViewProbeChildCommandConfiguration), typeof(ListBareChildrenQuery),
        typeof(ListBareChildrenQueryConfiguration),
        typeof(RenameSetProbeCommand), typeof(RenameSetProbeCommandConfiguration), typeof(GetEnumGapProbeQuery),
    ];

    public static AggregateViewRegistry Build(params Type[] viewConfigurations) =>
        AggregateViewRegistry.Build(
            [AggregateViewSource.FromTypes("tst", viewConfigurations)],
            CommandConfigurationRegistry.FromConfigurationTypes(requestAndConfigurationTypes),
            AggregateFeatureRegistry.FromConfigurationTypes(aggregateConfigurationTypes),
            TextCatalog.FromSources(
            [
                TextSource.Common,
                TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(ViewProbeTexts), typeof(PartHolderTexts), typeof(QuerylessProbeTexts),
                    typeof(FilterProbeTexts), typeof(UnlistedProbeTexts), typeof(NoteProbeTexts), typeof(BadQueryProbeTexts),
                    typeof(PlainProbeTexts), typeof(EditScheduledProbeTexts), typeof(CtorProbeTexts), typeof(BadOverviewProbeTexts),
                    typeof(ChildHostTexts), typeof(ViewProbeChildTexts), typeof(BareChildHostTexts), typeof(SetProbeTexts), typeof(EnumGapProbeTexts)),
            ]));
}

// ---- Refusal: a transition that is not id-only ----

internal sealed record NoteProbeId(Guid Value);

internal sealed class NoteProbe : AggregateRoot<NoteProbeId>;

public static class NoteProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "NoteProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Note probe");
}

internal sealed class NoteProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<NoteProbe>
{
    public void Configure(AggregateFeatureBuilder<NoteProbe> builder) =>
        builder.Texts(NoteProbeTexts.Category).Named(NoteProbeTexts.Singular);
}

internal sealed record RunWithNoteNoteProbeCommand(NoteProbeId Id, string Note) : ICommand, ITransitionCommand<NoteProbe, NoteProbeId>;

internal sealed class RunWithNoteViewProbeViewConfiguration : IAggregateViewConfiguration<NoteProbe>
{
    public void Configure(AggregateViewBuilder<NoteProbe> view) =>
        view.Command<RunWithNoteNoteProbeCommand>().Label(ViewProbeTexts.Run);
}

// ---- Refusal: a GetOne, overview or audit query whose constructor does not fit ----

internal sealed record BadQueryProbeId(Guid Value);

internal sealed class BadQueryProbe : AggregateRoot<BadQueryProbeId>;

public static class BadQueryProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "BadQueryProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Bad query probe");
}

internal sealed class BadQueryProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<BadQueryProbe>
{
    public void Configure(AggregateFeatureBuilder<BadQueryProbe> builder) =>
        builder.Texts(BadQueryProbeTexts.Category).Named(BadQueryProbeTexts.Singular);
}

internal sealed record BadQueryProbeResponse(Guid Id);

internal sealed record GetBadQueryProbeQuery(BadQueryProbeId Id, string Extra) : IQuery<BadQueryProbeResponse>, IGetOneQuery<BadQueryProbe, BadQueryProbeId>;

internal sealed class BadQueryProbeViewConfiguration : IAggregateViewConfiguration<BadQueryProbe>
{
    public void Configure(AggregateViewBuilder<BadQueryProbe> view)
    {
    }
}

// ---- Refusals: routes promising a form with no command behind it ----

/// <summary>A create route on a root with no create command.</summary>
internal sealed class CreateRouteWithoutCommandViewConfiguration : IAggregateViewConfiguration<PlainProbe>
{
    public void Configure(AggregateViewBuilder<PlainProbe> view) =>
        view.Routes("/tst/plain-probes", id => $"/tst/plain-probes/{id}", create: "/tst/plain-probes/new");
}

/// <summary>An edit route on a root with no update command.</summary>
internal sealed class EditRouteWithoutUpdateViewProbeViewConfiguration : IAggregateViewConfiguration<ViewProbe>
{
    public void Configure(AggregateViewBuilder<ViewProbe> view)
    {
        view.Routes("/tst/view-probes", id => $"/tst/view-probes/{id}", edit: id => $"/tst/view-probes/{id}/edit");
        view.Command<RunViewProbeCommand>().Label(ViewProbeTexts.Run);
    }
}

// ---- Refusal: a schedulable command a detail view cannot build from the id ----

internal sealed record EditScheduledProbeId(Guid Value);

internal sealed record EditScheduledProbeName(string Value);

internal sealed class EditScheduledProbe : AggregateRoot<EditScheduledProbeId>;

public static class EditScheduledProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "EditScheduledProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Edit-scheduled probe");

    public static FieldText Name { get; } = Category.Field(nameof(Name), "Name");
}

internal sealed class EditScheduledProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<EditScheduledProbe>
{
    public void Configure(AggregateFeatureBuilder<EditScheduledProbe> builder) =>
        builder.Texts(EditScheduledProbeTexts.Category).Named(EditScheduledProbeTexts.Singular);
}

internal sealed record UpdateEditScheduledProbeCommand(EditScheduledProbeId Id, EditScheduledProbeName Name)
    : ICommand, IUpdateCommand<EditScheduledProbe, EditScheduledProbeId>;

internal sealed class UpdateEditScheduledProbeCommandConfiguration : ICommandConfiguration<UpdateEditScheduledProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<UpdateEditScheduledProbeCommand> builder) =>
        builder.Schedulable("tst.edit-scheduled-probes.update");
}

internal sealed class EditScheduledProbeViewConfiguration : IAggregateViewConfiguration<EditScheduledProbe>
{
    public void Configure(AggregateViewBuilder<EditScheduledProbe> view)
    {
    }
}

// ---- Refusal: a form command with no public constructor ----

internal sealed record CtorProbeId(Guid Value);

internal sealed record CtorProbeName(string Value);

internal sealed class CtorProbe : AggregateRoot<CtorProbeId>;

public static class CtorProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "CtorProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Constructor probe");

    public static FieldText Name { get; } = Category.Field(nameof(Name), "Name");
}

internal sealed class CtorProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<CtorProbe>
{
    public void Configure(AggregateFeatureBuilder<CtorProbe> builder) =>
        builder.Texts(CtorProbeTexts.Category).Named(CtorProbeTexts.Singular);
}

/// <summary>Its only constructor is internal, so a generated form has none to build it with.</summary>
internal sealed record CreateCtorProbeCommand : ICommand, ICreateCommand<CtorProbe, CtorProbeId>
{
    internal CreateCtorProbeCommand(CtorProbeName name) => Name = name;

    public CtorProbeName Name { get; }
}

internal sealed class CtorProbeViewConfiguration : IAggregateViewConfiguration<CtorProbe>
{
    public void Configure(AggregateViewBuilder<CtorProbe> view)
    {
    }
}

// ---- Refusal: a shown enum field with a member that has no value text ----

internal sealed record EnumGapProbeId(Guid Value);

internal sealed class EnumGapProbe : AggregateRoot<EnumGapProbeId>;

public static class EnumGapProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "EnumGapProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Enum gap probe");

    public static FieldText Status { get; } = Category.Field(nameof(Status), "Status");

    /// <summary>Declares the first member only.</summary>
    public static EnumTexts<ProbeEnum> StatusValues { get; } = Category.Values((ProbeEnum.First, "Primary value", null));
}

internal sealed class EnumGapProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<EnumGapProbe>
{
    public void Configure(AggregateFeatureBuilder<EnumGapProbe> builder) =>
        builder.Texts(EnumGapProbeTexts.Category).Named(EnumGapProbeTexts.Singular);
}

internal sealed record EnumGapProbeResponse(Guid Id, ProbeEnum Status);

internal sealed record GetEnumGapProbeQuery(EnumGapProbeId Id) : IQuery<EnumGapProbeResponse>, IGetOneQuery<EnumGapProbe, EnumGapProbeId>;

internal sealed class EnumGapProbeViewConfiguration : IAggregateViewConfiguration<EnumGapProbe>
{
    public void Configure(AggregateViewBuilder<EnumGapProbe> view)
    {
    }
}
