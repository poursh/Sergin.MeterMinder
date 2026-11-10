using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;
using Sergin.SharedKernel.Application.Localizations.Validation;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Delete;

// A soft delete fires no foreign key, so dm.device.device_model_id's RESTRICT no longer stops a manufacturer
// whose models are in use from going: this rule does. Advisory like every repository rule — a device created
// between this query and the save still lands on a deleted model.
internal sealed class DeleteManufacturerCommandValidator : AbstractValidator<DeleteManufacturerCommand>
{
    public DeleteManufacturerCommandValidator(IDeviceRepository devices)
    {
        RuleFor(x => x.Id.Value).NotEmpty().OverridePropertyName(nameof(DeleteManufacturerCommand.Id));

        RuleFor(x => x.Id)
            .MustAsync(async (id, cancellationToken) =>
                !await devices.AnyUsingManufacturerAsync(id, cancellationToken))
            .WithMessage(_ => ValidationTexts.Template(ManufacturerTexts.InUse))
            .When(x => x.Id.Value != Guid.Empty);
    }
}
