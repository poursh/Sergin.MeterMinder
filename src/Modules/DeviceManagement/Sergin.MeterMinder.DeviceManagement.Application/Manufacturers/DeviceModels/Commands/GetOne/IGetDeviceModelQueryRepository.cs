using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetOne;

public interface IGetDeviceModelQueryRepository
{
    Task<DeviceModelQueryResponse?> GetDeviceModelById(
        ManufacturerId manufacturerId, DeviceModelInternalId id, CancellationToken cancellationToken = default);
}
