using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;

[RequiredPermissions("permission.dm.devices.update")]
[RequiresExpectedVersion]
public sealed record UpdateDeviceCommand(Guid Id, DeviceId DeviceId, DeviceModelInternalId DeviceModelId)
    : ICommand<UpdateDeviceCommandResponse>;
