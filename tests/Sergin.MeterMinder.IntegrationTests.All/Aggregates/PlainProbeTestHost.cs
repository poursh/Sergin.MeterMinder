using System.Collections.Concurrent;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

// A test-only root that is not Versioned(): its GetOne publishes no version, so the generated detail and edit views must
// load and send it without one. Only PlainProbeTestHost registers it, into a test host.

internal sealed record PlainProbeId(Guid Value);

internal sealed record PlainProbeName(string Value);

internal sealed class PlainProbe : AggregateRoot<PlainProbeId>;

public static class PlainProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "PlainProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Plain probe");

    public static TextDefinition Title { get; } = Category.Text("Title", "Plain probes");

    public static FieldText Name { get; } = Category.Field(nameof(Name), "Name");
}

internal sealed class PlainProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<PlainProbe>
{
    public void Configure(AggregateFeatureBuilder<PlainProbe> builder) =>
        builder.Texts(PlainProbeTexts.Category).Named(PlainProbeTexts.Singular);
}

internal sealed record PlainProbeResponse(Guid Id, PlainProbeName Name);

internal sealed record GetPlainProbeQuery(PlainProbeId Id) : IQuery<PlainProbeResponse>, IGetOneQuery<PlainProbe, PlainProbeId>;

internal sealed record PlainProbeListItem(Guid Id, string Name);

internal sealed record ListPlainProbesQuery : ListQuery<PlainProbeListItem>, IListOf<PlainProbe>
{
    public ListPlainProbesQuery(Paggination paggination)
        : base(paggination)
    {
    }
}

internal sealed record UpdatePlainProbeCommand(PlainProbeId Id, PlainProbeName Name) : ICommand, IUpdateCommand<PlainProbe, PlainProbeId>;

internal sealed class PlainProbeViewConfiguration : IAggregateViewConfiguration<PlainProbe>
{
    public void Configure(AggregateViewBuilder<PlainProbe> view) =>
        view.Title(PlainProbeTexts.Title)
            .Routes("/tst/plain-probes", id => $"/tst/plain-probes/{id}", edit: id => $"/tst/plain-probes/{id}/edit");
}

/// <summary>The plain probes the handlers below read and write, by id; nothing reaches a database.</summary>
internal sealed class PlainProbeStore
{
    private readonly ConcurrentDictionary<Guid, PlainProbeResponse> records = new();

    public Guid Add(string name)
    {
        var id = Guid.CreateVersion7();
        records[id] = new PlainProbeResponse(id, new PlainProbeName(name));
        return id;
    }

    public PlainProbeResponse? Find(Guid id) => records.GetValueOrDefault(id);

    public bool Rename(Guid id, string name)
    {
        if (!records.ContainsKey(id))
        {
            return false;
        }

        records[id] = new PlainProbeResponse(id, new PlainProbeName(name));
        return true;
    }
}

/// <summary>A plain handler, not a VersionedQueryHandler: it publishes no version.</summary>
internal sealed class GetPlainProbeQueryHandler(PlainProbeStore store) : IRequestHandler<GetPlainProbeQuery, ErrorOr<PlainProbeResponse>>
{
    public Task<ErrorOr<PlainProbeResponse>> Handle(GetPlainProbeQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<PlainProbeResponse>>(store.Find(request.Id.Value) is { } found ? found : Error.NotFound());
}

internal sealed class UpdatePlainProbeCommandHandler(PlainProbeStore store) : IRequestHandler<UpdatePlainProbeCommand, ErrorOr<Success>>
{
    public Task<ErrorOr<Success>> Handle(UpdatePlainProbeCommand request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<Success>>(store.Rename(request.Id.Value, request.Name.Value) ? Result.Success : Error.NotFound());
}

/// <summary>
/// Puts the plain probe into a test host beside the view probe (call after <see cref="ViewProbeTestHost.AddTo"/>, whose
/// registry factory reads every registered view source): its requests, texts, view configuration and in-memory handlers.
/// </summary>
internal static class PlainProbeTestHost
{
    public static void AddTo(IServiceCollection services)
    {
        services.AddSingleton(ModuleConfigurationSource.FromTypes(
            typeof(PlainProbeAggregateFeatureConfiguration),
            typeof(GetPlainProbeQuery),
            typeof(ListPlainProbesQuery),
            typeof(UpdatePlainProbeCommand)));
        services.AddSingleton(TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(PlainProbeTexts)));
        services.AddSingleton(AggregateViewSource.FromTypes("tst", typeof(PlainProbeViewConfiguration)));

        services.AddSingleton<PlainProbeStore>();
        services.AddTransient<IRequestHandler<GetPlainProbeQuery, ErrorOr<PlainProbeResponse>>, GetPlainProbeQueryHandler>();
        services.AddTransient<IRequestHandler<UpdatePlainProbeCommand, ErrorOr<Success>>, UpdatePlainProbeCommandHandler>();
    }
}
