using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Delete;

// Remove is a soft delete: Device is configured SoftDeletable(), so SoftDeleteInterceptor turns it into an
// UPDATE of deleted_at_utc/deleted_by. A device already deleted is not found by GetAsync, whose query filter
// hides it, so deleting twice is a not-found.
internal sealed class DeleteDeviceCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IDeviceRepository repository) : ICommandHandler<DeleteDeviceCommand, DeleteDeviceCommandResponse>
{
    public async Task<ErrorOr<DeleteDeviceCommandResponse>> Handle(
        DeleteDeviceCommand request, CancellationToken cancellationToken)
    {
        Device? device = await repository.GetAsync(new DeviceIntenralId(request.Id), cancellationToken);

        if (device is null)
        {
            return Error.NotFound();
        }

        repository.Remove(device);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeleteDeviceCommandResponse(device.Id.Value);
    }
}
