using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

public class Manufacturer : AggregateRoot<ManufacturerId>
{
    private readonly List<DeviceModel> models = [];

    private Manufacturer() { }

    public ManufacturerName Name { get; private set; }
    public ManufacturerAddress? Address { get; private set; }

    public IReadOnlyCollection<DeviceModel> Models => models;

    public static Manufacturer Create(ManufacturerName name, ManufacturerAddress? address = null)
    {
        return new Manufacturer
        {
            Id = new ManufacturerId(Guid.CreateVersion7()),
            Name = name,
            Address = address
        };
    }

    /// <summary>
    /// The only way a model comes into being. Refuses a name this manufacturer already uses — the invariant
    /// the aggregate exists to hold; <c>ix_device_model_manufacturer_id_name</c> is the guarantee under a race.
    /// Requires the manufacturer to have been loaded with its models
    /// (<c>IManufacturerRepository.GetWithModelsAsync</c>); on a manufacturer loaded through <c>GetAsync</c>
    /// the collection is empty and the check cannot see existing names.
    /// </summary>
    public ErrorOr<DeviceModel> AddModel(DeviceModelName name)
    {
        if (models.Any(model => model.Name == name))
        {
            // Error.Validation, so it renders through the path already built for validator errors:
            // Description shown, one snackbar in Blazor, one ValidationProblem entry on the API. The code and
            // text are what MustBeUniqueIn would have produced for a Name property.
            return Error.Validation(nameof(DeviceModel.Name), "'Name' is already in use.");
        }

        var model = DeviceModel.Create(Id, name);
        models.Add(model);

        return model;
    }
}

public sealed record ManufacturerId(Guid Value);

// MaxLength: read by CreateManufacturerCommandValidator. The columns are unbounded text today, so these
// are the only limits; widen here and the validator follows.
public sealed record ManufacturerName(string Value)
{
    public const int MaxLength = 200;
}

public sealed record ManufacturerAddress(string Value)
{
    public const int MaxLength = 500;
}
