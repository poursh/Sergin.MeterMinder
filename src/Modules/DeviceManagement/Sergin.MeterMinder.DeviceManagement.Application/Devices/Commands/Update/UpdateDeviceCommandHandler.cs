using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Update;

// A mutation of an existing aggregate, so the handler owns not-found. GetAsync hides a deleted device through
// the soft-delete query filter, so updating one is a not-found too.
internal sealed class UpdateDeviceCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IDeviceRepository repository) : ICommandHandler<UpdateDeviceCommand, UpdateDeviceCommandResponse>
{
    public async Task<ErrorOr<UpdateDeviceCommandResponse>> Handle(
        UpdateDeviceCommand request, CancellationToken cancellationToken)
    {
        Device? device = await repository.GetAsync(new DeviceIntenralId(request.Id), cancellationToken);

        if (device is null)
        {
            return Error.NotFound();
        }

        device.Update(request.DeviceId, request.DeviceModelId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateDeviceCommandResponse(device.Id.Value);
    }
}
