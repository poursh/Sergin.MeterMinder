using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Delete;

[RequiredPermissions("permission.dm.devices.delete")]
[RequiresExpectedVersion]
public sealed record DeleteDeviceCommand(Guid Id) : ICommand<DeleteDeviceCommandResponse>;
