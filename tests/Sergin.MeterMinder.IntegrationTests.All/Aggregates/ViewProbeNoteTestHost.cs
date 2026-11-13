using System.Collections.Concurrent;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>The parent ids the notes' list queries were sent with, in order.</summary>
internal sealed class ViewProbeNoteQueries
{
    public ConcurrentQueue<Guid> ParentIds { get; } = new();
}

/// <summary>Records each list query's parent id and answers no rows.</summary>
internal sealed class ListViewProbeNotesQueryHandler(ViewProbeNoteQueries queries)
    : IRequestHandler<ListViewProbeNotesQuery, ErrorOr<ListQueryResponse<ViewProbeNoteListItem>>>
{
    public Task<ErrorOr<ListQueryResponse<ViewProbeNoteListItem>>> Handle(ListViewProbeNotesQuery request, CancellationToken cancellationToken)
    {
        queries.ParentIds.Enqueue(request.ParentId.Value);
        return Task.FromResult<ErrorOr<ListQueryResponse<ViewProbeNoteListItem>>>(new ListQueryResponse<ViewProbeNoteListItem>([], 0));
    }
}

/// <summary>
/// Puts the view probe's notes into a test host beside the view probe (call after <see cref="ViewProbeTestHost.AddTo"/>,
/// whose registry factory reads every registered view source and includes the notes' features): their list query, texts,
/// view configuration and a list handler that records the parent id each query carried. The notes' page shows a slot that
/// loads nothing; a test that renders the generated table itself sends the query.
/// </summary>
internal static class ViewProbeNoteTestHost
{
    public static void AddTo(IServiceCollection services)
    {
        services.AddSingleton(ModuleConfigurationSource.FromTypes(
            typeof(ViewProbeNoteAggregateFeatureConfiguration),
            typeof(ListViewProbeNotesQuery)));
        services.AddSingleton(TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(ViewProbeNoteTexts)));
        services.AddSingleton(AggregateViewSource.FromTypes("tst", typeof(ViewProbeNoteViewConfiguration)));

        services.AddSingleton<ViewProbeNoteQueries>();
        services.AddTransient<IRequestHandler<ListViewProbeNotesQuery, ErrorOr<ListQueryResponse<ViewProbeNoteListItem>>>, ListViewProbeNotesQueryHandler>();
    }
}
