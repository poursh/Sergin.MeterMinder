using ErrorOr;
using MediatR;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// Sends a guarded command the way a detail page does: read the aggregate's current version first, then send
/// the command with it. When the aggregate cannot be read (it does not exist, or is deleted), a fresh version is
/// sent instead, so what comes back is the handler's not-found rather than the pipeline's missing-version error.
/// For tests whose subject is not concurrency.
/// </summary>
internal static class VersionedDispatch
{
    public static async Task<ErrorOr<TResponse>> SendAtManufacturerVersionAsync<TResponse>(
        this ISerginDispatcher dispatcher, Guid manufacturerId, IRequest<ErrorOr<TResponse>> command)
    {
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId));

        return (await dispatcher.SendVersionedAsync(command, loaded.IsError ? RowVersion.Create() : loaded.Value.Version))
            .Then(sent => sent.Value);
    }

    public static async Task<ErrorOr<TResponse>> SendAtDeviceVersionAsync<TResponse>(
        this ISerginDispatcher dispatcher, Guid deviceId, IRequest<ErrorOr<TResponse>> command)
    {
        ErrorOr<Versioned<DeviceQueryResponse>> loaded =
            await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(deviceId));

        return (await dispatcher.SendVersionedAsync(command, loaded.IsError ? RowVersion.Create() : loaded.Value.Version))
            .Then(sent => sent.Value);
    }
}
