using ErrorOr;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;

internal sealed class GetDeviceListQueryCommandHandler(IGetDeviceListQueryRepository queryRepository) : IListQueryHandler<GetDeviceListQueryCommand, GetDeviceListItem>
{
    public async Task<ErrorOr<ListQueryResponse<GetDeviceListItem>>> Handle(
        GetDeviceListQueryCommand request, CancellationToken cancellationToken)
    {
        ListQueryResponse<GetDeviceListItem> res = await queryRepository.GetListAsync(request, cancellationToken);

        return res;
    }
}
