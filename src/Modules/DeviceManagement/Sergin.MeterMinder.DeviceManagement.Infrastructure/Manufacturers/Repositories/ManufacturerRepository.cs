using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Repositories;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Manufacturers.Repositories;

internal class ManufacturerRepository(IDeviceManagementDbContext dbContext)
    : EfRepository<Manufacturer, ManufacturerId>(dbContext), IManufacturerRepository;
