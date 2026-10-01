using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Rename;

// The AddDeviceModel shape: the handler owns not-found for the manufacturer, the aggregate owns not-found for
// the model and the name-uniqueness invariant.
internal sealed class RenameDeviceModelCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IManufacturerRepository repository) : ICommandHandler<RenameDeviceModelCommand, RenameDeviceModelCommandResponse>
{
    public async Task<ErrorOr<RenameDeviceModelCommandResponse>> Handle(
        RenameDeviceModelCommand request, CancellationToken cancellationToken)
    {
        // GetWithModelsAsync, not GetAsync: RenameModel checks the name against the loaded collection.
        Manufacturer? manufacturer = await repository.GetWithModelsAsync(request.ManufacturerId, cancellationToken);

        if (manufacturer is null)
        {
            return Error.NotFound();
        }

        ErrorOr<DeviceModel> renamed = manufacturer.RenameModel(new DeviceModelInternalId(request.Id), request.Name);

        if (renamed.IsError)
        {
            return renamed.Errors;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RenameDeviceModelCommandResponse(renamed.Value.Id.Value);
    }
}
