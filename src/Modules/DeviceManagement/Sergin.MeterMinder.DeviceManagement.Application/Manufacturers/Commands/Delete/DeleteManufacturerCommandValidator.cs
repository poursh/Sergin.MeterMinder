using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Delete;

// A soft delete fires no foreign key, so dm.device.device_model_id's RESTRICT no longer stops a manufacturer
// whose models are in use from going: this rule does. Advisory like every repository rule — a device created
// between this query and the save still lands on a deleted model.
internal sealed class DeleteManufacturerCommandValidator : AbstractValidator<DeleteManufacturerCommand>
{
    public DeleteManufacturerCommandValidator(IDeviceRepository devices)
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Id)
            .MustAsync(async (id, cancellationToken) =>
                !await devices.AnyUsingManufacturerAsync(new ManufacturerId(id), cancellationToken))
            .WithMessage("The manufacturer's models are still used by a device; delete those devices first.")
            .When(x => x.Id != Guid.Empty);
    }
}
