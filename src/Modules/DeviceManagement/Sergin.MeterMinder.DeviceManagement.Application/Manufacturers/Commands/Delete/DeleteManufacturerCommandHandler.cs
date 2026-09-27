using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Delete;

// Remove is a soft delete that SoftDeleteInterceptor cascades to the manufacturer's models; it would load
// them itself, but loading them here makes it one query rather than two.
internal sealed class DeleteManufacturerCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IManufacturerRepository repository) : ICommandHandler<DeleteManufacturerCommand, DeleteManufacturerCommandResponse>
{
    public async Task<ErrorOr<DeleteManufacturerCommandResponse>> Handle(
        DeleteManufacturerCommand request, CancellationToken cancellationToken)
    {
        Manufacturer? manufacturer = await repository.GetWithModelsAsync(new ManufacturerId(request.Id), cancellationToken);

        if (manufacturer is null)
        {
            return Error.NotFound();
        }

        repository.Remove(manufacturer);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeleteManufacturerCommandResponse(manufacturer.Id.Value);
    }
}
