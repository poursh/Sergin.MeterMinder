using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Validations;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;

// Shape rules target the value object's Value and OverridePropertyName restores the command property name:
// that name is what ValidationPipelineBehavior puts in Error.Code and what the API groups a
// ValidationProblem by.
//
// The repository rules target the wrapper itself, so their Error.Code is already the property name. Whether
// the manufacturer exists and whether the device id is free are answered here, as ErrorOr validation errors;
// the foreign key and the unique index on dm.device.device_id stay as the guarantee under a race. Each is
// guarded by a When so no query runs for a value the shape rule has already refused.
internal sealed class CreateDeviceCommandValidator : AbstractValidator<CreateDeviceCommand>
{
    public CreateDeviceCommandValidator(IDeviceRepository devices, IManufacturerRepository manufacturers)
    {
        RuleFor(x => x.DeviceId.Value)
            .NotEmpty()
            .MaximumLength(DeviceId.MaxLength)
            .OverridePropertyName(nameof(CreateDeviceCommand.DeviceId));

        RuleFor(x => x.ManufacturerId.Value)
            .NotEmpty()
            .OverridePropertyName(nameof(CreateDeviceCommand.ManufacturerId));

        RuleFor(x => x.DeviceId)
            .MustBeUniqueIn(devices)
            .When(x => !string.IsNullOrWhiteSpace(x.DeviceId.Value));

        RuleFor(x => x.ManufacturerId)
            .MustExistIn(manufacturers)
            .When(x => x.ManufacturerId.Value != Guid.Empty);
    }
}
