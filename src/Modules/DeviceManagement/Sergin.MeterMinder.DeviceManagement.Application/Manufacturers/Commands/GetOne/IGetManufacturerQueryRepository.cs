using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;

public interface IGetManufacturerQueryRepository
{
    Task<ManufacturerQueryResponse?> GetManufacturerById(ManufacturerId id, CancellationToken cancellationToken = default);
}
