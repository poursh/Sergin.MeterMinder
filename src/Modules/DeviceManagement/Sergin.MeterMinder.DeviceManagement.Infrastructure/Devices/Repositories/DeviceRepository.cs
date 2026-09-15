using Microsoft.EntityFrameworkCore;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Repositories;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Devices.Repositories;

internal class DeviceRepository(IDeviceManagementDbContext dbContext)
    : EfRepository<Device, DeviceIntenralId>(dbContext), IDeviceRepository
{
    public Task<Device?> GetByDeviceId(DeviceId deviceId, CancellationToken cancellationToken = default)
    {
        return Set.SingleOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken);
    }

    public Task<bool> IsTakenAsync(DeviceId key, CancellationToken cancellationToken = default)
        => AnyAsync(d => d.DeviceId == key, cancellationToken);
}
