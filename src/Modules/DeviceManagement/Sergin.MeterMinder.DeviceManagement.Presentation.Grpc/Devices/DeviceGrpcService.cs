using Grpc.Core;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Presentation.Grpc.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Devices;

/// <summary>
/// Runs in the module's own process when Remote. Proto request in, dispatched the same way
/// GetDeviceEndpoint (Presentation.WebApi) dispatches — directly via ISender.Send, no wrapper on either
/// side — ErrorOr out — just a different transport in front. This service always resolves the real handler
/// via raw ISender.Send: it *is* the Local side of Remote dispatch, from the target process's own point of
/// view, so unlike a caller choosing between DeviceManagementModule (Local) and DeviceManagementRemoteModule
/// (Remote) at composition time, this service has no Local/Remote choice to make — that's what being the
/// gRPC server target means. Same MediatR pipeline (PermissionCheckPipelineBehavior,
/// ValidationPipelineBehavior) runs here as it does for every other ISender.Send call in the process this
/// service lives in.
/// </summary>
/// <remarks>
/// Public, not internal: no production composition root wires this into a real host yet — see
/// <c>src/Modules/DeviceManagement/CLAUDE.md</c>'s "gRPC dispatch slice" note, which explains this is
/// live-but-unhosted (no host passes <c>DeviceManagementRemoteModule</c> as a <c>remoteModules</c> entry to
/// <c>AddSerginCore</c> today). Its one current cross-assembly call site is
/// <c>DeviceGrpcRoundTripTests</c>' own from-scratch Kestrel host in the outer test project. Same reasoning
/// <c>RemoteForwardingHandler</c> documents: an <c>InternalsVisibleTo</c> for one call site costs more than
/// the encapsulation it buys.
/// </remarks>
public sealed class DeviceGrpcService(ISender sender) : DeviceService.DeviceServiceBase
{
    public override async Task<GetDeviceByIdReply> GetDeviceById(
        GetDeviceByIdRequest request, ServerCallContext context)
    {
        ErrorOr<DeviceQueryResponse> result = await sender.Send(
            new GetDeviceByIdQueryCommand(Guid.Parse(request.Id)), context.CancellationToken);

        return result.Match(
            response => new GetDeviceByIdReply
            {
                Success = new DeviceData
                {
                    Id = response.Id.ToString(),
                    DeviceId = response.DeviceId,
                    ManufacturerId = response.ManufacturerId.ToString(),
                },
            },
            errors => new GetDeviceByIdReply { Error = errors[0].ToErrorReply() });
    }
}
