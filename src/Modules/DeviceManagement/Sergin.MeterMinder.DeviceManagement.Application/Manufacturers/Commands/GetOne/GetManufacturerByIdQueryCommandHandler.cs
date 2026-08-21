using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;

internal sealed class GetManufacturerByIdQueryCommandHandler(IGetManufacturerQueryRepository repository)
    : IQueryHandler<GetManufacturerByIdQueryCommand, ManufacturerQueryResponse>
{
    public async Task<ErrorOr<ManufacturerQueryResponse>> Handle(GetManufacturerByIdQueryCommand request, CancellationToken cancellationToken)
    {
        ManufacturerQueryResponse? res = await repository.GetManufacturerById(new ManufacturerId(request.Id), cancellationToken);

        if (res is null)
        {
            return Error.NotFound();
        }

        return res;
    }
}
