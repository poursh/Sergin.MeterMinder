using FluentValidation;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Delete;

internal sealed class DeleteDeviceCommandValidator : AbstractValidator<DeleteDeviceCommand>
{
    public DeleteDeviceCommandValidator()
    {
        RuleFor(x => x.Id.Value).NotEmpty().OverridePropertyName(nameof(DeleteDeviceCommand.Id));
    }
}
