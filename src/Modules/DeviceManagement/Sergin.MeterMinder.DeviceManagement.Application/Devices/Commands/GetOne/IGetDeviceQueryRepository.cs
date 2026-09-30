using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Concurrency;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

public interface IGetDeviceQueryRepository
{
    Task<Versioned<DeviceQueryResponse>?> GetDeviceById(DeviceIntenralId Id, CancellationToken cancellationToken = default);
}
