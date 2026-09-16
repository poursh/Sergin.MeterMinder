using Sergin.SharedKernel.Domain.Repositories;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

public interface IManufacturerRepository : IRepository<Manufacturer, ManufacturerId>
{
    /// <summary>
    /// The aggregate with its <see cref="Manufacturer.Models"/> loaded — what <see cref="Manufacturer.AddModel"/>
    /// needs. <c>GetAsync</c> is a <c>FindAsync</c>, which loads no navigation.
    /// </summary>
    Task<Manufacturer?> GetWithModelsAsync(ManufacturerId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any manufacturer has this model — a <c>SELECT EXISTS</c> on <c>dm.device_model</c>, nothing
    /// loaded. The one place a model is looked up from outside its aggregate: <c>CreateDeviceCommandValidator</c>.
    /// </summary>
    Task<bool> ModelExistsAsync(DeviceModelInternalId id, CancellationToken cancellationToken = default);
}
