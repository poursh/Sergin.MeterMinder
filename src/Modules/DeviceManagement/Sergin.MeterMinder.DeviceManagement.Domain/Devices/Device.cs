using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Devices;

public class Device : AggregateRoot<DeviceIntenralId>
{
    private Device() { }

    public DeviceId DeviceId { get; private set; }
    public ManufacturerId ManufacturerId { get; private set; }
    ///public DeviceModelInternalId ModelId { get; private set; }
    ///public virtual DeviceModel Model { get; private set; }

    ///public static Device Create(DeviceModelInternalId modelId)
    ///{
    ///    return new Device
    ///    {
    ///        Id = new DeviceIntenralId(Guid.CreateVersion7()),
    ///        ModelId = modelId
    ///    };
    ///}

    public static Device Create(DeviceId deviceId, ManufacturerId manufacturerId)
    {
        return new Device
        {
            Id = new DeviceIntenralId(Guid.CreateVersion7()),
            DeviceId = deviceId,
            ManufacturerId = manufacturerId
        };
    }
}

public sealed record DeviceIntenralId(Guid Value);
public sealed record DeviceId(string Value);
