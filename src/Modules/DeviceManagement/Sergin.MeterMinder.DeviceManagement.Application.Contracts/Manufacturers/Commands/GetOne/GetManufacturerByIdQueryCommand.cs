using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;

public sealed record GetManufacturerByIdQueryCommand(ManufacturerId Id)
    : IQuery<ManufacturerQueryResponse>, IGetOneQuery<Manufacturer, ManufacturerId>;
