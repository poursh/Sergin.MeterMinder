using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

[RequiredPermissions("permission.dm.devices.read")]
public sealed record GetDeviceByIdQueryCommand(Guid Id) : IQuery<DeviceQueryResponse>;
