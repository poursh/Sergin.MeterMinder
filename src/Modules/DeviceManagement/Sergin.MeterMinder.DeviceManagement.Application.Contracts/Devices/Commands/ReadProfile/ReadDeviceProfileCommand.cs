using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;

// A transition, though it changes no field: the type must name one entity target, from which the root, the scope check and
// the schedulable target derive, and no other effect interface fits a command that only acts on the record.
public sealed record ReadDeviceProfileCommand(DeviceIntenralId Id)
    : ICommand, ITransitionCommand<Device, DeviceIntenralId>, ISelectable;
