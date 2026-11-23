using Sergin.MeterMinder.DeviceManagement.Domain.Devices;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;

public sealed record ReadDeviceProfileCommand(DeviceIntenralId Id)
    : ITaskCommand<Device, DeviceIntenralId>, ISelectable;
