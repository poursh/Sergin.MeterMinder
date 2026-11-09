using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetOne;

internal sealed class GetDeviceModelByIdQueryCommandHandler(IGetDeviceModelQueryRepository repository)
    : IQueryHandler<GetDeviceModelByIdQueryCommand, DeviceModelQueryResponse>
{
    public async Task<ErrorOr<DeviceModelQueryResponse>> Handle(GetDeviceModelByIdQueryCommand request, CancellationToken cancellationToken)
    {
        DeviceModelQueryResponse? res = await repository.GetDeviceModelById(
            request.RootId, request.Id, cancellationToken);

        if (res is null)
        {
            return Error.NotFound();
        }

        return res;
    }
}
