using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.BadOverviewTargets;

// Refusal: an entity overview whose constructor takes more than the id. Its own namespace so no host scans it.

internal sealed record BadOverviewProbeId(Guid Value);

internal sealed class BadOverviewProbe : AggregateRoot<BadOverviewProbeId>;

public static class BadOverviewProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "BadOverviewProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Bad overview probe");
}

internal sealed class BadOverviewProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<BadOverviewProbe>
{
    public void Configure(AggregateFeatureBuilder<BadOverviewProbe> builder) =>
        builder.Texts(BadOverviewProbeTexts.Category).Named(BadOverviewProbeTexts.Singular);
}

internal sealed record BadOverviewProbeOverview(int Figure);

internal sealed record GetBadOverviewProbeOverviewQuery(BadOverviewProbeId Id, string Extra)
    : IQuery<BadOverviewProbeOverview>, IEntityOverviewQuery<BadOverviewProbe, BadOverviewProbeId>;

internal sealed class BadOverviewProbeViewConfiguration : IAggregateViewConfiguration<BadOverviewProbe>
{
    public void Configure(AggregateViewBuilder<BadOverviewProbe> view)
    {
    }
}
