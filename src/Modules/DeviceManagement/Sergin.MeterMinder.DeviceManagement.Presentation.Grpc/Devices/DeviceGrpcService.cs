using Grpc.Core;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Presentation.Grpc.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Devices;

/// <summary>
/// Runs in the module's own process when Remote. Proto request in, dispatched the same way
/// GetDeviceEndpoint (Presentation.WebApi) dispatches — directly via ISender.Send, no wrapper on either
/// side anymore — ErrorOr out — just a different transport in front. This service
/// itself stays on raw ISender.Send by construction: it *is* the Local side of Remote dispatch, so routing
/// it through ISerginSender would risk a Remote→Remote loop. Same MediatR pipeline
/// (PermissionCheckPipelineBehavior, ValidationPipelineBehavior) runs here as it does for every other
/// ISender.Send call in the process this service lives in.
/// </summary>
/// <remarks>
/// Public, not internal: no production composition root wires this into a real host yet — see
/// <c>src/Modules/DeviceManagement/CLAUDE.md</c>'s "gRPC dispatch slice" note, which explains this is
/// live-but-unhosted (the real host's <c>Sergin:Dispatch:Modules:dm</c> stays <c>Local</c>). Its one
/// current cross-assembly call site is <c>DeviceGrpcRoundTripTests</c>' own from-scratch Kestrel host in
/// the outer test project. Same reasoning <c>ModuleDispatchRouteResolver</c> documents: an
/// <c>InternalsVisibleTo</c> for one call site costs more than the encapsulation it buys.
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
