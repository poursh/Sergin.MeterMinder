using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.UnlistedTargets;

// A target a typed request names but no list query lists: a GetOne query is all UnlistedThing has (Scope's shape: a
// tree query, no IListOf). A filter or form picker naming it has nothing to fill its options with. Its own namespace,
// because a list query's field set is the one declared beside it.

internal sealed record UnlistedThingId(Guid Value);

internal sealed class UnlistedThing : AggregateRoot<UnlistedThingId>;

internal sealed record UnlistedThingResponse(Guid Id);

internal sealed record GetUnlistedThingQuery(UnlistedThingId Id) : IQuery<UnlistedThingResponse>, IGetOneQuery<UnlistedThing, UnlistedThingId>;

internal sealed record UnlistedProbeId(Guid Value);

internal sealed class UnlistedProbe : AggregateRoot<UnlistedProbeId>;

public static class UnlistedProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "UnlistedProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Unlisted probe");

    public static FieldText ThingId { get; } = Category.Field(nameof(ThingId), "Thing");
}

internal sealed class UnlistedProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<UnlistedProbe>
{
    public void Configure(AggregateFeatureBuilder<UnlistedProbe> builder) =>
        builder.Texts(UnlistedProbeTexts.Category).Named(UnlistedProbeTexts.Singular);
}

internal sealed record UnlistedProbeListItem(Guid Id);

internal sealed record ListUnlistedProbesQuery : ListQuery<UnlistedProbeListItem>, IListOf<UnlistedProbe>
{
    public ListUnlistedProbesQuery(Paggination paggination)
        : base(paggination)
    {
    }
}

/// <summary>An identifier filter whose id names a root with no list query.</summary>
public static class UnlistedProbeListFields
{
    public static ListFieldSet Set { get; } =
        ListFieldSet.Create(ListField.FilterOnly("unlistedThingId", ListFieldKind.Identifier, FilteringType.equals));
}

/// <summary>A form field of the unlisted root's id type.</summary>
internal sealed record CreateUnlistedProbeCommand(UnlistedThingId ThingId) : ICommand, ICreateCommand<UnlistedProbe, UnlistedProbeId>;

/// <summary>The filter is picked, so only the form field is refused.</summary>
internal sealed class UnlistedPickerViewConfiguration : IAggregateViewConfiguration<UnlistedProbe>
{
    public void Configure(AggregateViewBuilder<UnlistedProbe> view) =>
        view.List<UnlistedProbeListItem>(list => list.Filter("unlistedThingId", typeof(MudBlazor.MudText)));
}

/// <summary>Neither picked nor listed: the filter is refused first.</summary>
internal sealed class UnlistedFilterViewConfiguration : IAggregateViewConfiguration<UnlistedProbe>
{
    public void Configure(AggregateViewBuilder<UnlistedProbe> view)
    {
    }
}
