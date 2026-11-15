using System.Collections.Concurrent;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

// The confirm probe's in-memory store and handlers. Only ConfirmProbeTestHost registers them, into a test host.

/// <summary>The confirm probes the handlers below read and write, by id, each at its version; nothing reaches a database.</summary>
internal sealed class ConfirmProbeStore
{
    /// <summary>What the delete handler refuses a delete without Cascade with.</summary>
    public static readonly Error HoldsRecords = Error.Conflict("Tst.ConfirmProbe.HoldsRecords", "The probe still holds records.");

    private readonly ConcurrentDictionary<Guid, (ConfirmProbeResponse Record, RowVersion Version)> records = new();
    private int sends;
    private int loads;

    /// <summary>How many commands reached a handler.</summary>
    public int Sends => sends;

    /// <summary>How many times the GetOne query read a record.</summary>
    public int Loads => loads;

    public ConcurrentBag<Guid> Archived { get; } = [];

    public Guid Add(string name)
    {
        var id = Guid.CreateVersion7();
        records[id] = (new ConfirmProbeResponse(id, name, new ConfirmProbeTitle("Headline " + name)), RowVersion.Create());
        return id;
    }

    public (ConfirmProbeResponse Record, RowVersion Version)? Find(Guid id) =>
        records.TryGetValue(id, out (ConfirmProbeResponse Record, RowVersion Version) found) ? found : null;

    /// <summary>Someone else's save: the record moves to a new version without a command.</summary>
    public void Touch(Guid id)
    {
        if (Find(id) is { } found)
        {
            records[id] = (found.Record, RowVersion.Create());
        }
    }

    public (ConfirmProbeResponse Record, RowVersion Version)? Load(Guid id)
    {
        Interlocked.Increment(ref loads);
        return Find(id);
    }

    /// <summary>Applies <paramref name="change"/> at <paramref name="expected"/> (any version when null), then removes the record when <paramref name="remove"/>. Answers the new version.</summary>
    public ErrorOr<RowVersion> Change(Guid id, RowVersion? expected, Func<ConfirmProbeResponse, ErrorOr<ConfirmProbeResponse>> change, bool remove = false)
    {
        Interlocked.Increment(ref sends);

        if (Find(id) is not { } found)
        {
            return Error.NotFound();
        }

        if (expected is not null && expected != found.Version)
        {
            return VersionErrors.Stale;
        }

        ErrorOr<ConfirmProbeResponse> changed = change(found.Record);

        if (changed.IsError)
        {
            return changed.Errors;
        }

        var next = RowVersion.Create();

        if (remove)
        {
            records.TryRemove(id, out _);
        }
        else
        {
            records[id] = (changed.Value, next);
        }

        return next;
    }
}

internal sealed class GetConfirmProbeQueryHandler(ConfirmProbeStore store, ConcurrencyContext concurrency)
    : VersionedQueryHandler<GetConfirmProbeQuery, ConfirmProbeResponse>(concurrency)
{
    public override Task<ErrorOr<Versioned<ConfirmProbeResponse>>> HandleVersioned(GetConfirmProbeQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<Versioned<ConfirmProbeResponse>>>(
            store.Load(request.Id.Value) is { } found ? new Versioned<ConfirmProbeResponse>(found.Record, found.Version) : Error.NotFound());
}

/// <summary>Each command changes the record at the expected version and publishes the new one.</summary>
internal abstract class ConfirmProbeCommandHandler<TCommand>(ConfirmProbeStore store, ConcurrencyContext concurrency)
    : IRequestHandler<TCommand, ErrorOr<Success>>
    where TCommand : IRequest<ErrorOr<Success>>
{
    protected ConfirmProbeStore Store => store;

    public Task<ErrorOr<Success>> Handle(TCommand request, CancellationToken cancellationToken)
    {
        ErrorOr<RowVersion> changed = store.Change(IdOf(request), concurrency.Expected, record => Apply(record, request), Removes);

        if (changed.IsError)
        {
            return Task.FromResult<ErrorOr<Success>>(changed.Errors);
        }

        concurrency.Current = changed.Value;
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }

    protected abstract Guid IdOf(TCommand request);

    /// <summary>Whether a change that succeeded removes the record.</summary>
    protected virtual bool Removes => false;

    protected abstract ErrorOr<ConfirmProbeResponse> Apply(ConfirmProbeResponse record, TCommand request);
}

internal sealed class ArchiveConfirmProbeCommandHandler(ConfirmProbeStore store, ConcurrencyContext concurrency)
    : ConfirmProbeCommandHandler<ArchiveConfirmProbeCommand>(store, concurrency)
{
    protected override Guid IdOf(ArchiveConfirmProbeCommand request) => request.Id.Value;

    protected override ErrorOr<ConfirmProbeResponse> Apply(ConfirmProbeResponse record, ArchiveConfirmProbeCommand request)
    {
        Store.Archived.Add(record.Id);
        return record;
    }
}

internal sealed class RetitleConfirmProbeCommandHandler(ConfirmProbeStore store, ConcurrencyContext concurrency)
    : ConfirmProbeCommandHandler<RetitleConfirmProbeCommand>(store, concurrency)
{
    protected override Guid IdOf(RetitleConfirmProbeCommand request) => request.Id.Value;

    protected override ErrorOr<ConfirmProbeResponse> Apply(ConfirmProbeResponse record, RetitleConfirmProbeCommand request) =>
        record with { Title = request.Title };
}

/// <summary>Refuses a delete without Cascade (the probe holds records); removes the record with it.</summary>
internal sealed class DeleteConfirmProbeCommandHandler(ConfirmProbeStore store, ConcurrencyContext concurrency)
    : ConfirmProbeCommandHandler<DeleteConfirmProbeCommand>(store, concurrency)
{
    protected override Guid IdOf(DeleteConfirmProbeCommand request) => request.Id.Value;

    protected override bool Removes => true;

    protected override ErrorOr<ConfirmProbeResponse> Apply(ConfirmProbeResponse record, DeleteConfirmProbeCommand request) =>
        request.Cascade ? record : ConfirmProbeStore.HoldsRecords;
}

/// <summary>
/// Puts the confirm probe into a test host beside the view probe (call after <see cref="ViewProbeTestHost.AddTo"/>, whose
/// registry factory includes the confirm probe's features): its requests, texts, view configuration and in-memory handlers.
/// </summary>
internal static class ConfirmProbeTestHost
{
    public static void AddTo(IServiceCollection services)
    {
        services.AddSingleton(ModuleConfigurationSource.FromTypes(
            typeof(ConfirmProbeAggregateFeatureConfiguration),
            typeof(GetConfirmProbeQuery),
            typeof(ListConfirmProbesQuery),
            typeof(ArchiveConfirmProbeCommand),
            typeof(ArchiveConfirmProbeCommandConfiguration),
            typeof(RetitleConfirmProbeCommand),
            typeof(RetitleConfirmProbeCommandConfiguration),
            typeof(DeleteConfirmProbeCommand),
            typeof(DeleteConfirmProbeCommandConfiguration)));
        services.AddSingleton(TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(ConfirmProbeTexts)));
        services.AddSingleton(AggregateViewSource.FromTypes("tst", typeof(ConfirmProbeViewConfiguration)));

        services.AddSingleton<ConfirmProbeStore>();
        services.AddTransient<IRequestHandler<GetConfirmProbeQuery, ErrorOr<ConfirmProbeResponse>>, GetConfirmProbeQueryHandler>();
        services.AddTransient<IRequestHandler<ArchiveConfirmProbeCommand, ErrorOr<Success>>, ArchiveConfirmProbeCommandHandler>();
        services.AddTransient<IRequestHandler<RetitleConfirmProbeCommand, ErrorOr<Success>>, RetitleConfirmProbeCommandHandler>();
        services.AddTransient<IRequestHandler<DeleteConfirmProbeCommand, ErrorOr<Success>>, DeleteConfirmProbeCommandHandler>();
    }
}
