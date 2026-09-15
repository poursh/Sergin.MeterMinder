using Sergin.SharedKernel.Domain.Repositories;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Devices;
public interface IDeviceRepository : IRepository<Device, DeviceIntenralId>, IUniqueKeyRepository<DeviceId>
{
    Task<Device?> GetByDeviceId(DeviceId deviceId, CancellationToken cancellationToken = default);
}
