using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;

public sealed record DeleteDeviceCommand(Guid Id) : ICommand<DeleteDeviceCommandResponse>;
