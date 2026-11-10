using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Rename;

// Shape only, like AddDeviceModelCommandValidator: not-found is the handler's and the aggregate's, and name
// uniqueness is Manufacturer.RenameModel's.
internal sealed class RenameDeviceModelCommandValidator : AbstractValidator<RenameDeviceModelCommand>
{
    public RenameDeviceModelCommandValidator()
    {
        RuleFor(x => x.RootId.Value)
            .NotEmpty()
            .OverridePropertyName(nameof(RenameDeviceModelCommand.RootId));

        RuleFor(x => x.Id.Value).NotEmpty().OverridePropertyName(nameof(RenameDeviceModelCommand.Id));

        RuleFor(x => x.Name.Value)
            .NotEmpty()
            .MaximumLength(DeviceModelName.MaxLength)
            .OverridePropertyName(nameof(RenameDeviceModelCommand.Name));
    }
}
