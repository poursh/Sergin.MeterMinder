using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;

internal sealed class GetDeviceModelListQueryCommandHandler(IGetDeviceModelListQueryRepository queryRepository)
    : IListQueryHandler<GetDeviceModelListQueryCommand, GetDeviceModelListItem>
{
    public async Task<ErrorOr<ListQueryResponse<GetDeviceModelListItem>>> Handle(
        GetDeviceModelListQueryCommand request, CancellationToken cancellationToken)
    {
        ListQueryResponse<GetDeviceModelListItem> res =
            await queryRepository.GetListAsync(request.ManufacturerId, request, cancellationToken);

        return res;
    }
}
