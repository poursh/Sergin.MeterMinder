using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;

// The version goes out beside the response, through the scope's ConcurrencyContext, never on the record.
internal sealed class GetManufacturerByIdQueryCommandHandler(IGetManufacturerQueryRepository repository, ConcurrencyContext concurrency)
    : IQueryHandler<GetManufacturerByIdQueryCommand, ManufacturerQueryResponse>
{
    public async Task<ErrorOr<ManufacturerQueryResponse>> Handle(GetManufacturerByIdQueryCommand request, CancellationToken cancellationToken)
    {
        Versioned<ManufacturerQueryResponse>? res = await repository.GetManufacturerById(new ManufacturerId(request.Id), cancellationToken);

        if (res is null)
        {
            return Error.NotFound();
        }

        concurrency.Current = res.Version;

        return res.Value;
    }
}
