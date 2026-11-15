using System.Collections.Concurrent;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

// The probe parents' and child probes' in-memory store and handlers. Only ParentProbeTestHost registers them, into a test host.

/// <summary>The probe parents and child probes the handlers below read and write, by id, and every create sent; nothing reaches a database.</summary>
internal sealed class ParentProbeStore
{
    private readonly ConcurrentDictionary<Guid, ParentProbeResponse> parents = new();
    private readonly ConcurrentDictionary<Guid, ChildProbeResponse> children = new();

    public ConcurrentQueue<object> Created { get; } = new();

    public Guid AddParent(string name, Guid? parentId = null)
    {
        var id = Guid.CreateVersion7();
        string? parentName = parentId is { } parent && parents.TryGetValue(parent, out ParentProbeResponse? found) ? found.Name : null;
        parents[id] = new ParentProbeResponse(id, name, parentId, parentName);
        return id;
    }

    public Guid AddChild(string name, Guid ownerId)
    {
        var id = Guid.CreateVersion7();
        children[id] = new ChildProbeResponse(id, name, ownerId, parents[ownerId].Name);
        return id;
    }

    public ParentProbeResponse? FindParent(Guid id) => parents.GetValueOrDefault(id);

    public ChildProbeResponse? FindChild(Guid id) => children.GetValueOrDefault(id);

    public bool RemoveParent(Guid id) => parents.TryRemove(id, out _);

    public bool RemoveChild(Guid id) => children.TryRemove(id, out _);
}

internal sealed class GetParentProbeQueryHandler(ParentProbeStore store) : IRequestHandler<GetParentProbeQuery, ErrorOr<ParentProbeResponse>>
{
    public Task<ErrorOr<ParentProbeResponse>> Handle(GetParentProbeQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<ParentProbeResponse>>(store.FindParent(request.Id.Value) is { } found ? found : Error.NotFound());
}

internal sealed class GetChildProbeQueryHandler(ParentProbeStore store) : IRequestHandler<GetChildProbeQuery, ErrorOr<ChildProbeResponse>>
{
    public Task<ErrorOr<ChildProbeResponse>> Handle(GetChildProbeQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<ChildProbeResponse>>(store.FindChild(request.Id.Value) is { } found ? found : Error.NotFound());
}

internal sealed class CreateParentProbeCommandHandler(ParentProbeStore store) : IRequestHandler<CreateParentProbeCommand, ErrorOr<ProbeCreatedResponse>>
{
    public Task<ErrorOr<ProbeCreatedResponse>> Handle(CreateParentProbeCommand request, CancellationToken cancellationToken)
    {
        store.Created.Enqueue(request);
        return Task.FromResult<ErrorOr<ProbeCreatedResponse>>(new ProbeCreatedResponse(store.AddParent(request.Name, request.ParentId.Value)));
    }
}

internal sealed class CreateChildProbeCommandHandler(ParentProbeStore store) : IRequestHandler<CreateChildProbeCommand, ErrorOr<ProbeCreatedResponse>>
{
    public Task<ErrorOr<ProbeCreatedResponse>> Handle(CreateChildProbeCommand request, CancellationToken cancellationToken)
    {
        store.Created.Enqueue(request);
        return Task.FromResult<ErrorOr<ProbeCreatedResponse>>(new ProbeCreatedResponse(store.AddChild(request.Name, request.OwnerId.Value)));
    }
}

internal sealed class DeleteParentProbeCommandHandler(ParentProbeStore store) : IRequestHandler<DeleteParentProbeCommand, ErrorOr<Success>>
{
    public Task<ErrorOr<Success>> Handle(DeleteParentProbeCommand request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<Success>>(store.RemoveParent(request.Id.Value) ? Result.Success : Error.NotFound());
}

internal sealed class DeleteChildProbeCommandHandler(ParentProbeStore store) : IRequestHandler<DeleteChildProbeCommand, ErrorOr<Success>>
{
    public Task<ErrorOr<Success>> Handle(DeleteChildProbeCommand request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<Success>>(store.RemoveChild(request.Id.Value) ? Result.Success : Error.NotFound());
}

/// <summary>
/// Puts the probe parents and child probes into a test host beside the view probe (call after
/// <see cref="ViewProbeTestHost.AddTo"/>, whose registry factory includes their features): their requests, texts, view
/// configurations and in-memory handlers.
/// </summary>
internal static class ParentProbeTestHost
{
    public static void AddTo(IServiceCollection services)
    {
        services.AddSingleton(ModuleConfigurationSource.FromTypes(
            typeof(ParentProbeAggregateFeatureConfiguration),
            typeof(ChildProbeAggregateFeatureConfiguration),
            typeof(GetParentProbeQuery),
            typeof(GetParentProbeTreeQuery),
            typeof(CreateParentProbeCommand),
            typeof(CreateParentProbeCommandConfiguration),
            typeof(DeleteParentProbeCommand),
            typeof(GetChildProbeQuery),
            typeof(CreateChildProbeCommand),
            typeof(DeleteChildProbeCommand)));
        services.AddSingleton(TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(ParentProbeTexts), typeof(ChildProbeTexts)));
        services.AddSingleton(AggregateViewSource.FromTypes("tst", typeof(ParentProbeViewConfiguration), typeof(ChildProbeViewConfiguration)));

        services.AddSingleton<ParentProbeStore>();
        services.AddTransient<IRequestHandler<GetParentProbeQuery, ErrorOr<ParentProbeResponse>>, GetParentProbeQueryHandler>();
        services.AddTransient<IRequestHandler<GetChildProbeQuery, ErrorOr<ChildProbeResponse>>, GetChildProbeQueryHandler>();
        services.AddTransient<IRequestHandler<CreateParentProbeCommand, ErrorOr<ProbeCreatedResponse>>, CreateParentProbeCommandHandler>();
        services.AddTransient<IRequestHandler<CreateChildProbeCommand, ErrorOr<ProbeCreatedResponse>>, CreateChildProbeCommandHandler>();
        services.AddTransient<IRequestHandler<DeleteParentProbeCommand, ErrorOr<Success>>, DeleteParentProbeCommandHandler>();
        services.AddTransient<IRequestHandler<DeleteChildProbeCommand, ErrorOr<Success>>, DeleteChildProbeCommandHandler>();
    }
}
