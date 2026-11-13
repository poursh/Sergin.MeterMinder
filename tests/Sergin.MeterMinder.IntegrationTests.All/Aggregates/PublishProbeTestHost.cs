using System.Collections.Concurrent;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

// The publish probe's in-memory store and GetOne handler. Only PublishProbeTestHost registers them, into a test host.

/// <summary>The publish probes the handler below reads, by id; nothing reaches a database.</summary>
internal sealed class PublishProbeStore
{
    private readonly ConcurrentDictionary<Guid, PublishProbeResponse> records = new();

    public Guid Add(string name, bool published)
    {
        var id = Guid.CreateVersion7();
        records[id] = new PublishProbeResponse(id, name, published);
        return id;
    }

    public PublishProbeResponse? Find(Guid id) => records.GetValueOrDefault(id);
}

/// <summary>A plain handler: the probe is not versioned, so it publishes no version.</summary>
internal sealed class GetPublishProbeQueryHandler(PublishProbeStore store) : IRequestHandler<GetPublishProbeQuery, ErrorOr<PublishProbeResponse>>
{
    public Task<ErrorOr<PublishProbeResponse>> Handle(GetPublishProbeQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<PublishProbeResponse>>(store.Find(request.Id.Value) is { } found ? found : Error.NotFound());
}

/// <summary>
/// Puts the publish probe into a test host beside the view probe (call after <see cref="ViewProbeTestHost.AddTo"/>, whose
/// registry factory reads every registered view source and includes the probe's features): its requests, texts, view
/// configuration and in-memory GetOne handler.
/// </summary>
internal static class PublishProbeTestHost
{
    public static void AddTo(IServiceCollection services)
    {
        services.AddSingleton(ModuleConfigurationSource.FromTypes(
            typeof(PublishProbeAggregateFeatureConfiguration),
            typeof(GetPublishProbeQuery),
            typeof(ListPublishProbesQuery),
            typeof(PublishProbeCommand),
            typeof(UnpublishProbeCommand)));
        services.AddSingleton(TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(PublishProbeTexts)));
        services.AddSingleton(AggregateViewSource.FromTypes("tst", typeof(PublishProbeViewConfiguration)));

        services.AddSingleton<PublishProbeStore>();
        services.AddTransient<IRequestHandler<GetPublishProbeQuery, ErrorOr<PublishProbeResponse>>, GetPublishProbeQueryHandler>();
    }
}
