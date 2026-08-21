using Sergin.SharedKernel.Domain.Repositories;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
public interface IManufacturerRepository : IRepository<Manufacturer, ManufacturerId>;
