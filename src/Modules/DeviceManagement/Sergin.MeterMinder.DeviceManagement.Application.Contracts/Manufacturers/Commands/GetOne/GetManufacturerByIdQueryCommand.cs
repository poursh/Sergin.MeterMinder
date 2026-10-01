using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;

public sealed record GetManufacturerByIdQueryCommand(Guid Id) : IQuery<ManufacturerQueryResponse>;
