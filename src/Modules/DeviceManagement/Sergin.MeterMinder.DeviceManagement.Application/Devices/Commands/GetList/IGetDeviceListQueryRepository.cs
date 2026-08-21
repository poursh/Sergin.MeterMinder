using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
public interface IGetDeviceListQueryRepository
{
    Task<ListQueryResponse<GetDeviceListItem>> GetListAsync(ListQuery<GetDeviceListItem> query, CancellationToken cancellationToken = default);
}
