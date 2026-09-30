using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Concurrency;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;

// The version goes out beside the response, through the scope's ConcurrencyContext, never on the record.
internal sealed class GetManufacturerByIdQueryCommandHandler(IGetManufacturerQueryRepository repository, ConcurrencyContext concurrency)
    : VersionedQueryHandler<GetManufacturerByIdQueryCommand, ManufacturerQueryResponse>(concurrency)
{
    public override async Task<ErrorOr<Versioned<ManufacturerQueryResponse>>> HandleVersioned(GetManufacturerByIdQueryCommand request, CancellationToken cancellationToken)
    {
        Versioned<ManufacturerQueryResponse>? res = await repository.GetManufacturerById(new ManufacturerId(request.Id), cancellationToken);

        if (res is null)
        {
            return Error.NotFound();
        }

        return res;
    }
}
