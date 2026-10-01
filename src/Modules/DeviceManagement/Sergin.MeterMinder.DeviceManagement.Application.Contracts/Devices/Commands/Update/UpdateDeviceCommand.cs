using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;

public sealed record UpdateDeviceCommand(Guid Id, DeviceId DeviceId, DeviceModelInternalId DeviceModelId)
    : ICommand<UpdateDeviceCommandResponse>;
