using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;

[RequiredPermissions("permission.dm.manufacturers.read")]
public sealed record GetDeviceModelListQueryCommand : ListQuery<GetDeviceModelListItem>
{
    public GetDeviceModelListQueryCommand(
        ManufacturerId manufacturerId,
        Paggination paggination,
        Term? term = default,
        Filtering? filtering = default,
        Sorting? sorting = default)
        : base(paggination, term, filtering, sorting)
    {
        ManufacturerId = manufacturerId;
    }

    // The filter every read of this list must carry. Not routed through Filtering: no query repository reads
    // it, and it would make a mandatory scope optional.
    public ManufacturerId ManufacturerId { get; }
}
