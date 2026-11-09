using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Delete;

public sealed record DeleteManufacturerCommand(ManufacturerId Id)
    : ICommand<DeleteManufacturerCommandResponse>, IDeleteCommand<Manufacturer, ManufacturerId>, ISelectable;
