using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;

[RequiredPermissions("permission.dm.devices.read")]
public sealed record GetDeviceListQueryCommand : ListQuery, IListQuery<GetDeviceListItem>
{
    public GetDeviceListQueryCommand(
        Paggination paggination,
        Term? term = default,
        Filtering? filtering = default,
        Sorting? sorting = default)
        : base(paggination, term, filtering, sorting)
    {
    }
}
