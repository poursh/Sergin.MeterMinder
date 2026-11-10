using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;

public sealed record UpdateManufacturerCommand(ManufacturerId Id, ManufacturerName Name, ManufacturerAddress? Address)
    : ICommand<UpdateManufacturerCommandResponse>, IUpdateCommand<Manufacturer, ManufacturerId>;
