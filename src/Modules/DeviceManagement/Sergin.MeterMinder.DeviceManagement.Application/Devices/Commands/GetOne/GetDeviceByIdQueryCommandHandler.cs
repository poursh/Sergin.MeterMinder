using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

// The version goes out beside the response, through the scope's ConcurrencyContext, never on the record.
internal sealed class GetDeviceByIdQueryCommandHandler(IGetDeviceQueryRepository repository, ConcurrencyContext concurrency)
    : IQueryHandler<GetDeviceByIdQueryCommand, DeviceQueryResponse>
{
    public async Task<ErrorOr<DeviceQueryResponse>> Handle(GetDeviceByIdQueryCommand request, CancellationToken cancellationToken)
    {
        Versioned<DeviceQueryResponse>? res = await repository.GetDeviceById(new DeviceIntenralId(request.Id), cancellationToken);

        if (res is null)
        {
            return Error.NotFound();
        }

        concurrency.Current = res.Version;

        return res.Value;
    }
}
