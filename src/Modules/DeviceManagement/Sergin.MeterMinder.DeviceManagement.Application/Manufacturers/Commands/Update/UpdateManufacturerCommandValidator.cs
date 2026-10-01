using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Update;

// CreateManufacturerCommandValidator's shape rules plus the id. Manufacturer names are not unique, so there is
// no repository rule; not-found is the handler's.
internal sealed class UpdateManufacturerCommandValidator : AbstractValidator<UpdateManufacturerCommand>
{
    public UpdateManufacturerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Name.Value)
            .NotEmpty()
            .MaximumLength(ManufacturerName.MaxLength)
            .OverridePropertyName(nameof(UpdateManufacturerCommand.Name));

        // Optional, but when supplied it has to be a real value — an empty string is not "no address".
        RuleFor(x => x.Address!.Value)
            .NotEmpty()
            .MaximumLength(ManufacturerAddress.MaxLength)
            .OverridePropertyName(nameof(UpdateManufacturerCommand.Address))
            .When(x => x.Address is not null);
    }
}
