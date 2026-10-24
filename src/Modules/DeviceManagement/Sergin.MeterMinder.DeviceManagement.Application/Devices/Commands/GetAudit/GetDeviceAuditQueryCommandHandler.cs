using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetAudit;

internal sealed class GetDeviceAuditQueryCommandHandler(IGetDeviceAuditQueryRepository repository)
    : IQueryHandler<GetDeviceAuditQueryCommand, AuditInfo>
{
    public async Task<ErrorOr<AuditInfo>> Handle(GetDeviceAuditQueryCommand request, CancellationToken cancellationToken)
    {
        AuditInfo? audit = await repository.GetDeviceAuditAsync(new DeviceIntenralId(request.Id), cancellationToken);

        if (audit is null)
        {
            return Error.NotFound();
        }

        return audit;
    }
}
