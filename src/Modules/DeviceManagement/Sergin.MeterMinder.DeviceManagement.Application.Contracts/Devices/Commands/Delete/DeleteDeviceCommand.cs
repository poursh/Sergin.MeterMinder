using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;

public sealed record DeleteDeviceCommand(DeviceIntenralId Id)
    : ICommand<DeleteDeviceCommandResponse>, IDeleteCommand<Device, DeviceIntenralId>, ISelectable;
