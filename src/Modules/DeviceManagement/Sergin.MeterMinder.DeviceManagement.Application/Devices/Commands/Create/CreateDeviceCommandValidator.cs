using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;

// Rules target the value object's Value and OverridePropertyName restores the command property name:
// that name is what ValidationPipelineBehavior puts in Error.Code and what the API groups a
// ValidationProblem by. Whether the manufacturer exists is still the foreign key's job — a bad id
// surfaces as a Postgres FK violation, not an ErrorOr result (see CLAUDE.md, "No FK-existence check").
internal sealed class CreateDeviceCommandValidator : AbstractValidator<CreateDeviceCommand>
{
    public CreateDeviceCommandValidator()
    {
        RuleFor(x => x.DeviceId.Value)
            .NotEmpty()
            .MaximumLength(DeviceId.MaxLength)
            .OverridePropertyName(nameof(CreateDeviceCommand.DeviceId));

        RuleFor(x => x.ManufacturerId.Value)
            .NotEmpty()
            .OverridePropertyName(nameof(CreateDeviceCommand.ManufacturerId));
    }
}
