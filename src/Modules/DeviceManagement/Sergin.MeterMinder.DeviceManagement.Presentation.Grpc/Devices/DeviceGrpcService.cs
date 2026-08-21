using Grpc.Core;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Presentation.Grpc.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Devices;

/// <summary>
/// Runs in the module's own process when Remote. Structurally the same shape as GetDeviceEndpoint
/// (Presentation.WebApi) — proto request in, ISender.Send, ErrorOr out — just a different transport.
/// Same MediatR pipeline (PermissionCheckPipelineBehavior, ValidationPipelineBehavior) runs here as it
/// does for every other ISender.Send call in the process this service lives in.
/// </summary>
/// <remarks>
/// Public, not internal: no production composition root wires this into a real host yet (see the
/// DeviceManagement module's CLAUDE.md/task notes — Task 5 added the contract, nothing maps it into
/// DeviceManagementModule so far), and its one current cross-assembly call site is
/// DeviceGrpcRoundTripTests' own from-scratch Kestrel host in the outer test project. Same reasoning
/// ModuleDispatchRouteResolver documents: an InternalsVisibleTo for one call site costs more than the
/// encapsulation it buys.
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
