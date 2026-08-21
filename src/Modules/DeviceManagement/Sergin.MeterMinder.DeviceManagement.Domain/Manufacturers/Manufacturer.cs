using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

public class Manufacturer : AggregateRoot<ManufacturerId>
{
    private Manufacturer() { }

    public ManufacturerName Name { get; private set; }
    public ManufacturerAddress? Address { get; private set; }

    public static Manufacturer Create(ManufacturerName name, ManufacturerAddress? address = null)
    {
        return new Manufacturer
        {
            Id = new ManufacturerId(Guid.CreateVersion7()),
            Name = name,
            Address = address
        };
    }
}

public sealed record ManufacturerId(Guid Value);
public sealed record ManufacturerName(string Value);
public sealed record ManufacturerAddress(string Value);
