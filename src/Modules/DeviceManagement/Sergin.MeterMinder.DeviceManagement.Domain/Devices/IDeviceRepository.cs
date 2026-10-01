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

    /// <summary>
    /// Whether a live device other than <paramref name="exceptId"/> already uses <paramref name="key"/> —
    /// <c>IsTakenAsync</c> for an update, where the device's own unchanged id must not count. Advisory: the
    /// partial unique index <c>ix_device_device_id</c> is the guarantee.
    /// </summary>
    Task<bool> IsTakenByOtherAsync(DeviceId key, DeviceIntenralId exceptId, CancellationToken cancellationToken = default);
}
