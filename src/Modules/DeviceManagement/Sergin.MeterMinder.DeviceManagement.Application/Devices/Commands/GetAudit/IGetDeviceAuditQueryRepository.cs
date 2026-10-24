using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetAudit;

public interface IGetDeviceAuditQueryRepository
{
    Task<AuditInfo?> GetDeviceAuditAsync(DeviceIntenralId id, CancellationToken cancellationToken = default);
}
