using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;

// Shape only. Two rules are deliberately absent: manufacturer existence (the handler loads the aggregate and
// answers NotFound — a MustExistIn here would query for an answer the handler is about to get anyway) and name
// uniqueness (Manufacturer.AddModel holds that invariant; the composite unique index is the guarantee).
internal sealed class AddDeviceModelCommandValidator : AbstractValidator<AddDeviceModelCommand>
{
    public AddDeviceModelCommandValidator()
    {
        RuleFor(x => x.ManufacturerId.Value)
            .NotEmpty()
            .OverridePropertyName(nameof(AddDeviceModelCommand.ManufacturerId));

        RuleFor(x => x.Name.Value)
            .NotEmpty()
            .MaximumLength(DeviceModelName.MaxLength)
            .OverridePropertyName(nameof(AddDeviceModelCommand.Name));
    }
}
