using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;

public sealed record CreateDeviceCommand(DeviceId DeviceId, ManufacturerId ManufacturerId) : ICommand<CreateDeviceCommandResponse>;
