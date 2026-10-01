using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Remove;

internal sealed class RemoveDeviceModelCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IManufacturerRepository repository) : ICommandHandler<RemoveDeviceModelCommand, RemoveDeviceModelCommandResponse>
{
    public async Task<ErrorOr<RemoveDeviceModelCommandResponse>> Handle(
        RemoveDeviceModelCommand request, CancellationToken cancellationToken)
    {
        // GetWithModelsAsync, not GetAsync: RemoveModel finds the model in the loaded collection.
        Manufacturer? manufacturer = await repository.GetWithModelsAsync(request.ManufacturerId, cancellationToken);

        if (manufacturer is null)
        {
            return Error.NotFound();
        }

        ErrorOr<Deleted> removed = manufacturer.RemoveModel(new DeviceModelInternalId(request.Id));

        if (removed.IsError)
        {
            return removed.Errors;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RemoveDeviceModelCommandResponse(request.Id);
    }
}
