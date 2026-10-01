using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Update;

internal sealed class UpdateManufacturerCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IManufacturerRepository repository) : ICommandHandler<UpdateManufacturerCommand, UpdateManufacturerCommandResponse>
{
    public async Task<ErrorOr<UpdateManufacturerCommandResponse>> Handle(
        UpdateManufacturerCommand request, CancellationToken cancellationToken)
    {
        Manufacturer? manufacturer = await repository.GetAsync(new ManufacturerId(request.Id), cancellationToken);

        if (manufacturer is null)
        {
            return Error.NotFound();
        }

        manufacturer.Update(request.Name, request.Address);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateManufacturerCommandResponse(manufacturer.Id.Value);
    }
}
