using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;

[RequiredPermissions("permission.dm.manufacturers.read")]
public sealed record GetManufacturerByIdQueryCommand(Guid Id) : IQuery<ManufacturerQueryResponse>;
