using System.Collections.Concurrent;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// Run as a test host schedules it: from the record's id, with no version (a version stored with a task is stale by the
/// next run). The registry tests read <see cref="RunViewProbeCommandConfiguration"/>, which keeps the version guard; a
/// host builds the scheduled-command catalogue, which refuses that, so the probe host registers this one instead.
/// </summary>
internal sealed class HostedRunViewProbeCommandConfiguration : ICommandConfiguration<RunViewProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<RunViewProbeCommand> builder) =>
        builder.RequirePermissions(ViewProbePermissions.Run).Schedulable("tst.view-probes.run").WithoutExpectedVersion();
}

/// <summary>The probe records the handlers below read and write, by id; nothing reaches a database.</summary>
internal sealed class ViewProbeStore
{
    private readonly ConcurrentDictionary<Guid, (ViewProbeResponse Record, RowVersion Version)> records = new();

    public ConcurrentBag<Guid> Runs { get; } = [];

    public Guid Add(string name)
    {
        var id = Guid.CreateVersion7();
        records[id] = (new ViewProbeResponse(id, new ViewProbeName(name), 0), RowVersion.Create());
        return id;
    }

    public (ViewProbeResponse Record, RowVersion Version)? Find(Guid id) => records.TryGetValue(id, out (ViewProbeResponse Record, RowVersion Version) found) ? found : null;

    public bool Remove(Guid id) => records.TryRemove(id, out _);
}

internal sealed class GetViewProbeQueryHandler(ViewProbeStore store, ConcurrencyContext concurrency)
    : VersionedQueryHandler<GetViewProbeQuery, ViewProbeResponse>(concurrency)
{
    public override Task<ErrorOr<Versioned<ViewProbeResponse>>> HandleVersioned(GetViewProbeQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<Versioned<ViewProbeResponse>>>(
            store.Find(request.Id.Value) is { } found ? new Versioned<ViewProbeResponse>(found.Record, found.Version) : Error.NotFound());
}

internal sealed class GetViewProbeOverviewQueryHandler : IRequestHandler<GetViewProbeOverviewQuery, ErrorOr<ViewProbeOverview>>
{
    public const int Figure = 4242;

    public Task<ErrorOr<ViewProbeOverview>> Handle(GetViewProbeOverviewQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<ViewProbeOverview>>(new ViewProbeOverview(Figure));
}

internal sealed class GetViewProbeAuditQueryHandler(ViewProbeStore store) : IRequestHandler<GetViewProbeAuditQuery, ErrorOr<AuditInfo>>
{
    public Task<ErrorOr<AuditInfo>> Handle(GetViewProbeAuditQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<AuditInfo>>(
            store.Find(request.Id.Value) is null ? Error.NotFound() : new AuditInfo(DateTime.UtcNow, Guid.Empty, null, null, null));
}

internal sealed class RunViewProbeCommandHandler(ViewProbeStore store) : IRequestHandler<RunViewProbeCommand, ErrorOr<Success>>
{
    public Task<ErrorOr<Success>> Handle(RunViewProbeCommand request, CancellationToken cancellationToken)
    {
        if (store.Find(request.Id.Value) is null)
        {
            return Task.FromResult<ErrorOr<Success>>(Error.NotFound());
        }

        store.Runs.Add(request.Id.Value);
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }
}

internal sealed class DeleteViewProbeCommandHandler(ViewProbeStore store, ConcurrencyContext concurrency)
    : IRequestHandler<DeleteViewProbeCommand, ErrorOr<Success>>
{
    public Task<ErrorOr<Success>> Handle(DeleteViewProbeCommand request, CancellationToken cancellationToken)
    {
        if (store.Find(request.Id.Value) is not { } found)
        {
            return Task.FromResult<ErrorOr<Success>>(Error.NotFound());
        }

        if (concurrency.Expected is { } expected && expected != found.Version)
        {
            return Task.FromResult<ErrorOr<Success>>(VersionErrors.Stale);
        }

        store.Remove(request.Id.Value);
        concurrency.Current = RowVersion.Create();
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }
}

/// <summary>
/// Puts the view probe into a test host: its requests and configurations as a test source (with the list queries its
/// routes and filters need, which nothing here sends), its texts, its view
/// configuration, its in-memory handlers. The host's aggregate feature registry is not touched — the feature guard
/// refuses a type no DbContext maps — so only the view registry is built with the probe's features included.
/// </summary>
internal static class ViewProbeTestHost
{
    public static void AddTo(IServiceCollection services)
    {
        services.AddSingleton(ModuleConfigurationSource.FromTypes(
            typeof(ViewProbeAggregateFeatureConfiguration),
            typeof(HostedRunViewProbeCommandConfiguration),
            typeof(GetViewProbeQuery),
            typeof(GetViewProbeOverviewQuery),
            typeof(GetViewProbeAuditQuery),
            typeof(ListViewProbesQuery),
            typeof(ListViewOwnersQuery),
            typeof(ListViewPartsQuery),
            typeof(RunViewProbeCommand),
            typeof(DeleteViewProbeCommand)));
        services.AddSingleton(TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(ViewProbeTexts)));
        services.AddSingleton(AggregateViewSource.FromTypes("tst", typeof(ViewProbeViewConfiguration)));

        ServiceDescriptor views = services.Last(descriptor => descriptor.ServiceType == typeof(AggregateViewRegistry));
        services.Remove(views);
        services.AddSingleton(provider => AggregateViewRegistry.Build(
            provider.GetServices<AggregateViewSource>(),
            provider.GetRequiredService<CommandConfigurationRegistry>(),
            provider.GetRequiredService<AggregateFeatureRegistry>().Including([typeof(ViewProbeAggregateFeatureConfiguration), typeof(PlainProbeAggregateFeatureConfiguration), typeof(EditProbeAggregateFeatureConfiguration)]),
            provider.GetRequiredService<TextCatalog>()));

        services.AddSingleton<ViewProbeStore>();
        services.AddTransient<IRequestHandler<GetViewProbeQuery, ErrorOr<ViewProbeResponse>>, GetViewProbeQueryHandler>();
        services.AddTransient<IRequestHandler<GetViewProbeOverviewQuery, ErrorOr<ViewProbeOverview>>, GetViewProbeOverviewQueryHandler>();
        services.AddTransient<IRequestHandler<GetViewProbeAuditQuery, ErrorOr<AuditInfo>>, GetViewProbeAuditQueryHandler>();
        services.AddTransient<IRequestHandler<RunViewProbeCommand, ErrorOr<Success>>, RunViewProbeCommandHandler>();
        services.AddTransient<IRequestHandler<DeleteViewProbeCommand, ErrorOr<Success>>, DeleteViewProbeCommandHandler>();
    }
}
