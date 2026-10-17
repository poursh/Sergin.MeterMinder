using Microsoft.EntityFrameworkCore;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Repositories;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Devices.Repositories;

internal class DeviceRepository(IDeviceManagementDbContext dbContext)
    : EfRepository<Device, DeviceIntenralId>(dbContext), IDeviceRepository
{
    public Task<Device?> GetByDeviceId(DeviceId deviceId, CancellationToken cancellationToken = default)
    {
        return Set.SingleOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken);
    }

    // The four checks below look across every scope, deleted rows still hidden: a model in use by a device the
    // caller cannot see is still in use, and a device id taken in another scope is still taken.
    public Task<bool> AnyUsingManufacturerAsync(ManufacturerId manufacturerId, CancellationToken cancellationToken = default)
    {
        IQueryable<DeviceModel> models = SetOf<DeviceModel>();

        return InEveryScope.AnyAsync(
            d => models.Any(model => model.Id == d.DeviceModelId && model.ManufacturerId == manufacturerId),
            cancellationToken);
    }

    public Task<bool> AnyUsingModelAsync(DeviceModelInternalId modelId, CancellationToken cancellationToken = default)
        => InEveryScope.AnyAsync(d => d.DeviceModelId == modelId, cancellationToken);

    public Task<bool> IsTakenAsync(DeviceId key, CancellationToken cancellationToken = default)
        => InEveryScope.AnyAsync(d => d.DeviceId == key, cancellationToken);

    public Task<bool> IsTakenByOtherAsync(DeviceId key, DeviceIntenralId exceptId, CancellationToken cancellationToken = default)
        => InEveryScope.AnyAsync(d => d.DeviceId == key && d.Id != exceptId, cancellationToken);

    private IQueryable<Device> InEveryScope => Set.IgnoreQueryFilters([ScopeColumns.QueryFilterName]);
}
