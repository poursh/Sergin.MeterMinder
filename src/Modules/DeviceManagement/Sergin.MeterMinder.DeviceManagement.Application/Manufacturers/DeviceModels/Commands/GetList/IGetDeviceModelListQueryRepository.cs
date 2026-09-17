using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;

public interface IGetDeviceModelListQueryRepository
{
    // The manufacturer is a first-class argument, not something read out of query.Filtering — see
    // GetDeviceModelListQueryCommand. The base ListQuery is enough for the rest: only Paggination is read.
    Task<ListQueryResponse<GetDeviceModelListItem>> GetListAsync(
        ManufacturerId manufacturerId, ListQuery query, CancellationToken cancellationToken = default);
}
