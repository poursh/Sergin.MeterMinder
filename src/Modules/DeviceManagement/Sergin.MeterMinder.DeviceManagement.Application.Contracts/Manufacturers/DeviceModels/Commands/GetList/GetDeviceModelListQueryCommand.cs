using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetList;

public sealed record GetDeviceModelListQueryCommand
    : ListQuery<GetDeviceModelListItem>, IChildListOf<Manufacturer, ManufacturerId, DeviceModel>
{
    public GetDeviceModelListQueryCommand(
        ManufacturerId rootId,
        Paggination paggination,
        Term? term = default,
        Filtering? filtering = default,
        Sorting? sorting = default)
        : base(paggination, term, filtering, sorting)
    {
        RootId = rootId;
    }

    // The filter every read of this list must carry. Not routed through Filtering: no query repository reads
    // it, and it would make a mandatory scope optional.
    public ManufacturerId RootId { get; }
}
