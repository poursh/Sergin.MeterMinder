using FluentValidation;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Update;

// CreateDeviceCommandValidator's rules, with one difference: uniqueness excludes the device itself, so a save
// that keeps the device's own id passes. That is a local MustAsync over IsTakenByOtherAsync rather than
// MustBeUniqueIn, whose IsTakenAsync cannot tell "taken by me" from "taken by another"; the message is
// MustBeUniqueIn's so the two read alike.
internal sealed class UpdateDeviceCommandValidator : AbstractValidator<UpdateDeviceCommand>
{
    public UpdateDeviceCommandValidator(IDeviceRepository devices, IManufacturerRepository manufacturers)
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.DeviceId.Value)
            .NotEmpty()
            .MaximumLength(DeviceId.MaxLength)
            .OverridePropertyName(nameof(UpdateDeviceCommand.DeviceId));

        RuleFor(x => x.DeviceModelId.Value)
            .NotEmpty()
            .OverridePropertyName(nameof(UpdateDeviceCommand.DeviceModelId));

        RuleFor(x => x.DeviceId)
            .MustAsync(async (command, deviceId, cancellationToken) =>
                !await devices.IsTakenByOtherAsync(deviceId, new DeviceIntenralId(command.Id), cancellationToken))
            .WithMessage("'{PropertyName}' is already in use.")
            .When(x => !string.IsNullOrWhiteSpace(x.DeviceId.Value) && x.Id != Guid.Empty);

        RuleFor(x => x.DeviceModelId)
            .MustAsync(manufacturers.ModelExistsAsync)
            .WithMessage("'{PropertyName}' must refer to an existing DeviceModel.")
            .When(x => x.DeviceModelId.Value != Guid.Empty);
    }
}
