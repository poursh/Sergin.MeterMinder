using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Presentation.Grpc.Dispatching;
using Sergin.SharedKernel.Presentation.Grpc.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Devices;

internal sealed class GetDeviceByIdGrpcInvoker(DeviceService.DeviceServiceClient client)
    : IRemoteInvoker<GetDeviceByIdQueryCommand, DeviceQueryResponse>
{
    public async Task<ErrorOr<DeviceQueryResponse>> InvokeAsync(
        GetDeviceByIdQueryCommand request, CancellationToken cancellationToken)
    {
        GetDeviceByIdReply reply = await client.GetDeviceByIdAsync(
            new GetDeviceByIdRequest { Id = request.Id.ToString() },
            cancellationToken: cancellationToken);

        return reply.ResultCase == GetDeviceByIdReply.ResultOneofCase.Error
            ? reply.Error.ToErrorOr<DeviceQueryResponse>()
            : new DeviceQueryResponse(
                Guid.Parse(reply.Success.Id),
                reply.Success.DeviceId,
                Guid.Parse(reply.Success.ManufacturerId));
    }
}
