using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;

// A mutation of an existing aggregate, so the handler owns not-found (the DeactivateUser shape) and the
// aggregate owns the uniqueness rule. The validator checks shape only.
internal sealed class AddDeviceModelCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IManufacturerRepository repository) : ICommandHandler<AddDeviceModelCommand, AddDeviceModelCommandResponse>
{
    public async Task<ErrorOr<AddDeviceModelCommandResponse>> Handle(
        AddDeviceModelCommand request, CancellationToken cancellationToken)
    {
        // GetWithModelsAsync, not GetAsync: AddModel checks the name against the loaded collection.
        Manufacturer? manufacturer = await repository.GetWithModelsAsync(request.ManufacturerId, cancellationToken);

        if (manufacturer is null)
        {
            return Error.NotFound();
        }

        ErrorOr<DeviceModel> added = manufacturer.AddModel(request.Name);

        if (added.IsError)
        {
            return added.Errors;
        }

        // EF tracks the new entity through the Models navigation of the tracked manufacturer; nothing is
        // inserted explicitly.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AddDeviceModelCommandResponse(added.Value.Id.Value);
    }
}
