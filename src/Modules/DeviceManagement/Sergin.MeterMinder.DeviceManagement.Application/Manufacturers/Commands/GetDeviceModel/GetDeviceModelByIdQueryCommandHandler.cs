using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;

internal sealed class GetDeviceModelByIdQueryCommandHandler(IGetDeviceModelQueryRepository repository)
    : IQueryHandler<GetDeviceModelByIdQueryCommand, DeviceModelQueryResponse>
{
    public async Task<ErrorOr<DeviceModelQueryResponse>> Handle(GetDeviceModelByIdQueryCommand request, CancellationToken cancellationToken)
    {
        DeviceModelQueryResponse? res = await repository.GetDeviceModelById(
            new ManufacturerId(request.ManufacturerId), new DeviceModelInternalId(request.Id), cancellationToken);

        if (res is null)
        {
            return Error.NotFound();
        }

        return res;
    }
}
