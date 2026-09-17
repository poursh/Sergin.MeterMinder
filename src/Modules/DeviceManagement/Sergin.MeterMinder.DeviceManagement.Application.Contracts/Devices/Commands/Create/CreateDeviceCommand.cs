using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;

public sealed record CreateDeviceCommand(DeviceId DeviceId, DeviceModelInternalId DeviceModelId) : ICommand<CreateDeviceCommandResponse>;
