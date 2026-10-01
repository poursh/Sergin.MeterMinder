using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Remove;

// A soft delete fires no foreign key, so dm.device.device_model_id's RESTRICT no longer stops a model in use
// from going: this rule does, as DeleteManufacturerCommandValidator does for a whole manufacturer. Advisory like
// every repository rule — a device created between this query and the save still lands on a deleted model.
internal sealed class RemoveDeviceModelCommandValidator : AbstractValidator<RemoveDeviceModelCommand>
{
    public RemoveDeviceModelCommandValidator(IDeviceRepository devices)
    {
        RuleFor(x => x.ManufacturerId.Value)
            .NotEmpty()
            .OverridePropertyName(nameof(RemoveDeviceModelCommand.ManufacturerId));

        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Id)
            .MustAsync(async (id, cancellationToken) =>
                !await devices.AnyUsingModelAsync(new DeviceModelInternalId(id), cancellationToken))
            .WithMessage("The model is still used by a device; delete those devices first.")
            .When(x => x.Id != Guid.Empty);
    }
}
