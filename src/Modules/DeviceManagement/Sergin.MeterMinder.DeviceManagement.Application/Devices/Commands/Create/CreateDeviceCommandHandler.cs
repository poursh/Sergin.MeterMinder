using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;

internal sealed class CreateDeviceCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IDeviceRepository repository) : ICommandHandler<CreateDeviceCommand, CreateDeviceCommandResponse>
{
    public async Task<ErrorOr<CreateDeviceCommandResponse>> Handle(
        CreateDeviceCommand request, CancellationToken cancellationToken)
    {
        var newDevice = Device.Create(request.DeviceId, request.ManufacturerId);

        repository.Insert(newDevice);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateDeviceCommandResponse(newDevice.Id.Value);
    }
}
