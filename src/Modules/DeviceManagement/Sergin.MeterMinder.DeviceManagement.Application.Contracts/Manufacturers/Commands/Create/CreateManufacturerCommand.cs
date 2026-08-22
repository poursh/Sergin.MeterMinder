using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;

public sealed record CreateManufacturerCommand(ManufacturerName Name, ManufacturerAddress? Address) : ICommand<CreateManufacturerCommandResponse>;
