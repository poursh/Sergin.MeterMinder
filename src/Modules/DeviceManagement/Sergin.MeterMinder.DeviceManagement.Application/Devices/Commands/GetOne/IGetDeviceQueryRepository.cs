using Sergin.MeterMinder.DeviceManagement.Domain.Devices;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

public interface IGetDeviceQueryRepository
{
    Task<DeviceQueryResponse?> GetDeviceById(DeviceIntenralId Id, CancellationToken cancellationToken = default);
}
