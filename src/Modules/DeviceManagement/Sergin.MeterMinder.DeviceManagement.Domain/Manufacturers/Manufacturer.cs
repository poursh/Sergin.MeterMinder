using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
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

    public void Update(ManufacturerName name, ManufacturerAddress? address)
    {
        Name = name;
        Address = address;
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
        if (models.Exists(model => model.Name == name))
        {
            return NameInUse();
        }

        var model = DeviceModel.Create(Id, name);
        models.Add(model);

        return model;
    }

    /// <summary>
    /// Renames one of this manufacturer's models. Refuses a name another model of this manufacturer already uses,
    /// with the same error <see cref="AddModel"/> returns; renaming a model to its own current name succeeds.
    /// Requires the manufacturer to have been loaded with its models (<c>GetWithModelsAsync</c>).
    /// </summary>
    public ErrorOr<DeviceModel> RenameModel(DeviceModelInternalId id, DeviceModelName name)
    {
        DeviceModel? model = models.Find(candidate => candidate.Id == id);

        if (model is null)
        {
            return Error.NotFound();
        }

        if (models.Exists(other => other.Id != id && other.Name == name))
        {
            return NameInUse();
        }

        model.Rename(name);

        return model;
    }

    // Error.Validation, so it renders through the path already built for validator errors: Description shown,
    // one snackbar in Blazor, one ValidationProblem entry on the API. The code and text are what MustBeUniqueIn
    // would have produced for a Name property.
    private static Error NameInUse() => Error.Validation(nameof(DeviceModel.Name), "'Name' is already in use.");
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
