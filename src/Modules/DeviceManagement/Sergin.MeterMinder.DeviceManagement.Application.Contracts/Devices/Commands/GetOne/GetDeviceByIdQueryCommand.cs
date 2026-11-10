using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;

public sealed record GetDeviceByIdQueryCommand(DeviceIntenralId Id)
    : IQuery<DeviceQueryResponse>, IGetOneQuery<Device, DeviceIntenralId>;
