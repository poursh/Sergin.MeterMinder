using Sergin.MeterMinder.IntegrationTests.All.Aggregates.UnresolvedFilters;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

// Test-only roots, requests and view configurations. Nothing here is registered into a host: each test builds its own
// AggregateViewRegistry from the configurations it names (AggregateViewTestRegistry.Build).

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

    public static TextDefinition Run { get; } = Category.Text("Run", "Run");

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

internal sealed record ViewProbeResponse(Guid Id, string Name, int Hits);

internal sealed record GetViewProbeQuery(ViewProbeId Id) : IQuery<ViewProbeResponse>, IGetOneQuery<ViewProbe, ViewProbeId>;

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
    }
}

// ---- Another root with a child: what pickers and filters point at ----

internal sealed record ViewOwnerId(Guid Value);

internal sealed class ViewOwner : AggregateRoot<ViewOwnerId>;

internal sealed record ViewPartId(Guid Value);

internal sealed class ViewPart : Entity<ViewPartId>;

internal sealed record ViewOwnerResponse(Guid Id);

internal sealed record GetViewOwnerQuery(ViewOwnerId Id) : IQuery<ViewOwnerResponse>, IGetOneQuery<ViewOwner, ViewOwnerId>;

internal sealed record ListViewPartsQuery(ViewOwnerId RootId) : IQuery<IReadOnlyList<Guid>>, IChildListOf<ViewOwner, ViewOwnerId, ViewPart>;

internal sealed record RenameViewOwnerCommand(ViewOwnerId Id) : ICommand, IUpdateCommand<ViewOwner, ViewOwnerId>;

// ---- A root whose form holds a child entity's id ----

internal sealed record PartHolderId(Guid Value);

internal sealed class PartHolder : AggregateRoot<PartHolderId>;

public static class PartHolderTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "PartHolders");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Part holder");

    public static FieldText PartId { get; } = Category.Field(nameof(PartId), "Part");
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
        view.Detail<ViewProbeResponse>(detail => detail.Field(record => record.Name.Length));
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

/// <summary>The registry every view test builds against: the probe types above, in one test catalog.</summary>
internal static class AggregateViewTestRegistry
{
    private static readonly Type[] aggregateConfigurationTypes =
    [
        typeof(ViewProbeAggregateFeatureConfiguration), typeof(PartHolderAggregateFeatureConfiguration),
        typeof(QuerylessProbeAggregateFeatureConfiguration), typeof(FilterProbeAggregateFeatureConfiguration),
    ];

    private static readonly Type[] requestAndConfigurationTypes =
    [
        .. aggregateConfigurationTypes, typeof(RunViewProbeCommandConfiguration),
        typeof(GetViewProbeQuery), typeof(ListViewProbesQuery), typeof(CreateViewProbeCommand), typeof(RunViewProbeCommand),
        typeof(DeleteViewProbeCommand), typeof(GetViewOwnerQuery), typeof(ListViewPartsQuery), typeof(RenameViewOwnerCommand),
        typeof(CreatePartHolderCommand), typeof(DeleteQuerylessProbeCommand), typeof(ListFilterProbesQuery),
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
                    typeof(FilterProbeTexts)),
            ]));
}
