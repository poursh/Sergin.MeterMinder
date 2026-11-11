using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;

public sealed record ReadDeviceProfileCommand(DeviceIntenralId Id)
    : ICommand, ITransitionCommand<Device, DeviceIntenralId>, ISelectable;
