using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.UnresolvedFilters;

// A namespace of its own: a list query's field set is the one declared beside it, so this list's "gadgetId" filter
// does not reach the probe list in the parent namespace.

internal sealed record FilterProbeId(Guid Value);

internal sealed class FilterProbe : AggregateRoot<FilterProbeId>;

public static class FilterProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "FilterProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Filter probe");
}

internal sealed class FilterProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<FilterProbe>
{
    public void Configure(AggregateFeatureBuilder<FilterProbe> builder) =>
        builder.Texts(FilterProbeTexts.Category).Named(FilterProbeTexts.Singular);
}

internal sealed record FilterProbeListItem(Guid Id);

internal sealed record ListFilterProbesQuery : ListQuery<FilterProbeListItem>, IListOf<FilterProbe>
{
    public ListFilterProbesQuery(Paggination paggination)
        : base(paggination)
    {
    }
}

/// <summary>An identifier filter whose id names no root or child entity.</summary>
public static class FilterProbeListFields
{
    public static ListFieldSet Set { get; } =
        ListFieldSet.Create(ListField.FilterOnly("gadgetId", ListFieldKind.Identifier, FilteringType.equals));
}

/// <summary>The unresolved filter, with no picker: refused.</summary>
internal sealed class UnresolvedFilterViewConfiguration : IAggregateViewConfiguration<FilterProbe>
{
    public void Configure(AggregateViewBuilder<FilterProbe> view)
    {
    }
}

/// <summary>The unresolved filter, given a picker component: accepted.</summary>
internal sealed class PickedFilterViewConfiguration : IAggregateViewConfiguration<FilterProbe>
{
    public void Configure(AggregateViewBuilder<FilterProbe> view) =>
        view.List<FilterProbeListItem>(list => list.Filter("gadgetId", typeof(MudBlazor.MudText)));
}
