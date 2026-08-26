using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;

[RequiredPermissions("permission.dm.manufacturers.read")]
public sealed record GetManufacturerListQueryCommand : ListQuery<GetManufacturerListItem>
{
    public GetManufacturerListQueryCommand(
        Paggination paggination,
        Term? term = default,
        Filtering? filtering = default,
        Sorting? sorting = default)
        : base(paggination, term, filtering, sorting)
    {
    }
}
