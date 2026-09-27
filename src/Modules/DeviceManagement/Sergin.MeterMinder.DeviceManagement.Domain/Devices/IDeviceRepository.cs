using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Domain.Repositories;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Devices;
public interface IDeviceRepository : IRepository<Device, DeviceIntenralId>, IUniqueKeyRepository<DeviceId>
{
    Task<Device?> GetByDeviceId(DeviceId deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any live device uses one of the manufacturer's live models — what
    /// <c>DeleteManufacturerCommandValidator</c> refuses a delete over. Deleted rows on either side are hidden
    /// by the soft-delete query filter.
    /// </summary>
    Task<bool> AnyUsingManufacturerAsync(ManufacturerId manufacturerId, CancellationToken cancellationToken = default);
}
