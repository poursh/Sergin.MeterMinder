using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;

internal sealed class CreateManufacturerCommandHandler(
    IDeviceManagementUnitOfWork unitOfWork,
    IManufacturerRepository repository) : ICommandHandler<CreateManufacturerCommand, CreateManufacturerCommandResponse>
{
    public async Task<ErrorOr<CreateManufacturerCommandResponse>> Handle(
        CreateManufacturerCommand request, CancellationToken cancellationToken)
    {
        var newManufacturer = Manufacturer.Create(request.Name, request.Address);

        repository.Insert(newManufacturer);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateManufacturerCommandResponse(newManufacturer.Id.Value);
    }
}
