using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;

internal sealed class GetManufacturerListQueryCommandHandler(IGetManufacturerListQueryRepository queryRepository) : IListQueryHandler<GetManufacturerListItem>
{
    public async Task<ErrorOr<ListQueryResponse<GetManufacturerListItem>>> Handle(
        ListQuery<GetManufacturerListItem> request, CancellationToken cancellationToken)
    {
        ListQueryResponse<GetManufacturerListItem> res = await queryRepository.GetListAsync(request, cancellationToken);

        return res;
    }
}
