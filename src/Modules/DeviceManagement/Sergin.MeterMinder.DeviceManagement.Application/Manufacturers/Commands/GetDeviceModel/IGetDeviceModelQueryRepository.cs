using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;

public interface IGetDeviceModelQueryRepository
{
    Task<DeviceModelQueryResponse?> GetDeviceModelById(
        ManufacturerId manufacturerId, DeviceModelInternalId id, CancellationToken cancellationToken = default);
}
