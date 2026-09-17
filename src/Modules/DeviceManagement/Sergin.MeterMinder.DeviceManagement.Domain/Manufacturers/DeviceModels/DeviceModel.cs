using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;

/// <summary>
/// A manufacturer's product line, an entity inside the Manufacturer aggregate. It has no life of its own:
/// <see cref="Manufacturer.AddModel"/> is the only way one comes into being, and there is no repository for it.
/// </summary>
public class DeviceModel : Entity<DeviceModelInternalId>
{
    private DeviceModel() { }

    // The owner's id. EF needs it as the foreign key, and the entity knowing which manufacturer it belongs
    // to costs nothing.
    public ManufacturerId ManufacturerId { get; private set; }

    public DeviceModelName Name { get; private set; }

    // internal: Manufacturer.AddModel is the only caller.
    internal static DeviceModel Create(ManufacturerId manufacturerId, DeviceModelName name)
    {
        return new DeviceModel
        {
            Id = new DeviceModelInternalId(Guid.CreateVersion7()),
            ManufacturerId = manufacturerId,
            Name = name
        };
    }
}

public sealed record DeviceModelInternalId(Guid Value);

// MaxLength: the one number AddDeviceModelCommandValidator and NewDeviceModelFormModel's [StringLength] both
// read, so the limit is never repeated as a literal.
public sealed record DeviceModelName(string Value)
{
    public const int MaxLength = 200;
}
