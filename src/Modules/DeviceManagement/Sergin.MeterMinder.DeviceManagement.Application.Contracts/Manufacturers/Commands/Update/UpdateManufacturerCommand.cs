using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;

public sealed record UpdateManufacturerCommand(Guid Id, ManufacturerName Name, ManufacturerAddress? Address)
    : ICommand<UpdateManufacturerCommandResponse>;
