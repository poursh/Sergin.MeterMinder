using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Concurrency;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;

public interface IGetManufacturerQueryRepository
{
    Task<Versioned<ManufacturerQueryResponse>?> GetManufacturerById(ManufacturerId id, CancellationToken cancellationToken = default);
}
