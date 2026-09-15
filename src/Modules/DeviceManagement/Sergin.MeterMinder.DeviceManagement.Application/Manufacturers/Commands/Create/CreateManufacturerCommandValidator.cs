using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;

internal sealed class CreateManufacturerCommandValidator : AbstractValidator<CreateManufacturerCommand>
{
    public CreateManufacturerCommandValidator()
    {
        RuleFor(x => x.Name.Value)
            .NotEmpty()
            .MaximumLength(ManufacturerName.MaxLength)
            .OverridePropertyName(nameof(CreateManufacturerCommand.Name));

        // Optional, but when supplied it has to be a real value — an empty string is not "no address".
        RuleFor(x => x.Address!.Value)
            .NotEmpty()
            .MaximumLength(ManufacturerAddress.MaxLength)
            .OverridePropertyName(nameof(CreateManufacturerCommand.Address))
            .When(x => x.Address is not null);
    }
}
