using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Devices;

public class Device : AggregateRoot<DeviceIntenralId>
{
    private Device() { }

    public DeviceId DeviceId { get; private set; }

    // A reference across the aggregate boundary to an entity inside Manufacturer. The manufacturer itself is
    // reachable only through the model — Device deliberately does not store it twice.
    public DeviceModelInternalId DeviceModelId { get; private set; }

    public static Device Create(DeviceId deviceId, DeviceModelInternalId deviceModelId)
    {
        return new Device
        {
            Id = new DeviceIntenralId(Guid.CreateVersion7()),
            DeviceId = deviceId,
            DeviceModelId = deviceModelId
        };
    }
}

public sealed record DeviceIntenralId(Guid Value);

// MaxLength: the one number CreateDeviceCommandValidator and NewDeviceFormModel's [StringLength] both
// read, so the limit is never repeated as a literal.
public sealed record DeviceId(string Value)
{
    public const int MaxLength = 100;
}
