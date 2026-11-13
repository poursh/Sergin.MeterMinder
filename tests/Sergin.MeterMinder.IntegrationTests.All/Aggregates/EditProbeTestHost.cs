using System.Collections.Concurrent;
using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

// The edit probe's in-memory store, handlers and validator. Only EditProbeTestHost registers them, into a test host.

/// <summary>The edit probes the handlers below read and write, by id, each at its version; nothing reaches a database.</summary>
internal sealed class EditProbeStore
{
    private readonly ConcurrentDictionary<Guid, (EditProbeResponse Record, RowVersion Version)> records = new();
    private int sends;

    /// <summary>How many set-property commands reached a handler.</summary>
    public int Sends => sends;

    public Guid Add(string name)
    {
        var id = Guid.CreateVersion7();
        records[id] = (new EditProbeResponse(id, new EditProbeName(name), ProbeColor.Green, new EditProbeTitle("Headline " + name)), RowVersion.Create());
        return id;
    }

    public (EditProbeResponse Record, RowVersion Version)? Find(Guid id) =>
        records.TryGetValue(id, out (EditProbeResponse Record, RowVersion Version) found) ? found : null;

    /// <summary>Someone else's save: the record moves to a new version without a command.</summary>
    public void Touch(Guid id)
    {
        if (Find(id) is { } found)
        {
            records[id] = (found.Record, RowVersion.Create());
        }
    }

    /// <summary>Applies <paramref name="change"/> at <paramref name="expected"/> (any version when null) and answers the new version.</summary>
    public ErrorOr<RowVersion> Change(Guid id, RowVersion? expected, Func<EditProbeResponse, EditProbeResponse> change)
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

        var next = RowVersion.Create();
        records[id] = (change(found.Record), next);
        return next;
    }
}

internal sealed class GetEditProbeQueryHandler(EditProbeStore store, ConcurrencyContext concurrency)
    : VersionedQueryHandler<GetEditProbeQuery, EditProbeResponse>(concurrency)
{
    public override Task<ErrorOr<Versioned<EditProbeResponse>>> HandleVersioned(GetEditProbeQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<Versioned<EditProbeResponse>>>(
            store.Find(request.Id.Value) is { } found ? new Versioned<EditProbeResponse>(found.Record, found.Version) : Error.NotFound());
}

/// <summary>Each set-property handler changes one property at the expected version and publishes the new one.</summary>
internal abstract class EditProbeCommandHandler<TCommand>(EditProbeStore store, ConcurrencyContext concurrency)
    : IRequestHandler<TCommand, ErrorOr<Success>>
    where TCommand : IRequest<ErrorOr<Success>>
{
    public Task<ErrorOr<Success>> Handle(TCommand request, CancellationToken cancellationToken)
    {
        ErrorOr<RowVersion> changed = store.Change(IdOf(request), concurrency.Expected, record => Apply(record, request));

        if (changed.IsError)
        {
            return Task.FromResult<ErrorOr<Success>>(changed.Errors);
        }

        concurrency.Current = changed.Value;
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }

    protected abstract Guid IdOf(TCommand request);

    protected abstract EditProbeResponse Apply(EditProbeResponse record, TCommand request);
}

internal sealed class RenameProbeCommandHandler(EditProbeStore store, ConcurrencyContext concurrency)
    : EditProbeCommandHandler<RenameProbeCommand>(store, concurrency)
{
    protected override Guid IdOf(RenameProbeCommand request) => request.Id.Value;

    protected override EditProbeResponse Apply(EditProbeResponse record, RenameProbeCommand request) => record with { Name = request.Name };
}

internal sealed class SetProbeColorCommandHandler(EditProbeStore store, ConcurrencyContext concurrency)
    : EditProbeCommandHandler<SetProbeColorCommand>(store, concurrency)
{
    protected override Guid IdOf(SetProbeColorCommand request) => request.Id.Value;

    protected override EditProbeResponse Apply(EditProbeResponse record, SetProbeColorCommand request) => record with { Color = request.Color };
}

internal sealed class RetitleProbeCommandHandler(EditProbeStore store, ConcurrencyContext concurrency)
    : EditProbeCommandHandler<RetitleProbeCommand>(store, concurrency)
{
    protected override Guid IdOf(RetitleProbeCommand request) => request.Id.Value;

    protected override EditProbeResponse Apply(EditProbeResponse record, RetitleProbeCommand request) => record with { Title = request.Title };
}

/// <summary>A name is required: what an inline rename to a blank value is refused with.</summary>
internal sealed class RenameProbeCommandValidator : AbstractValidator<RenameProbeCommand>
{
    public RenameProbeCommandValidator() =>
        RuleFor(command => command.Name.Value).NotEmpty().OverridePropertyName(nameof(RenameProbeCommand.Name));
}

/// <summary>
/// Puts the edit probe into a test host beside the view probe (call after <see cref="ViewProbeTestHost.AddTo"/>, whose
/// registry factory reads every registered view source and includes the edit probe's features): its requests, texts, view
/// configuration, in-memory handlers and validator.
/// </summary>
internal static class EditProbeTestHost
{
    public static void AddTo(IServiceCollection services)
    {
        services.AddSingleton(ModuleConfigurationSource.FromTypes(
            typeof(EditProbeAggregateFeatureConfiguration),
            typeof(GetEditProbeQuery),
            typeof(RenameProbeCommand),
            typeof(RenameProbeCommandConfiguration),
            typeof(SetProbeColorCommand),
            typeof(SetProbeColorCommandConfiguration),
            typeof(RetitleProbeCommand),
            typeof(RetitleProbeCommandConfiguration)));
        services.AddSingleton(TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(EditProbeTexts)));
        services.AddSingleton(AggregateViewSource.FromTypes("tst", typeof(EditProbeViewConfiguration)));

        services.AddSingleton<EditProbeStore>();
        services.AddTransient<IRequestHandler<GetEditProbeQuery, ErrorOr<EditProbeResponse>>, GetEditProbeQueryHandler>();
        services.AddTransient<IRequestHandler<RenameProbeCommand, ErrorOr<Success>>, RenameProbeCommandHandler>();
        services.AddTransient<IRequestHandler<SetProbeColorCommand, ErrorOr<Success>>, SetProbeColorCommandHandler>();
        services.AddTransient<IRequestHandler<RetitleProbeCommand, ErrorOr<Success>>, RetitleProbeCommandHandler>();
        services.AddScoped<IValidator<RenameProbeCommand>, RenameProbeCommandValidator>();
    }
}
