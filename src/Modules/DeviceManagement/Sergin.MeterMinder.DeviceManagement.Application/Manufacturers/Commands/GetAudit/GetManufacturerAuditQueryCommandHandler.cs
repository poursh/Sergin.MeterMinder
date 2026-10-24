using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetAudit;

internal sealed class GetManufacturerAuditQueryCommandHandler(IGetManufacturerAuditQueryRepository repository)
    : IQueryHandler<GetManufacturerAuditQueryCommand, AuditInfo>
{
    public async Task<ErrorOr<AuditInfo>> Handle(GetManufacturerAuditQueryCommand request, CancellationToken cancellationToken)
    {
        AuditInfo? audit = await repository.GetManufacturerAuditAsync(new ManufacturerId(request.Id), cancellationToken);

        if (audit is null)
        {
            return Error.NotFound();
        }

        return audit;
    }
}
