using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

internal sealed class GetDeviceByIdQueryCommandHandler(IGetDeviceQueryRepository repository) : IQueryHandler<GetDeviceByIdQueryCommand, DeviceQueryResponse>
{
    public async Task<ErrorOr<DeviceQueryResponse>> Handle(GetDeviceByIdQueryCommand request, CancellationToken cancellationToken)
    {
        DeviceQueryResponse? res = await repository.GetDeviceById(new DeviceIntenralId(request.Id), cancellationToken);

        if (res is null)
        {
            return Error.NotFound();
        }

        return res;
    }
}
