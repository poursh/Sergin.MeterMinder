using Microsoft.EntityFrameworkCore;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Repositories;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.Repositories;

internal class ManufacturerRepository(IDeviceManagementDbContext dbContext)
    : EfRepository<Manufacturer, ManufacturerId>(dbContext), IManufacturerRepository
{
    public Task<Manufacturer?> GetWithModelsAsync(ManufacturerId id, CancellationToken cancellationToken = default)
    {
        return Set.Include(m => m.Models).SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    // Through the aggregate's own Set rather than dbContext.Set<DeviceModel>(): capturing the primary-constructor
    // parameter that is also passed to the base is CS9107, an error under TreatWarningsAsErrors. EF translates
    // the SelectMany into an EXISTS over dm.manufacturer joined to dm.device_model — same answer, since the FK is
    // NOT NULL — and nothing is loaded.
    public Task<bool> ModelExistsAsync(DeviceModelInternalId id, CancellationToken cancellationToken = default)
    {
        return Set.SelectMany(m => m.Models).AnyAsync(model => model.Id == id, cancellationToken);
    }
}
