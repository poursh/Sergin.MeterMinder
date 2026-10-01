using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;

public sealed record GetDeviceByIdQueryCommand(Guid Id) : IQuery<DeviceQueryResponse>;
