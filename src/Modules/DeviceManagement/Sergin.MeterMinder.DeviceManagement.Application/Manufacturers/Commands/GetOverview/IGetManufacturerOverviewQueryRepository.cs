using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOverview;

public interface IGetManufacturerOverviewQueryRepository
{
    Task<ManufacturerOverview?> GetManufacturerOverviewAsync(ManufacturerId id, CancellationToken cancellationToken = default);
}
