using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOverview;

internal sealed class GetManufacturerOverviewQueryCommandHandler(IGetManufacturerOverviewQueryRepository repository)
    : IQueryHandler<GetManufacturerOverviewQueryCommand, ManufacturerOverview>
{
    public async Task<ErrorOr<ManufacturerOverview>> Handle(GetManufacturerOverviewQueryCommand request, CancellationToken cancellationToken)
    {
        ManufacturerOverview? overview = await repository.GetManufacturerOverviewAsync(new ManufacturerId(request.Id), cancellationToken);

        if (overview is null)
        {
            return Error.NotFound();
        }

        return overview;
    }
}
