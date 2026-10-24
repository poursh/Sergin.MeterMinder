using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetAudit;

public interface IGetManufacturerAuditQueryRepository
{
    Task<AuditInfo?> GetManufacturerAuditAsync(ManufacturerId id, CancellationToken cancellationToken = default);
}
