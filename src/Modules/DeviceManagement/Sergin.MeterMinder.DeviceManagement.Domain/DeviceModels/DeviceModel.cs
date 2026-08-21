using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.DeviceManagement.Domain.DeviceModels;

public sealed class DeviceModel : AggregateRoot<DeviceModelInternalId>
{
    public DeviceModelName Name { get; private set; }

    public static DeviceModel Create(DeviceModelName name)
    {
        return new DeviceModel
        {
            Id = new DeviceModelInternalId(Guid.CreateVersion7()),
            Name = name
        };
    }
}

public sealed record DeviceModelInternalId(Guid Value);

public sealed record DeviceModelName(string Value);
